using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.IntegrationTests;

// The pages with the real services and database: do they load, and do they show what the services return.
[Collection(PostgresCollection.Name)]
public sealed class WarehousePagesTests : WarehouseTestBase
{
    private readonly BunitContext _context = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public WarehousePagesTests(PostgresFixture postgres)
        : base(postgres)
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        // Factories: the user id is known only after InitializeAsync.
        _context.Services.AddScoped(_ => Documents);
        _context.Services.AddScoped(_ => Stock);
        _context.Services.AddScoped(_ => Products);
        _context.Services.AddScoped(_ => Contractors);
        _context.Services.AddScoped(_ => new CategoryService(Postgres.DbContextFactory, new AdminUser(() => UserId), Clock));
        _context.Render<MudPopoverProvider>();
        _dialogs = _context.Render<MudDialogProvider>();
    }

    public override async Task DisposeAsync() => await _context.DisposeAsync();

    private sealed class AdminUser(Func<int> id) : Regal.Web.Features.Auth.ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => Task.FromResult(id());
    }

    private async Task<string> NameOfAsync(int product) => (await Products.GetForEditAsync(product))!.Name!;

    private static void ClickButton<T>(IRenderedComponent<T> fragment, string text)
        where T : IComponent =>
        fragment.FindAll("button").Single(button => button.TextContent.Contains(text)).Click();

    [Fact]
    public async Task Document_list_shows_documents_and_links_a_correction()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 1, 5m)));
        await ReverseOkAsync(pz);
        var number = (await DocumentAsync(pz)).Number;

        var page = _context.Render<StockDocumentsPage>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(number, page.Markup);
            Assert.Contains("Skorygowany", page.Markup);
            Assert.Contains("Korekta dokumentu", page.Markup);
        });
    }

    [Fact]
    public async Task Details_show_movements_and_the_correction_button_creates_a_linked_document()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 2, 5m, "L-SZCZEG", Today.AddDays(9))));
        var name = await NameOfAsync(product);

        var page = _context.Render<StockDocumentDetailsPage>(parameters => parameters.Add(x => x.Id, pz));
        page.WaitForAssertion(() => Assert.Contains("L-SZCZEG", page.Markup));
        Assert.Contains(name, page.Markup);
        Assert.Contains(StockLabels.Date(Today.AddDays(9)), page.Markup);

        ClickButton(page, "Korekta");
        _dialogs.WaitForAssertion(() => Assert.Contains("Utwórz korektę", _dialogs.Markup));
        ClickButton(_dialogs, "Utwórz korektę");

        page.WaitForAssertion(() => Assert.Contains("Dokument skorygowany dokumentem", page.Markup));
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Korekta"));
        Assert.Equal(0m, await StockOfAsync(product));
    }

    [Fact]
    public async Task Details_of_a_correction_link_back_and_offer_no_correction()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 2, 5m)));
        var correction = await ReverseOkAsync(pz);
        var number = (await DocumentAsync(pz)).Number;

        var page = _context.Render<StockDocumentDetailsPage>(parameters => parameters.Add(x => x.Id, correction));

        page.WaitForAssertion(() => Assert.Contains(number, page.Markup));
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Korekta"));
    }

    [Fact]
    public void Details_of_a_missing_document_say_so()
    {
        var page = _context.Render<StockDocumentDetailsPage>(parameters => parameters.Add(x => x.Id, int.MaxValue));

        page.WaitForAssertion(() => Assert.Contains("Nie znaleziono dokumentu.", page.Markup));
    }

    [Fact]
    public async Task Stock_page_marks_inactive_products_and_expired_batches_and_expands_batches()
    {
        var expiring = await NewGoodsAsync(hasExpiry: true);
        await PostOkAsync(Pz(In(expiring, 2, 5m, "STARA-P", Today.AddDays(1))));
        var inactive = await NewGoodsAsync();
        await PostOkAsync(Pz(In(inactive, 3, 5m)));
        var wz = await PostOkAsync(Wz(Out(inactive, 3)));
        await Products.SetActiveAsync(inactive, false);
        await ReverseOkAsync(wz);
        Clock.Advance(TimeSpan.FromDays(5));
        var expiringName = await NameOfAsync(expiring);
        var inactiveName = await NameOfAsync(inactive);

        var page = _context.Render<StockPage>();
        page.WaitForAssertion(() =>
        {
            Assert.Contains(expiringName, page.Markup);
            Assert.Contains(inactiveName, page.Markup);
            Assert.Contains("nieaktywny", page.Markup);
            Assert.Contains("przeterminowane", page.Markup);
        });

        var row = page.FindAll("tr").Single(r => r.TextContent.Contains(expiringName));
        row.QuerySelector("button")!.Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("STARA-P", page.Markup);
            Assert.Contains("batch-expired", page.Markup);
        });
    }

    [Fact]
    public async Task History_page_lists_the_products_movements()
    {
        var product = await NewGoodsAsync();
        var pz = await PostOkAsync(Pz(In(product, 2, 5m)));
        var wz = await PostOkAsync(Wz(Out(product, 1)));
        var pzNumber = (await DocumentAsync(pz)).Number;
        var wzNumber = (await DocumentAsync(wz)).Number;
        var name = await NameOfAsync(product);

        var page = _context.Render<ProductHistoryPage>(parameters => parameters.Add(x => x.ProductId, product));

        page.WaitForAssertion(() =>
        {
            Assert.Contains(name, page.Markup);
            Assert.Contains(pzNumber, page.Markup);
            Assert.Contains(wzNumber, page.Markup);
        });
    }

    [Fact]
    public void New_document_page_loads_with_its_editor()
    {
        var page = _context.Render<NewStockDocumentPage>();

        Assert.Contains("Nowy dokument magazynowy", page.Markup);
        Assert.NotNull(page.FindComponent<StockDocumentEditor>());
    }

    private async Task<int> NewProductAsync(string sku, string name)
    {
        var created = await Products.CreateAsync(new ProductInput { Sku = sku, Name = name, CategoryId = CategoryId, Unit = "szt", NetPrice = 20m });
        Assert.True(created.IsSuccess, created.Error);
        return created.Id!.Value;
    }

    private static void Scan(IRenderedComponent<NewStockDocumentPage> page, string text)
    {
        var picker = page.FindComponent<ProductPicker>();
        picker.Find("input").Input(text);
        picker.Find("input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static IReadOnlyList<string?> Quantities(IRenderedComponent<NewStockDocumentPage> page) =>
        page.FindComponents<MudNumericField<decimal?>>()
            .Where(field => field.Markup.Contains("Ilość")) // a receipt line also has a numeric cost field
            .Select(field => field.Find("input").GetAttribute("value"))
            .ToList();

    private static void AssertNotFound(IRenderedComponent<NewStockDocumentPage> page, string text)
    {
        page.WaitForAssertion(() => Assert.Contains($"Nie znaleziono produktu o kodzie {text}", page.Markup));
        Assert.Empty(Quantities(page));
    }

    // The case from the bug report: the code "1" is also a fragment of "10" and of both EANs.
    [Fact]
    public async Task Overlapping_codes_1_and_10_and_a_full_ean_each_add_their_own_product()
    {
        var bogdan = await NewProductAsync("1", "BOGDAN");
        await NewProductAsync("10", "1212");
        var ean = (await Products.GetForEditAsync(bogdan))!.Ean!;
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, "1");
        page.WaitForAssertion(() => Assert.Equal(["1"], Quantities(page)));
        Assert.Contains("BOGDAN", page.Markup);
        Assert.DoesNotContain("1212", page.Markup);

        Scan(page, "10");
        page.WaitForAssertion(() => Assert.Equal(["1", "1"], Quantities(page)));
        Assert.Contains("1212", page.Markup);

        Scan(page, ean);
        page.WaitForAssertion(() => Assert.Equal(["2", "1"], Quantities(page)));
    }

    [Fact]
    public async Task Exact_sku_ignores_letter_case_and_surrounding_spaces_and_a_second_scan_adds_one()
    {
        var sku = Unique("sku");
        await NewProductAsync(sku, Unique("Produkt"));
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, sku.ToLowerInvariant());
        page.WaitForAssertion(() => Assert.Equal(["1"], Quantities(page)));

        Scan(page, $"  {sku.ToUpperInvariant()} ");
        page.WaitForAssertion(() => Assert.Equal(["2"], Quantities(page)));
    }

    [Fact]
    public async Task Ean_wins_over_a_sku_with_the_same_text()
    {
        var byEan = await NewProductAsync(Unique("SKU"), Unique("Po-ean"));
        var ean = (await Products.GetForEditAsync(byEan))!.Ean!;
        var other = Unique("Po-sku");
        await NewProductAsync(ean, other);
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, ean);

        page.WaitForAssertion(() => Assert.Equal(["1"], Quantities(page)));
        Assert.DoesNotContain(other, page.Markup);
    }

    [Fact]
    public async Task Several_matches_by_fragment_show_a_list_and_add_nothing()
    {
        var fragment = Unique("frag");
        await NewProductAsync(Unique("SKU"), $"{fragment}-a");
        await NewProductAsync(Unique("SKU"), $"{fragment}-b");
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, fragment);

        page.WaitForAssertion(() => Assert.Equal(2, page.FindComponent<ProductPicker>().FindAll(".mud-list-item").Count));
        Assert.Empty(Quantities(page));
    }

    [Fact]
    public async Task The_only_match_by_fragment_is_added()
    {
        var fragment = Unique("jeden");
        await NewProductAsync(Unique("SKU"), $"Produkt {fragment} koniec");
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, fragment);

        page.WaitForAssertion(() => Assert.Equal(["1"], Quantities(page)));
    }

    [Fact]
    public void No_match_shows_the_message_and_adds_nothing()
    {
        var text = Unique("brak");
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, text);

        AssertNotFound(page, text);
    }

    [Fact]
    public async Task Inactive_product_is_not_added_by_exact_sku_and_no_other_product_takes_its_place()
    {
        var sku = Unique("NIEAKTYWNY").ToUpperInvariant();
        var inactive = await NewProductAsync(sku, Unique("Nieaktywny"));
        await NewProductAsync(sku + "0", Unique("Aktywny"));
        await Products.SetActiveAsync(inactive, false);
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, sku);

        AssertNotFound(page, sku);
    }

    [Fact]
    public async Task Inactive_product_is_not_added_by_ean_or_by_fragment()
    {
        var name = Unique("Wycofany");
        var inactive = await NewProductAsync(Unique("SKU"), name);
        var ean = (await Products.GetForEditAsync(inactive))!.Ean!;
        await Products.SetActiveAsync(inactive, false);
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, ean);
        AssertNotFound(page, ean);

        Scan(page, name);
        AssertNotFound(page, name);
    }

    [Fact]
    public async Task Two_fast_scans_of_one_code_add_one_line_with_quantity_two()
    {
        var sku = Unique("SKU");
        await NewProductAsync(sku, Unique("Produkt"));
        var page = _context.Render<NewStockDocumentPage>();

        Scan(page, sku);
        var picker = page.FindComponent<ProductPicker>();
        picker.WaitForAssertion(() => Assert.Equal(string.Empty, picker.Find("input").GetAttribute("value") ?? string.Empty));
        Scan(page, sku);

        page.WaitForAssertion(() => Assert.Equal(["2"], Quantities(page)));
    }
}
