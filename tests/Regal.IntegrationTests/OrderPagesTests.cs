using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Orders;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class OrderPagesTests : InvoiceTestBase
{
    private readonly BunitContext _context = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public OrderPagesTests(PostgresFixture postgres)
        : base(postgres)
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Services.AddSingleton<TimeProvider>(Clock);
        _context.Services.AddScoped(_ => Orders);
        _context.Services.AddScoped(_ => Invoices);
        _context.Services.AddScoped(_ => Documents);
        _context.Services.AddScoped(_ => Stock);
        _context.Services.AddScoped(_ => Contractors);
        _context.Services.AddScoped(_ => new InvoicePaymentService(Postgres.DbContextFactory, new FixedUser(UserId), Fakturownia, Clock));
        _context.Render<MudPopoverProvider>();
        _dialogs = _context.Render<MudDialogProvider>();
    }

    private CustomerOrderService Orders => new(Postgres.DbContextFactory, new FixedUser(UserId), Clock);

    public override async Task DisposeAsync() => await _context.DisposeAsync();

    private static void ClickButton<T>(IRenderedComponent<T> fragment, string text)
        where T : IComponent =>
        fragment.FindAll("button").Single(button => button.TextContent.Contains(text)).Click();

    private async Task<int> OrderOkAsync(params (int Product, decimal Quantity)[] lines)
    {
        var result = await Orders.CreateAsync(new OrderDraft
        {
            RequestId = Guid.NewGuid(),
            ContractorId = SupplierId,
            Lines = lines.Select(l => new OrderDraftLine { ProductId = l.Product, Quantity = l.Quantity }).ToList(),
        });
        Assert.True(result.IsSuccess, result.Error);
        return result.Id!.Value;
    }

    private async Task<CustomerOrder> OrderAsync(int id)
    {
        await using var db = Postgres.CreateAppDbContext();
        return await db.CustomerOrders.AsNoTracking().SingleAsync(o => o.Id == id);
    }

    [Fact]
    public async Task List_shows_an_open_order_with_its_number()
    {
        var id = await OrderOkAsync((await StockedGoodsAsync(5), 1));
        var number = (await OrderAsync(id)).Number;

        var page = _context.Render<OrdersPage>();

        page.WaitForAssertion(() => Assert.Contains(number, page.Markup));
        Assert.Contains("otwarte", page.Markup);
    }

    [Fact]
    public async Task Details_show_what_is_missing_after_a_loss_and_cancelling_releases_the_goods()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync((goods, 8));
        await PostOkAsync(Rw(Out(goods, 6)));

        var page = _context.Render<OrderDetailsPage>(parameters => parameters.Add(x => x.Id, id));
        page.WaitForAssertion(() => Assert.Contains("brakuje 4", page.Markup));
        ClickButton(page, "Anuluj zamówienie");
        _dialogs.WaitForAssertion(() => Assert.Contains("Rezerwacja towaru zostanie zwolniona", _dialogs.Markup));
        _dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Anuluj zamówienie").Click();

        page.WaitForAssertion(() => Assert.Contains("anulowane", page.Markup));
        Assert.Equal(CustomerOrderStatus.Cancelled, (await OrderAsync(id)).Status);
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Wystaw fakturę"));
    }

    [Fact]
    public async Task Invoice_page_from_an_order_starts_filled_and_completes_the_order()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await OrderOkAsync((goods, 10));
        var number = (await OrderAsync(id)).Number;
        _context.Services.GetRequiredService<NavigationManager>().NavigateTo(OrderRoutes.Invoice(id));

        var page = _context.Render<NewInvoicePage>();
        page.WaitForAssertion(() => Assert.Contains(number, page.Markup));
        Assert.Equal("10", page.FindComponents<MudNumericField<decimal?>>().First(f => f.Instance.Label == "Ilość (szt)").Find("input").GetAttribute("value"));

        ClickButton(page, "Pokaż podgląd");
        page.WaitForAssertion(() => Assert.False(page.FindAll("button").Single(b => b.TextContent.Contains("Wystaw fakturę")).HasAttribute("disabled")));
        ClickButton(page, "Wystaw fakturę");
        _dialogs.WaitForAssertion(() => Assert.Contains("zamówienie zostanie zrealizowane", _dialogs.Markup));
        _dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Wystaw").Click();

        await WaitForAsync(async () => (await OrderAsync(id)).Status == CustomerOrderStatus.Completed);
        var order = await OrderAsync(id);
        Assert.Equal(0m, await StockOfAsync(goods));
        Assert.Equal(SupplierId, (await InvoiceAsync(order.InvoiceId!.Value)).ContractorId);
    }

    [Fact]
    public async Task Invoice_page_refuses_an_order_that_is_no_longer_open()
    {
        var id = await OrderOkAsync((await StockedGoodsAsync(5), 1));
        await Orders.CancelAsync(id);
        _context.Services.GetRequiredService<NavigationManager>().NavigateTo(OrderRoutes.Invoice(id));

        var page = _context.Render<NewInvoicePage>();

        page.WaitForAssertion(() => Assert.Contains("nie można z niego wystawić faktury", page.Markup));
        Assert.Empty(page.FindComponents<InvoiceEditor>());
    }

    [Fact]
    public void New_order_page_loads_with_its_editor()
    {
        var page = _context.Render<NewOrderPage>();

        Assert.Contains("Nowe zamówienie", page.Markup);
        Assert.NotNull(page.FindComponent<OrderEditor>());
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100 && !await condition(); attempt++)
        {
            await Task.Delay(50);
        }

        Assert.True(await condition());
    }
}
