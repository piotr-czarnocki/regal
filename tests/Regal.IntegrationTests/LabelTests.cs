using System.Text;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Regal.Web.Features.Products;

namespace Regal.IntegrationTests;

/// <summary>Label sheets in PDF (SPEC 5.18).</summary>
[Collection(PostgresCollection.Name)]
public class LabelTests(PostgresFixture postgres) : WarehouseTestBase(postgres)
{
    private LabelService Labels => new(Postgres.DbContextFactory, new FixedUser(UserId));

    private static int PageCount(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;

    [Fact]
    public async Task Labels_fill_a4_sheets_of_24_including_polish_names()
    {
        var bread = await Products.CreateAsync(new ProductInput
        {
            Sku = Unique("CHL"), Name = Unique("Chleb żytni ŻÓŁĆ"), CategoryId = CategoryId, Unit = "szt", NetPrice = 4.50m,
        });
        var other = await NewGoodsAsync();

        var result = await Labels.BuildAsync([new LabelRequest(bread.Id!.Value, 20), new LabelRequest(other, 5)]);

        Assert.Null(result.Error);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(result.Pdf!, 0, 4));
        Assert.Equal(2, PageCount(result.Pdf!)); // 25 labels, 24 per sheet
    }

    [Theory]
    [InlineData(0, "od 1 do 100")]
    [InlineData(101, "od 1 do 100")]
    public async Task Copies_are_limited(int copies, string message)
    {
        var product = await NewGoodsAsync();

        var result = await Labels.BuildAsync([new LabelRequest(product, copies)]);

        Assert.Contains(message, result.Error);
        Assert.Null(result.Pdf);
    }

    [Fact]
    public async Task Nothing_selected_and_too_many_labels_are_refused()
    {
        var products = new List<LabelRequest>();
        for (var i = 0; i < 5; i++)
        {
            products.Add(new LabelRequest(await NewGoodsAsync(), 100));
        }

        Assert.Contains("Zaznacz produkty", (await Labels.BuildAsync([])).Error);
        Assert.Contains("najwyżej 480", (await Labels.BuildAsync(products)).Error);
    }

    [Fact]
    public async Task An_unknown_product_is_refused() =>
        Assert.Contains("Nie znaleziono", (await Labels.BuildAsync([new LabelRequest(int.MaxValue, 1)])).Error);

    [Theory]
    [InlineData("CHL-01", "etykieta-CHL-01")]
    [InlineData(" A/B 7 ", "etykieta-A-B-7")]
    [InlineData("ŻÓŁW_1", "etykieta----W_1")]
    public void One_product_label_file_is_named_after_a_safe_sku(string sku, string expected) =>
        Assert.Equal(expected, LabelService.FileNameFor(sku));

    [Fact]
    public async Task One_click_on_a_row_downloads_that_product_label_and_a_service_has_no_button()
    {
        var sku = Unique("ETK").ToUpperInvariant(); // SKUs are stored in capitals
        var goods = await Products.CreateAsync(new ProductInput { Sku = sku, Name = Unique("Kefir"), CategoryId = CategoryId, Unit = "szt", NetPrice = 3m });
        var serviceSku = Unique("USL").ToUpperInvariant();
        await Products.CreateAsync(new ProductInput { Sku = serviceSku, Name = Unique("Dostawa"), CategoryId = CategoryId, Unit = "h", Kind = Regal.Web.Data.Entities.ProductKind.Service, NetPrice = 50m });
        Assert.True(goods.IsSuccess, goods.Error);

        await using var context = new Bunit.BunitContext();
        context.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Regal.Web.Components.Shared.FileDownloader.ModulePath);
        var save = module.SetupVoid("saveFile", _ => true);
        context.Services.AddMudServices();
        context.Services.AddSingleton<TimeProvider>(Clock);
        context.Services.AddScoped(_ => Products);
        context.Services.AddScoped(_ => Labels);
        context.Services.AddScoped<Regal.Web.Components.Shared.FileDownloader>();
        context.Render<MudBlazor.MudPopoverProvider>();

        var page = context.Render<ProductsPage>();
        await SearchAsync(page, sku);
        page.WaitForAssertion(() => Assert.Contains(page.FindAll("tr"), row => row.TextContent.Contains(sku)));
        var services = context.Render<ProductsPage>();
        await SearchAsync(services, serviceSku);
        services.WaitForAssertion(() => Assert.Contains(services.FindAll("tr"), row => row.TextContent.Contains(serviceSku)));
        Assert.DoesNotContain(services.FindAll("tr"), row => row.TextContent.Contains(serviceSku) && row.TextContent.Contains("Etykieta"));

        page.FindAll("tr").Single(row => row.TextContent.Contains(sku)).QuerySelectorAll("button").Single(b => b.TextContent.Contains("Etykieta")).Click();

        page.WaitForAssertion(() => Assert.Single(save.Invocations));
        Assert.StartsWith($"etykieta-{sku}-", (string)save.Invocations.Single().Arguments[0]!);
    }

    /// <summary>Types into the debounced search as MudBlazor reports it once the pause has passed.</summary>
    private static Task SearchAsync(Bunit.IRenderedComponent<ProductsPage> page, string term)
    {
        var field = page.FindComponents<MudBlazor.MudTextField<string>>().First(f => f.Instance.Placeholder?.StartsWith("Szukaj") == true);
        return page.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync(term));
    }
}
