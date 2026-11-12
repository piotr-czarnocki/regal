using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Analytics;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Orders;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

/// <summary>
/// Product and contractor analysis (SPEC 5.20) on a set computed by hand. It holds what breaks
/// such figures: a quantity correction, a price-only correction, a proforma, a cancelled invoice,
/// a service, a reversed delivery, a paid invoice and an open order.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InsightTests(PostgresFixture postgres) : InvoiceTestBase(postgres)
{
    private InsightService Insight => new(Postgres.DbContextFactory, new FixedUser(UserId), Clock);

    private AnalyticsService Analytics => new(Postgres.DbContextFactory, new FixedUser(UserId), Clock);

    private InvoicePaymentService Payments => new(Postgres.DbContextFactory, new FixedUser(UserId), Fakturownia, Clock);

    /// <summary>Ten days from 21.06 to 30.06.2026; the period before it is 11.06–20.06.</summary>
    private AnalyticsFilter Period => new(new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 30));

    private sealed record Fixture(int Goods, int Service, int BuyerA, int BuyerB, int Supplier, int FirstDelivery, int SecondDelivery, int ReversedDelivery);

    private StockDocumentDraft PzFrom(int supplier, StockDocumentLine line)
    {
        var draft = Pz(line);
        draft.ContractorId = supplier;
        return draft;
    }

    private static InvoiceDraft For(int buyer, InvoiceDraft draft, InvoiceKind kind = InvoiceKind.Vat)
    {
        draft.ContractorId = buyer;
        draft.Kind = kind;
        return draft;
    }

    private async Task<int> SentAsync(InvoiceDraft draft)
    {
        var id = await IssueOkAsync(draft);
        await DrainAsync();
        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
        return id;
    }

    private async Task CorrectAsync(int invoiceId, Action<CorrectionDraftLine, InvoiceLine> change)
    {
        var invoice = await InvoiceAsync(invoiceId);
        var result = await Invoices.CorrectAsync(new CorrectionDraft
        {
            RequestId = Guid.NewGuid(),
            InvoiceId = invoiceId,
            Reason = "Korekta",
            Lines = invoice.Lines.Select(line =>
            {
                var after = new CorrectionDraftLine { LineId = line.Id, Quantity = line.Quantity, NetPrice = line.NetPrice, DiscountPercent = line.DiscountPercent };
                change(after, line);
                return after;
            }).ToList(),
        });
        Assert.True(result.IsSuccess, result.Error);
        await DrainAsync();
    }

    private async Task<Fixture> BuildAsync()
    {
        // 15.06 (the period before): 100 delivered at 5 zł, 2 sold to A at 10 zł and paid on 20.06.
        var goods = await NewGoodsAsync();
        var service = await NewServiceAsync();
        var buyerA = await NewContractorAsync();
        var buyerB = await NewContractorAsync();
        var supplier = await NewContractorAsync();
        var first = await PostOkAsync(PzFrom(supplier, In(goods, 100, 5m)));
        var earlier = await SentAsync(For(buyerA, Draft(Item(goods, 2, 10m))));
        var paid = await InvoiceAsync(earlier);
        Fakturownia.SetPayment(paid.FakturowniaId!.Value, "paid", paid.GrossTotal, new DateOnly(2026, 6, 20));
        Assert.True((await Payments.RefreshAsync(earlier)).IsSuccess);

        // 25.06 (the period): two deliveries, the second reversed.
        Clock.Advance(TimeSpan.FromDays(10));
        var second = await PostOkAsync(PzFrom(supplier, In(goods, 10, 6m)));
        var reversed = await PostOkAsync(PzFrom(supplier, In(goods, 5, 6m)));
        await ReverseOkAsync(reversed);

        // A: 4 at 20 zł less 10% = 72, then 1 returned (−18). B: 1 at 20 zł and a service, then
        // the price of the goods lowered to 15 zł (quantity 0, −5).
        var toA = await SentAsync(For(buyerA, Draft(Item(goods, 4, 20m, discount: 10))));
        Fakturownia.SetPayment((await InvoiceAsync(toA)).FakturowniaId!.Value, "unpaid", 0);
        Assert.True((await Payments.RefreshAsync(toA)).IsSuccess);
        var toB = await SentAsync(For(buyerB, Draft(Item(goods, 1, 20m), Item(service, 1, 100m))));
        await CorrectAsync(toA, (after, line) => after.Quantity = 3);
        await CorrectAsync(toB, (after, line) => after.NetPrice = line.ProductId == goods ? 15m : line.NetPrice);

        // Not sales: a proforma and a cancelled invoice.
        await IssueOkAsync(For(buyerA, Draft(Item(goods, 7)), InvoiceKind.Proforma));
        var cancelled = await IssueOkAsync(For(buyerB, Draft(Item(goods, 3))));
        Assert.True((await Invoices.CancelAsync(cancelled, Guid.NewGuid())).IsSuccess);

        // An open order of A reserves 1.
        var order = await new CustomerOrderService(Postgres.DbContextFactory, new FixedUser(UserId), Clock).CreateAsync(new OrderDraft
        {
            RequestId = Guid.NewGuid(), ContractorId = buyerA, Lines = [new OrderDraftLine { ProductId = goods, Quantity = 1 }],
        });
        Assert.True(order.IsSuccess, order.Error);

        return new Fixture(goods, service, buyerA, buyerB, supplier, first, second, reversed);
    }

    [Fact]
    public async Task The_product_card_counts_corrections_and_leaves_out_proformas_and_cancelled_invoices()
    {
        var f = await BuildAsync();

        var card = (await Insight.GetProductCardAsync(f.Goods, Period))!;

        var s = card.Sales;
        // The second delivery at 6 zł raised the average cost to (98 × 5 + 10 × 6) / 108 = 5.0926 zł, which
        // the later sales and the return carry: 4 × 5.0926 = 20.37.
        Assert.Equal((4m, 69m, 20.37m), (s.Quantity, s.NetSales, s.Cost)); // 4 + 1 − 1 + 0; 72 + 20 − 18 − 5
        Assert.Equal((2, 2), (s.Invoices, s.Customers));
        Assert.Equal((17.25m, 8.0m, 1m), (s.AveragePrice!.Value, s.AverageDiscountPercent!.Value, s.ReturnedQuantity)); // discount (4×10 + 1×0) / 5
        Assert.Equal((20m, 245.0m), (s.PreviousNetSales, s.ChangePercent!.Value));
        Assert.Equal((new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 25)), (s.FirstSale!.Value, s.LastSale!.Value));

        // The same numbers as the sales analysis for this product.
        var row = Assert.Single(await Analytics.GetSalesByProductAsync(Period with { CategoryId = CategoryId }), r => r.ProductId == f.Goods);
        Assert.Equal((row.Quantity, row.NetSales, row.Cost), (s.Quantity, s.NetSales, s.Cost));

        // 100 − 2 + 10 + 5 − 5 − 4 − 1 + 1 − 3 + 3 = 104 on hand, 1 reserved; 103 at 0.4 a day.
        var stock = card.Stock!;
        Assert.Equal((104m, 104m, 1m, 103m), (stock.OnHand, stock.Unexpired, stock.Reserved, stock.Available));
        Assert.Equal((257.5m, new DateOnly(2026, 6, 25)), (stock.DaysOfCover!.Value, stock.LastReceipt!.Value));

        Assert.Equal(
            [("Przyjęcia od dostawców (PZ)", 10m, 60m), ("Wydania i sprzedaż (WZ)", -4m, -20.37m)],
            card.Movements.Select(m => (m.Kind, m.Quantity, m.Value)));
        Assert.Equal([(f.ReversedDelivery, true, 6m), (f.SecondDelivery, false, 6m), (f.FirstDelivery, false, 5m)],
            card.Receipts.Select(r => (r.DocumentId, r.Reversed, r.UnitCost!.Value)));

        Assert.Equal([(f.BuyerA, 3m, 54m, 1), (f.BuyerB, 1m, 15m, 1)], card.TopCustomers.Select(b => (b.ContractorId, b.Quantity, b.NetSales, b.Invoices)));
    }

    [Fact]
    public async Task A_service_card_has_sales_but_no_stock()
    {
        var f = await BuildAsync();

        var card = (await Insight.GetProductCardAsync(f.Service, Period))!;

        Assert.Equal((1m, 100m), (card.Sales.Quantity, card.Sales.NetSales));
        Assert.Null(card.Stock);
        Assert.Empty(card.Movements);
    }

    [Fact]
    public async Task The_ranking_matches_the_card_and_compares_with_the_period_before()
    {
        var f = await BuildAsync();

        var rows = await Insight.GetProductRankingAsync(Period with { CategoryId = CategoryId });

        var goods = Assert.Single(rows, r => r.ProductId == f.Goods);
        Assert.Equal((4m, 69m, 20.37m, 2, 2, 20m), (goods.Quantity, goods.NetSales, goods.Cost, goods.Invoices, goods.Customers, goods.PreviousNetSales));
        Assert.Equal((48.63m, 70.5m, 17.25m, 245.0m), (goods.Margin, goods.MarginPercent!.Value, goods.AveragePrice!.Value, goods.ChangePercent!.Value));
        Assert.Equal((103m, 257.5m), (goods.Available!.Value, goods.DaysOfCover!.Value));

        // The service sold more, so it leads and is class A; the goods make up the rest.
        Assert.Equal(f.Service, rows[0].ProductId);
        Assert.Equal((AbcClass.A, 59.17m), (rows[0].Class, rows[0].SharePercent)); // 100 / 169
        Assert.Null(rows[0].Available);
    }

    [Fact]
    public async Task The_contractor_card_shows_the_buyer_its_payments_and_orders()
    {
        var f = await BuildAsync();

        var card = (await Insight.GetContractorCardAsync(f.BuyerA, Period))!;

        var c = card.Customer;
        Assert.Equal((54m, 15.28m, 1, 54m), (c.NetSales, c.Cost, c.Invoices, c.AverageInvoice)); // 72 − 18; (4 − 1) × 5.0926
        Assert.Equal((1, -18m), (c.Corrections, c.CorrectionsNet));
        Assert.Equal((20m, 170.0m), (c.PreviousNetSales, c.ChangePercent!.Value));
        Assert.Equal((new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 25), 0), (c.FirstPurchase!.Value, c.LastPurchase!.Value, c.DaysSinceLastPurchase!.Value));
        Assert.Null(c.AverageDaysBetweenPurchases); // one purchase day in the period

        var row = Assert.Single(await Analytics.GetCustomersAsync(Period), r => r.ContractorId == f.BuyerA);
        Assert.Equal((row.NetSales, row.Cost, row.InvoiceCount), (c.NetSales, c.Cost, c.Invoices));

        // Paid in 5 days of 14; the invoice of 25.06 is unpaid but not yet due.
        var unpaid = await InvoiceAsync((await InvoicesOfAsync(f.BuyerA)).Single(i => i.PaymentStatus == InvoicePaymentStatus.Unpaid).Id);
        Assert.Equal((1, 5.0m, 100.0m), (card.Payments.PaidInvoices, card.Payments.AverageDaysToPay!.Value, card.Payments.PaidOnTimePercent!.Value));
        Assert.Equal(new ReceivablesSummary(0, 0, 1, unpaid.GrossTotal), card.Payments.Receivables);

        Assert.Equal(new OrderFigures(1, 0, 0), card.Orders);
        Assert.Equal(0, card.Supplier.Deliveries);
    }

    [Fact]
    public async Task A_reversed_delivery_cancels_its_original_for_the_supplier()
    {
        var f = await BuildAsync();

        var card = (await Insight.GetContractorCardAsync(f.Supplier, Period))!;
        var row = Assert.Single(await Insight.GetSuppliersAsync(Period), r => r.ContractorId == f.Supplier);

        Assert.Equal((1, 60m, 1, new DateOnly(2026, 6, 25)), (card.Supplier.Deliveries, card.Supplier.Value, card.Supplier.Products, card.Supplier.LastDelivery!.Value));
        Assert.Equal((f.Goods, 10m, 60m), (card.Supplier.TopProducts.Single().ProductId, card.Supplier.TopProducts.Single().Quantity, card.Supplier.TopProducts.Single().Value));
        Assert.Equal((1, 60m, 1), (row.Deliveries, row.Value, row.Products));
        Assert.Equal(0m, card.Customer.NetSales);
    }

    [Fact]
    public async Task Unknown_product_or_contractor_gives_no_card()
    {
        Assert.Null(await Insight.GetProductCardAsync(int.MaxValue, Period));
        Assert.Null(await Insight.GetContractorCardAsync(int.MaxValue, Period));
    }

    [Fact]
    public async Task The_pages_show_the_ranking_both_cards_and_the_suppliers()
    {
        var f = await BuildAsync();
        await using var context = new Bunit.BunitContext();
        context.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        context.Services.AddSingleton<TimeProvider>(Clock);
        context.Services.AddScoped(_ => Insight);
        context.Services.AddScoped(_ => Analytics);
        context.Services.AddScoped(_ => new Regal.Web.Features.Products.CategoryService(Postgres.DbContextFactory, new FixedUser(UserId), Clock));
        context.Services.AddScoped(_ => Contractors);
        context.Services.AddScoped<Regal.Web.Components.Shared.FileDownloader>();
        context.Render<MudBlazor.MudPopoverProvider>();
        var goodsName = (await Products.GetForEditAsync(f.Goods))!.Name!;
        var buyerName = (await Contractors.GetForEditAsync(f.BuyerA))!.Name!;

        var product = context.Render<ProductCardPage>(p => p.Add(x => x.Id, f.Goods));
        product.WaitForAssertion(() => Assert.Contains("Najlepsi klienci", product.Markup));
        Assert.Contains(goodsName, product.Markup);
        Assert.Contains(AnalyticsRoutes.Contractor(f.BuyerA), product.Markup);
        Assert.Contains("Przyjęcia od dostawców (PZ)", product.Markup);
        Assert.Contains("cofnięty", product.Markup);

        var contractor = context.Render<ContractorCardPage>(p => p.Add(x => x.Id, f.BuyerA));
        contractor.WaitForAssertion(() => Assert.Contains("Co kupuje", contractor.Markup));
        Assert.Contains(buyerName, contractor.Markup);
        Assert.Contains(AnalyticsRoutes.Product(f.Goods), contractor.Markup);
        Assert.Contains("Płaci średnio po", contractor.Markup);
        Assert.DoesNotContain("Jako dostawca", contractor.Markup);

        var supplier = context.Render<ContractorCardPage>(p => p.Add(x => x.Id, f.Supplier));
        supplier.WaitForAssertion(() => Assert.Contains("Jako dostawca", supplier.Markup));

        var ranking = context.Render<ProductAnalysisPage>();
        ranking.WaitForAssertion(() => Assert.Contains("Analiza produktów", ranking.Markup));

        var contractors = context.Render<CustomersPage>();
        contractors.WaitForAssertion(() => Assert.Contains("Dostawcy", contractors.Markup));
    }

    [Fact]
    public async Task A_card_of_a_missing_product_says_so()
    {
        await using var context = new Bunit.BunitContext();
        context.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        context.Services.AddScoped(_ => Insight);
        context.Services.AddScoped(_ => Analytics);
        context.Services.AddScoped(_ => new Regal.Web.Features.Products.CategoryService(Postgres.DbContextFactory, new FixedUser(UserId), Clock));
        context.Services.AddScoped(_ => Contractors);

        var page = context.Render<ProductCardPage>(p => p.Add(x => x.Id, int.MaxValue));

        page.WaitForAssertion(() => Assert.Contains("Nie znaleziono produktu", page.Markup));
    }

    private async Task<List<Invoice>> InvoicesOfAsync(int contractorId)
    {
        await using var db = Postgres.CreateAppDbContext();
        return await db.Invoices.AsNoTracking().Where(i => i.ContractorId == contractorId && i.Kind == InvoiceKind.Vat).ToListAsync();
    }
}
