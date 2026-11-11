using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Features.Analytics;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Orders;
using Regal.Web.Features.Products;

namespace Regal.IntegrationTests;

/// <summary>Customers, comparison of periods and the order forecast (SPEC 5.16).</summary>
[Collection(PostgresCollection.Name)]
public class PlanningTests(PostgresFixture postgres) : InvoiceTestBase(postgres)
{
    private AnalyticsService Analytics => new(Postgres.DbContextFactory, new FixedUser(UserId), Clock);

    private AnalyticsFilter Mine(DateOnly from, DateOnly to) => new(from, to, CategoryId);

    private async Task<int> SellAsync(int buyer, params InvoiceDraftLine[] lines)
    {
        var draft = Draft(lines);
        draft.ContractorId = buyer;
        return await IssueOkAsync(draft);
    }

    [Fact]
    public async Task Customers_are_listed_best_first_with_invoices_margin_and_last_purchase()
    {
        var goods = await StockedGoodsAsync(100, cost: 5m);
        var small = await NewContractorAsync();
        var big = await NewContractorAsync();
        await SellAsync(small, Item(goods, 1, netPrice: 20m));
        await SellAsync(big, Item(goods, 2, netPrice: 20m));
        Clock.Advance(TimeSpan.FromDays(3));
        await SellAsync(big, Item(goods, 3, netPrice: 20m));

        var rows = await Analytics.GetCustomersAsync(Mine(Today.AddDays(-10), Today) with { ContractorId = small });

        Assert.Equal([big, small], rows.Select(r => r.ContractorId));
        var first = rows[0];
        Assert.Equal((2, 100m, 25m, 75m, 50m, Today), (first.InvoiceCount, first.NetSales, first.Cost, first.Margin, first.AverageInvoice, first.LastPurchase));
    }

    [Fact]
    public async Task A_correction_lowers_the_customer_but_does_not_count_as_an_invoice()
    {
        var goods = await StockedGoodsAsync(100, cost: 5m);
        var buyer = await NewContractorAsync();
        var id = await SellAsync(buyer, Item(goods, 4, netPrice: 20m));
        await DrainAsync();
        var line = (await InvoiceAsync(id)).Lines.Single();
        Assert.True((await Invoices.CorrectAsync(new CorrectionDraft
        {
            RequestId = Guid.NewGuid(), InvoiceId = id, Reason = "Zwrot",
            Lines = [new CorrectionDraftLine { LineId = line.Id, Quantity = 1, NetPrice = 20m }],
        })).IsSuccess);

        var row = Assert.Single(await Analytics.GetCustomersAsync(Mine(Today, Today)));

        Assert.Equal((1, 20m), (row.InvoiceCount, row.NetSales));
    }

    [Fact]
    public async Task Periods_are_compared_with_the_one_before_and_a_year_earlier()
    {
        var goods = await StockedGoodsAsync(100, cost: 5m);
        var buyer = await NewContractorAsync();
        await SellAsync(buyer, Item(goods, 1, netPrice: 10m));          // a year before the chosen week
        Clock.Advance(TimeSpan.FromDays(358));
        await SellAsync(buyer, Item(goods, 2, netPrice: 10m));          // the week before
        Clock.Advance(TimeSpan.FromDays(7));
        await SellAsync(buyer, Item(goods, 3, netPrice: 10m));          // the chosen week

        var comparison = await Analytics.ComparePeriodsAsync(Mine(Today.AddDays(-6), Today));

        Assert.Equal((30m, 20m, 10m), (comparison.Current.Sales.NetSales, comparison.Previous.Sales.NetSales, comparison.YearEarlier.Sales.NetSales));
        Assert.Equal((Today.AddDays(-13), Today.AddDays(-7)), (comparison.Previous.From, comparison.Previous.To));
        Assert.Equal(Today.AddYears(-1), comparison.YearEarlier.To);
    }

    [Fact]
    public async Task The_forecast_suggests_what_covers_the_horizon_beyond_available_and_reserved_stock()
    {
        var fast = await StockedGoodsAsync(40, cost: 2m);
        var covered = await StockedGoodsAsync(100, cost: 2m);
        var buyer = await NewContractorAsync();
        for (var day = 0; day < 4; day++)
        {
            await SellAsync(buyer, Item(fast, 7), Item(covered, 1));     // 28 and 4 sold, all within the 28-day history
            Clock.Advance(TimeSpan.FromDays(6));
        }

        // 12 of fast left; reserve 2 of them, so 10 are available.
        var orders = new CustomerOrderService(Postgres.DbContextFactory, new FixedUser(UserId), Clock);
        Assert.True((await orders.CreateAsync(new OrderDraft
        {
            RequestId = Guid.NewGuid(), ContractorId = buyer, Lines = [new OrderDraftLine { ProductId = fast, Quantity = 2 }],
        })).IsSuccess);

        var rows = await Analytics.GetForecastAsync(historyDays: 28, horizonDays: 14, CategoryId);

        var row = Assert.Single(rows);
        Assert.Equal(fast, row.ProductId);
        Assert.Equal((28m, 1m, 10m, 10.0m, 4m), (row.SoldInHistory, row.DailyAverage, row.Available, row.DaysOfCover!.Value, row.SuggestedOrder));
    }

    [Fact]
    public async Task The_forecast_refuses_days_out_of_range() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Analytics.GetForecastAsync(0, 14));

    [Fact]
    public async Task Pages_load()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        context.Services.AddScoped(_ => Analytics);
        context.Services.AddScoped(_ => new CategoryService(Postgres.DbContextFactory, new FixedUser(UserId), Clock));
        context.Services.AddScoped(_ => Contractors);
        context.Render<MudPopoverProvider>();

        var customers = context.Render<CustomersPage>();
        customers.WaitForAssertion(() => Assert.Contains("Ostatni zakup", customers.Markup));
        var forecast = context.Render<ForecastPage>();
        forecast.WaitForAssertion(() => Assert.Contains("Zapas na (dni)", forecast.Markup));
        var sales = context.Render<SalesPage>();
        sales.WaitForAssertion(() => Assert.Contains("Porównanie okresów", sales.Markup));
        Assert.Equal(3, sales.FindAll("tr[data-period]").Count);
    }
}
