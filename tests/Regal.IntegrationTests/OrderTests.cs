using Microsoft.EntityFrameworkCore;
using Npgsql;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Common;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Orders;

namespace Regal.IntegrationTests;

/// <summary>Customer orders and their reservations (SPEC 5.11).</summary>
[Collection(PostgresCollection.Name)]
public class OrderTests(PostgresFixture postgres) : InvoiceTestBase(postgres)
{
    private CustomerOrderService Orders => new(Postgres.DbContextFactory, new FixedUser(UserId), Clock);

    private OrderDraft Order(params OrderDraftLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), ContractorId = SupplierId, Lines = [.. lines] };

    private static OrderDraftLine Ordered(int productId, decimal quantity) => new() { ProductId = productId, Quantity = quantity };

    private async Task<int> OrderOkAsync(OrderDraft draft)
    {
        var result = await Orders.CreateAsync(draft);
        Assert.True(result.IsSuccess, result.Error);
        return result.Id!.Value;
    }

    private async Task<CustomerOrder> OrderAsync(int id)
    {
        await using var db = Postgres.CreateAppDbContext();
        return await db.CustomerOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == id);
    }

    private InvoiceDraft InvoiceFor(int orderId, params InvoiceDraftLine[] lines)
    {
        var draft = Draft(lines);
        draft.OrderId = orderId;
        return draft;
    }

    [Fact]
    public async Task An_order_is_numbered_open_and_reserves_its_goods()
    {
        var goods = await StockedGoodsAsync(10);
        var service = await NewServiceAsync();

        var id = await OrderOkAsync(Order(Ordered(goods, 4), Ordered(service, 2)));

        var order = await OrderAsync(id);
        Assert.Equal(CustomerOrderStatus.Open, order.Status);
        Assert.Matches(@"^ZK/\d+/2026$", order.Number);
        Assert.Equal(2, order.Lines.Count);
        var details = (await Orders.GetDetailsAsync(id))!;
        Assert.Equal(10m, details.Lines.Single(l => l.ProductId == goods).Available);
        Assert.Null(details.Lines.Single(l => l.ProductId == service).Available);
        Assert.Equal(10m, await StockOfAsync(goods)); // a reservation does not move stock
    }

    [Fact]
    public async Task Stock_list_shows_what_open_orders_reserve()
    {
        var goods = await StockedGoodsAsync(10);
        var sku = (await Products.GetForEditAsync(goods))!.Sku!;
        await OrderOkAsync(Order(Ordered(goods, 3)));
        await Orders.CancelAsync(await OrderOkAsync(Order(Ordered(goods, 2))));

        var row = Assert.Single((await Stock.SearchAsync(new Regal.Web.Features.Warehouse.StockFilter(null, sku, null), 0, 10)).Items);

        Assert.Equal((10m, 3m), (row.Quantity, row.Reserved));
    }

    [Fact]
    public async Task Order_numbers_follow_one_another_without_gaps_even_after_a_refusal()
    {
        var goods = await StockedGoodsAsync(5);
        var first = await OrderAsync(await OrderOkAsync(Order(Ordered(goods, 1))));

        Assert.False((await Orders.CreateAsync(Order(Ordered(goods, 50)))).IsSuccess);
        var second = await OrderAsync(await OrderOkAsync(Order(Ordered(goods, 1))));

        Assert.Equal(SequenceOf(first.Number) + 1, SequenceOf(second.Number));
    }

    [Fact]
    public async Task An_order_cannot_reserve_more_than_is_available_for_sale()
    {
        var goods = await StockedGoodsAsync(10);
        await OrderOkAsync(Order(Ordered(goods, 7)));

        var result = await Orders.CreateAsync(Order(Ordered(goods, 4)));

        Assert.False(result.IsSuccess);
        Assert.Contains("najwyżej 3", result.Error);
        await OrderOkAsync(Order(Ordered(goods, 3)));
    }

    [Fact]
    public async Task Expired_batches_cannot_be_reserved()
    {
        var goods = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(goods, 5, 2m, "STARA", Today.AddDays(1)), In(goods, 5, 2m, "NOWA", Today.AddDays(30))));
        Clock.Advance(TimeSpan.FromDays(3));

        var result = await Orders.CreateAsync(Order(Ordered(goods, 6)));

        Assert.False(result.IsSuccess);
        Assert.Contains("najwyżej 5", result.Error);
    }

    [Fact]
    public async Task A_manual_wz_cannot_take_goods_reserved_for_an_order()
    {
        var goods = await StockedGoodsAsync(10);
        await OrderOkAsync(Order(Ordered(goods, 8)));

        var refused = await Documents.PostAsync(Wz(Out(goods, 3)));
        var preview = Assert.Single(await Documents.PreviewIssueAsync(Wz(Out(goods, 3))));

        Assert.False(refused.IsSuccess);
        Assert.Contains("zarezerwowano", refused.Error);
        Assert.Contains("zarezerwowano", preview.Error);
        await PostOkAsync(Wz(Out(goods, 2)));
        Assert.Equal(8m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task An_invoice_without_the_order_cannot_take_reserved_goods()
    {
        var goods = await StockedGoodsAsync(10);
        await OrderOkAsync(Order(Ordered(goods, 8)));

        var result = await Invoices.IssueAsync(Draft(Item(goods, 5)));

        Assert.False(result.IsSuccess);
        Assert.Contains("zarezerwowano", result.Error);
    }

    [Fact]
    public async Task Rw_ignores_reservations_and_the_order_shows_what_is_missing()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync(Order(Ordered(goods, 8)));

        await PostOkAsync(Rw(Out(goods, 6)));

        var line = (await Orders.GetDetailsAsync(id))!.Lines.Single();
        Assert.Equal((4m, 4m), (line.Available!.Value, line.Missing));
    }

    [Fact]
    public async Task An_invoice_from_the_order_uses_its_reservation_and_completes_the_order()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync(Order(Ordered(goods, 10)));

        // The preview of the order's own invoice must not count its reservation against it.
        var draft = InvoiceFor(id, Item(goods, 10));
        Assert.Null(Assert.Single(await Documents.PreviewIssueAsync(InvoicePayload.IssueDraftFor(draft, [goods]))).Error);
        var invoiceId = await IssueOkAsync(draft);

        var order = await OrderAsync(id);
        Assert.Equal((CustomerOrderStatus.Completed, invoiceId), (order.Status, order.InvoiceId));
        Assert.Equal(0m, await StockOfAsync(goods));
        Assert.True((await Orders.CancelAsync(id)) is { IsSuccess: false, Error: var error } && error!.Contains("zrealizowane"));
    }

    [Fact]
    public async Task An_invoice_from_the_order_may_change_quantities_and_still_completes_it()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync(Order(Ordered(goods, 6)));

        await IssueOkAsync(InvoiceFor(id, Item(goods, 4)));

        Assert.Equal(CustomerOrderStatus.Completed, (await OrderAsync(id)).Status);
        await PostOkAsync(Wz(Out(goods, 6))); // nothing stays reserved
    }

    [Fact]
    public async Task An_order_is_completed_once_and_only_for_its_buyer()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync(Order(Ordered(goods, 2)));

        var otherBuyer = InvoiceFor(id, Item(goods, 2));
        otherBuyer.ContractorId = await NewContractorAsync();
        var refused = await Invoices.IssueAsync(otherBuyer);
        await IssueOkAsync(InvoiceFor(id, Item(goods, 2)));
        var second = await Invoices.IssueAsync(InvoiceFor(id, Item(goods, 2)));

        Assert.Contains("tego samego nabywcę", refused.Error);
        Assert.Contains("zrealizowane", second.Error);
        Assert.Equal(8m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Sending_the_same_invoice_from_an_order_twice_returns_the_first_one()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync(Order(Ordered(goods, 2)));
        var draft = InvoiceFor(id, Item(goods, 2));

        var first = await IssueOkAsync(draft);
        var second = await IssueOkAsync(draft);

        Assert.Equal(first, second);
        Assert.Equal(1, await InvoiceCountAsync(draft.RequestId));
    }

    [Fact]
    public async Task A_failed_invoice_from_the_order_leaves_the_order_open()
    {
        var goods = await StockedGoodsAsync(5);
        var id = await OrderOkAsync(Order(Ordered(goods, 5)));

        var result = await Invoices.IssueAsync(InvoiceFor(id, Item(goods, 6)));

        Assert.False(result.IsSuccess);
        Assert.Equal(CustomerOrderStatus.Open, (await OrderAsync(id)).Status);
    }

    [Fact]
    public async Task Cancelling_releases_the_reservation_and_can_be_repeated()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync(Order(Ordered(goods, 10)));

        Assert.True((await Orders.CancelAsync(id)).IsSuccess);
        Assert.True((await Orders.CancelAsync(id)).IsSuccess);

        Assert.Equal(CustomerOrderStatus.Cancelled, (await OrderAsync(id)).Status);
        await PostOkAsync(Wz(Out(goods, 10)));
        var invoice = await Invoices.IssueAsync(InvoiceFor(id, Item(goods, 1)));
        Assert.Contains("anulowane", invoice.Error);
    }

    [Fact]
    public async Task Sending_the_same_order_twice_creates_one_order()
    {
        var goods = await StockedGoodsAsync(10);
        var draft = Order(Ordered(goods, 3));

        var first = await OrderOkAsync(draft);
        var second = await OrderOkAsync(draft);

        Assert.Equal(first, second);
        await using var db = Postgres.CreateAppDbContext();
        Assert.Equal(1, await db.CustomerOrders.CountAsync(o => o.RequestId == draft.RequestId));
    }

    [Theory]
    [InlineData(null, "Wybierz nabywcę")]
    [InlineData(0, "co najmniej jedną")]
    public async Task Order_drafts_are_validated(int? lines, string message)
    {
        var goods = await StockedGoodsAsync(10);
        var draft = Order(Ordered(goods, 1));
        if (lines is null)
        {
            draft.ContractorId = null;
        }
        else
        {
            draft.Lines.Clear();
        }

        var result = await Orders.CreateAsync(draft);

        Assert.Contains(message, result.Error);
    }

    [Fact]
    public async Task Inactive_products_and_wrong_quantities_are_refused()
    {
        var goods = await StockedGoodsAsync(10);
        var inactive = await NewGoodsAsync();
        Assert.True((await Products.SetActiveAsync(inactive, false)).IsSuccess);

        Assert.Contains("nieaktywny", (await Orders.CreateAsync(Order(Ordered(inactive, 1)))).Error);
        Assert.False((await Orders.CreateAsync(Order(Ordered(goods, 1.5m)))).IsSuccess);
        Assert.Contains("tylko raz", (await Orders.CreateAsync(Order(Ordered(goods, 1), Ordered(goods, 2)))).Error);
    }

    [Fact]
    public async Task Parallel_orders_never_reserve_more_than_the_stock()
    {
        var goods = await StockedGoodsAsync(10);
        var service = Orders;

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => service.CreateAsync(Order(Ordered(goods, 3))))));

        Assert.Equal(3, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Contains("najwyżej", r.Error));
    }

    [Fact]
    public async Task Parallel_orders_and_wz_never_promise_goods_that_left()
    {
        var goods = await StockedGoodsAsync(10);
        var orders = Orders;
        var documents = Documents;

        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() => i % 2 == 0
            ? orders.CreateAsync(Order(Ordered(goods, 2)))
            : documents.PostAsync(Wz(Out(goods, 2)))));
        var results = await Task.WhenAll(tasks);

        // Whatever the order of events, what is reserved never exceeds what is left.
        await using var db = Postgres.CreateAppDbContext();
        var reserved = await db.CustomerOrderLines
            .Where(l => l.ProductId == goods && l.Order!.Status == CustomerOrderStatus.Open)
            .SumAsync(l => l.Quantity);
        Assert.True(reserved <= await StockOfAsync(goods), $"Reserved {reserved}, left {await StockOfAsync(goods)}.");
        Assert.Equal(5, results.Count(r => r.IsSuccess));
    }

    [Fact]
    public async Task Cancelling_and_invoicing_one_order_at_the_same_time_leaves_one_outcome()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync(Order(Ordered(goods, 4)));
        var orders = Orders;
        var invoices = Invoices;

        var cancel = Task.Run(() => orders.CancelAsync(id));
        var invoice = Task.Run(() => invoices.IssueAsync(InvoiceFor(id, Item(goods, 4))));
        var results = await Task.WhenAll(cancel, invoice);

        var order = await OrderAsync(id);
        if (order.Status == CustomerOrderStatus.Completed)
        {
            Assert.True(results[1].IsSuccess);
            Assert.False(results[0].IsSuccess);
            Assert.Equal(6m, await StockOfAsync(goods));
        }
        else
        {
            Assert.Equal(CustomerOrderStatus.Cancelled, order.Status);
            Assert.True(results[0].IsSuccess);
            Assert.False(results[1].IsSuccess);
            Assert.Equal(10m, await StockOfAsync(goods));
        }
    }

    [Theory]
    [InlineData("UPDATE \"CustomerOrders\" SET \"Notes\" = 'zmiana'")]
    [InlineData("UPDATE \"CustomerOrders\" SET \"ContractorId\" = \"ContractorId\"")]
    [InlineData("DELETE FROM \"CustomerOrders\"")]
    [InlineData("UPDATE \"CustomerOrderLines\" SET \"Quantity\" = 1")]
    [InlineData("DELETE FROM \"CustomerOrderLines\"")]
    public async Task Application_role_cannot_rewrite_or_delete_orders(string sql)
    {
        await using var db = Postgres.CreateAppDbContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task Orders_are_found_by_number_buyer_and_status()
    {
        var goods = await StockedGoodsAsync(10);
        var open = await OrderOkAsync(Order(Ordered(goods, 1)));
        var cancelled = await OrderOkAsync(Order(Ordered(goods, 1)));
        await Orders.CancelAsync(cancelled);
        var number = (await OrderAsync(open)).Number;

        var byNumber = await Orders.SearchAsync(new OrderFilter(null, number), 0, 10);
        var cancelledOnes = await Orders.SearchAsync(new OrderFilter(CustomerOrderStatus.Cancelled, null), 0, 50);

        Assert.Equal(open, Assert.Single(byNumber.Items).Id);
        Assert.Contains(cancelledOnes.Items, o => o.Id == cancelled);
        Assert.DoesNotContain(cancelledOnes.Items, o => o.Id == open);
    }
}
