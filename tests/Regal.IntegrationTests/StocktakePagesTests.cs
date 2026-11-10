using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class StocktakePagesTests : WarehouseTestBase
{
    private readonly BunitContext _context = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public StocktakePagesTests(PostgresFixture postgres)
        : base(postgres)
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Services.AddScoped(_ => Stocktakes);
        _context.Services.AddScoped(_ => Stock);
        _context.Render<MudPopoverProvider>();
        _dialogs = _context.Render<MudDialogProvider>();
    }

    private StocktakeService Stocktakes => new(Postgres.DbContextFactory, new FixedUser(UserId), Documents, Clock);

    public override async Task DisposeAsync() => await _context.DisposeAsync();

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Waits until the picker has finished with the scan (data-settled changes): bUnit sends events
    /// without waiting for their handlers, and a second scan sent into the first one's handling was lost.
    /// </summary>
    private static void Scan(IRenderedComponent<StocktakeDetailsPage> page, string text)
    {
        var settled = page.Find(".product-picker").GetAttribute("data-settled");
        var picker = page.FindComponent<ProductPicker>();
        picker.Find("input").Input(text);
        picker.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        page.WaitForAssertion(() => Assert.NotEqual(settled, page.Find(".product-picker").GetAttribute("data-settled")), Wait);
    }

    // Cells, not the whole markup: random element ids (GUIDs) contain fragments such as "-4".
    private static IReadOnlyList<string> Differences(IRenderedComponent<StocktakeDetailsPage> page) =>
        page.FindAll("td[data-label=\"Różnica\"]").Select(cell => cell.TextContent.Trim()).ToList();

    [Fact]
    public async Task Scanning_counting_and_approving_from_the_page_posts_the_shortage()
    {
        var product = await NewGoodsAsync();
        await PostOkAsync(Pz(In(product, 5, 2m, "A")));
        var ean = (await Products.GetForEditAsync(product))!.Ean!;
        var sheet = (await Stocktakes.CreateAsync()).Id!.Value;

        var page = _context.Render<StocktakeDetailsPage>(parameters => parameters.Add(x => x.Id, sheet));
        page.WaitForAssertion(() => Assert.Contains("w trakcie liczenia", page.Markup));
        Scan(page, ean);
        page.WaitForAssertion(() => Assert.Equal(["-4"], Differences(page)), Wait); // stock 5, one scanned
        Scan(page, ean);
        page.WaitForAssertion(() => Assert.Equal(["-3"], Differences(page)), Wait);

        page.FindComponents<MudNumericField<decimal?>>().Single(f => f.Instance.Label == "Policzono (szt)").Find("input").Change("4");
        page.WaitForAssertion(() => Assert.Equal(["-1"], Differences(page)), Wait);

        page.FindAll("button").Single(b => b.TextContent.Contains("Zatwierdź spis")).Click();
        _dialogs.WaitForAssertion(() => Assert.Contains("Niedobory: 1 pozycji", _dialogs.Markup));
        _dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Zatwierdź").Click();

        page.WaitForAssertion(() => Assert.Contains("zatwierdzony", page.Markup));
        Assert.Contains("RW/", page.Markup);
        Assert.Equal(4m, await StockOfAsync(product));
        Assert.Empty(page.FindComponents<ProductPicker>());
    }

    [Fact]
    public async Task The_list_links_to_each_sheet()
    {
        var sheet = (await Stocktakes.CreateAsync()).Id!.Value;

        var page = _context.Render<StocktakesPage>();

        page.WaitForAssertion(() => Assert.Contains(StockDocumentRoutes.Stocktake(sheet), page.Markup));
        Assert.Contains("w trakcie liczenia", page.Markup);
    }

    [Fact]
    public async Task A_missing_sheet_says_so()
    {
        var page = _context.Render<StocktakeDetailsPage>(parameters => parameters.Add(x => x.Id, int.MaxValue));

        page.WaitForAssertion(() => Assert.Contains("Nie znaleziono arkusza spisu", page.Markup));
        await Task.CompletedTask;
    }
}
