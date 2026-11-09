using Bunit;
using Microsoft.AspNetCore.Components;
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
}
