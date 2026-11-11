using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Components.Pages;
using Regal.Web.Features.Analytics;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Products;

namespace Regal.IntegrationTests;

// The database is shared, so each test filters by the category or the customer it created.
[Collection(PostgresCollection.Name)]
public sealed class AnalyticsTests(PostgresFixture postgres) : InvoiceTestBase(postgres)
{
    private sealed class DeniedUser : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => throw new UnauthorizedAccessException();
    }

    private AnalyticsService Analytics => new(Postgres.DbContextFactory, new FixedUser(UserId), Clock);

    private AnalyticsFilter Mine(DateOnly? from = null, DateOnly? to = null) =>
        new(from ?? Today.AddDays(-400), to ?? Today, CategoryId);

    private async Task<string> NameOfAsync(int product) => (await Products.GetForEditAsync(product))!.Name!;

    [Fact]
    public async Task Sales_count_invoices_that_were_not_cancelled_with_cost_from_the_wz()
    {
        var goods = await StockedGoodsAsync(cost: 5m);
        var service = await NewServiceAsync();
        await IssueOkAsync(Draft(Item(goods, 2), Item(service, 1, discount: 10m)));
        var cancelled = await IssueOkAsync(Draft(Item(goods, 3)));
        Assert.True((await Invoices.CancelAsync(cancelled, Guid.NewGuid())).IsSuccess);

        var summary = await Analytics.GetSummaryAsync(Mine());

        Assert.Equal((130m, 10m, 120m, 1), (summary.NetSales, summary.Cost, summary.Margin, summary.InvoiceCount));
        Assert.Equal(92.3m, summary.MarginPercent);

        var products = await Analytics.GetSalesByProductAsync(Mine());
        Assert.Equal([(service, 90m, 0m), (goods, 40m, 10m)], products.Select(p => (p.ProductId, p.NetSales, p.Cost)));
        Assert.Equal(2m, products[1].Quantity);
    }

    [Fact]
    public async Task Period_includes_both_end_days()
    {
        await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));

        Assert.Equal(100m, (await Analytics.GetSummaryAsync(Mine(Today, Today))).NetSales);
        Assert.Equal(0m, (await Analytics.GetSummaryAsync(Mine(Today.AddDays(1), Today.AddDays(5)))).NetSales);
        Assert.Equal(0m, (await Analytics.GetSummaryAsync(Mine(Today.AddDays(-5), Today.AddDays(-1)))).NetSales);
    }

    [Fact]
    public async Task Customer_filter_keeps_only_that_customers_invoices()
    {
        var service = await NewServiceAsync();
        await IssueOkAsync(Draft(Item(service, 1)));
        var other = Draft(Item(service, 2));
        other.ContractorId = await NewContractorAsync();
        await IssueOkAsync(other);

        var filter = Mine() with { ContractorId = SupplierId };

        Assert.Equal(100m, (await Analytics.GetSummaryAsync(filter)).NetSales);
        Assert.Equal(300m, (await Analytics.GetSummaryAsync(Mine())).NetSales);
    }

    [Fact]
    public async Task Short_periods_are_shown_by_day_and_long_ones_by_month_with_empty_days_as_zero()
    {
        await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));

        var daily = await Analytics.GetSalesOverTimeAsync(Mine(Today.AddDays(-2), Today));
        Assert.Equal([0m, 0m, 100m], daily.Select(p => p.NetSales));
        Assert.Equal(Today.ToString("dd.MM"), daily[2].Label);

        var monthly = await Analytics.GetSalesOverTimeAsync(Mine(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        Assert.Equal(12, monthly.Count);
        Assert.Equal(100m, monthly.Single(p => p.Start == new DateOnly(2026, 6, 1)).NetSales);
        Assert.Equal(100m, monthly.Sum(p => p.NetSales));
    }

    [Fact]
    public async Task Sales_are_grouped_by_category()
    {
        await IssueOkAsync(Draft(Item(await StockedGoodsAsync(), 1), Item(await NewServiceAsync(), 1)));

        var category = Assert.Single(await Analytics.GetSalesByCategoryAsync(Mine()));

        Assert.Equal(120m, category.NetSales);
    }

    [Fact]
    public async Task Abc_classifies_the_products_sold_in_the_period()
    {
        var big = await NewServiceAsync();
        var middle = await NewServiceAsync();
        var small = await NewServiceAsync();
        await IssueOkAsync(Draft(Item(big, 1, 800m), Item(middle, 1, 150m), Item(small, 1, 50m)));

        var rows = await Analytics.GetAbcAsync(Mine());

        Assert.Equal([(big, AbcClass.A), (middle, AbcClass.B), (small, AbcClass.C)], rows.Select(r => (r.ProductId, r.Class)));
    }

    [Fact]
    public async Task Slow_moving_lists_goods_in_stock_without_recent_sales()
    {
        var soldLongAgo = await StockedGoodsAsync();
        var soldRecently = await StockedGoodsAsync();
        var neverSold = await StockedGoodsAsync(quantity: 4, cost: 2.5m);
        var soldOut = await StockedGoodsAsync(quantity: 1);
        await IssueOkAsync(Draft(Item(soldLongAgo, 1), Item(soldOut, 1)));
        Clock.Advance(TimeSpan.FromDays(100));
        await IssueOkAsync(Draft(Item(soldRecently, 1)));

        var rows = await Analytics.GetSlowMovingAsync(90, CategoryId);

        var ids = rows.Select(r => r.ProductId).ToList();
        Assert.Contains(soldLongAgo, ids);
        Assert.Contains(neverSold, ids);
        Assert.DoesNotContain(soldRecently, ids);
        Assert.DoesNotContain(soldOut, ids);
        var never = rows.Single(r => r.ProductId == neverSold);
        Assert.Null(never.LastSale);
        Assert.Equal(10m, never.Value);
        Assert.Equal(Today.AddDays(-100), rows.Single(r => r.ProductId == soldLongAgo).LastSale);
    }

    [Fact]
    public async Task Slow_moving_ignores_cancelled_sales_and_lists_the_most_valuable_stock_first()
    {
        var cheap = await StockedGoodsAsync(quantity: 10, cost: 1m);
        var valuable = await StockedGoodsAsync(quantity: 10, cost: 9m);
        await IssueOkAsync(Draft(Item(valuable, 1)));
        Clock.Advance(TimeSpan.FromDays(100));
        var cancelled = await IssueOkAsync(Draft(Item(valuable, 1)));
        Assert.True((await Invoices.CancelAsync(cancelled, Guid.NewGuid())).IsSuccess);

        var rows = await Analytics.GetSlowMovingAsync(90, CategoryId);

        // The cancelled sale today does not count, so the last sale is the one 100 days ago.
        Assert.Equal([valuable, cheap], rows.Select(r => r.ProductId));
        Assert.Equal((9m, 81m, Today.AddDays(-100)), (rows[0].Quantity, rows[0].Value, rows[0].LastSale));
    }

    [Fact]
    public async Task Expiring_batches_include_expired_ones_and_skip_later_or_empty_ones()
    {
        var product = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(product, 1, 5m, "WKROTCE", Today.AddDays(1)), In(product, 1, 5m, "POZNIEJ", Today.AddDays(60))));
        Clock.Advance(TimeSpan.FromDays(2));

        var rows = (await Analytics.GetExpiringBatchesAsync(14)).Where(r => r.ProductId == product).ToList();

        var row = Assert.Single(rows);
        Assert.Equal("WKROTCE", row.BatchNumber);
        Assert.True(row.IsExpired);
    }

    [Fact]
    public async Task Every_analytics_method_requires_an_administrator()
    {
        var denied = new AnalyticsService(Postgres.DbContextFactory, new DeniedUser(), new FakeTimeProvider());
        var filter = Mine();

        Func<Task>[] calls =
        [
            () => denied.GetSummaryAsync(filter),
            () => denied.GetSalesOverTimeAsync(filter),
            () => denied.GetSalesByProductAsync(filter),
            () => denied.GetSalesByCategoryAsync(filter),
            () => denied.GetAbcAsync(filter),
            () => denied.GetSlowMovingAsync(30),
            () => denied.GetExpiringBatchesAsync(30),
        ];

        foreach (var call in calls)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(call);
        }
    }

    [Fact]
    public async Task Pages_load_and_show_what_the_service_returns()
    {
        var service = await NewServiceAsync();
        var name = await NameOfAsync(service);
        await IssueOkAsync(Draft(Item(service, 1, 250m)));

        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddMudServices();
        context.Services.AddScoped(_ => Analytics);
        context.Services.AddScoped<Regal.Web.Components.Shared.FileDownloader>();
        context.Services.AddScoped(_ => new Regal.Web.Features.Invoices.InvoicePaymentService(Postgres.DbContextFactory, new FixedUser(UserId), Fakturownia, Clock));
        context.Services.AddScoped(_ => new CategoryService(Postgres.DbContextFactory, new FixedUser(UserId), Clock));
        context.Services.AddScoped(_ => Contractors);
        var authorization = context.AddAuthorization();
        authorization.SetAuthorized("admin@example.com").SetRoles(Regal.Web.Features.Auth.Roles.Administrator);
        context.Render<MudPopoverProvider>();

        // The home page depends on the role: figures for the administrator, the warehouse view for the rest.
        var home = context.Render<Home>();
        home.WaitForAssertion(() => Assert.Contains("Sprzedaż netto", home.Markup));
        Assert.Contains("Nowa faktura", home.Markup);

        authorization.SetRoles(Regal.Web.Features.Auth.Roles.Warehouse);
        var warehouse = context.Render<Home>();
        warehouse.WaitForAssertion(() => Assert.Contains("Partie z bliską datą ważności", warehouse.Markup));
        Assert.DoesNotContain("Sprzedaż netto", warehouse.Markup);
        Assert.DoesNotContain("Nowa faktura", warehouse.Markup);

        var abc = context.Render<AbcPage>();
        abc.WaitForAssertion(() => Assert.Contains(name, abc.Markup));

        var sales = context.Render<SalesPage>();
        sales.WaitForAssertion(() => Assert.Contains("Według kategorii", sales.Markup));

        var slow = context.Render<SlowMovingPage>();
        slow.WaitForAssertion(() => Assert.Contains("Towary zalegające", slow.Markup));
    }
}
