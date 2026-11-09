using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.E2ETests;

/// <summary>The manual scenarios S1, S4 and S8 driven through the browser.</summary>
[Collection(AppCollection.Name)]
public sealed class WarehouseScenarioTests(AppFixture app)
{
    private async Task<(int Id, string Name, string Ean)> NewGoodsAsync(bool hasExpiry = false)
    {
        var category = await app.Categories.SaveAsync(null, AppFixture.Unique("Kategoria"));
        var name = AppFixture.Unique("Mleko");
        var created = await app.Products.CreateAsync(new ProductInput
        {
            Sku = AppFixture.Unique("SKU"),
            Name = name,
            CategoryId = category.Id,
            Unit = "szt",
            NetPrice = 4m,
            HasExpiry = hasExpiry,
        });
        var ean = (await app.Products.GetForEditAsync(created.Id!.Value))!.Ean!;
        return (created.Id.Value, name, ean);
    }

    private async Task<string> NewSupplierAsync()
    {
        var name = AppFixture.Unique("Hurtownia");
        await app.Contractors.SaveAsync(null, new ContractorInput { Name = name });
        return name;
    }

    private async Task<(decimal Quantity, decimal Average)> StockOfAsync(int productId)
    {
        await using var db = await app.Host.DbContextFactory.CreateDbContextAsync();
        var quantity = await db.Stock.Where(s => s.Batch!.ProductId == productId).SumAsync(s => s.Quantity);
        var average = await db.Products.Where(p => p.Id == productId).Select(p => p.AverageCost).SingleAsync();
        return (quantity, average);
    }

    private static async Task ReceiveAsync(IPage page, string supplier, string ean, string quantity, string cost, string batch)
    {
        await AppFixture.OpenAsync(page, "/stock-documents/new");
        await page.ChooseAsync("Dostawca", supplier);
        await page.ScanAsync(ean);
        await page.FillFieldAsync("Ilość (szt)", quantity);
        await page.FillFieldAsync("Cena zakupu (zł)", cost);
        await page.FillFieldAsync("Numer partii", batch);
        await page.ApproveAsync("Zatwierdź", "Zatwierdź");
        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/stock-documents/\d+$"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("PZ/");
    }

    // S1: the data sets come from docs/scenariusze-testowe.md and from the weighted average by hand.
    [Theory]
    [InlineData("100", "2,50", "50", "3,10", 150, 2.7)]
    [InlineData("10", "5", "10", "7", 20, 6)]
    [InlineData("3", "0,99", "1", "100", 4, 25.7425)]
    public async Task Two_receipts_of_one_batch_give_the_weighted_average(
        string quantity1, string cost1, string quantity2, string cost2, double expectedStock, double expectedAverage)
    {
        var product = await NewGoodsAsync();
        var supplier = await NewSupplierAsync();
        var page = await app.NewPageAsync(app.AdminId);

        await page.WithScreenshotOnFailureAsync($"receipts-{quantity1}-{quantity2}", async () =>
        {
            await ReceiveAsync(page, supplier, product.Ean, quantity1, cost1, "L-1");
            await ReceiveAsync(page, supplier, product.Ean, quantity2, cost2, "l-1");

            await AppFixture.OpenAsync(page, "/stock");
            await page.GetByPlaceholder("Szukaj: nazwa, SKU, EAN").FillAsync(product.Name);
            await Assertions.Expect(page.GetByText(product.Name)).ToBeVisibleAsync();
        });

        Assert.Equal(((decimal)expectedStock, (decimal)expectedAverage), await StockOfAsync(product.Id));
    }

    // S4: first expired, first out, previewed before approval.
    [Fact]
    public async Task Issue_takes_the_batch_expiring_first_and_shows_it_in_the_preview()
    {
        var product = await NewGoodsAsync(hasExpiry: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var posted = await app.Documents.PostAsync(new StockDocumentDraft
        {
            RequestId = Guid.NewGuid(),
            Type = StockDocumentType.PW,
            Lines =
            [
                new StockDocumentLine { ProductId = product.Id, Quantity = 5, UnitCost = 2m, BatchNumber = "POZNA", ExpiryDate = today.AddDays(60) },
                new StockDocumentLine { ProductId = product.Id, Quantity = 5, UnitCost = 2m, BatchNumber = "WCZESNA", ExpiryDate = today.AddDays(10) },
            ],
        });
        Assert.True(posted.IsSuccess, posted.Error);
        var page = await app.NewPageAsync(app.AdminId);

        await page.WithScreenshotOnFailureAsync("fefo", async () =>
        {
            await AppFixture.OpenAsync(page, "/stock-documents/new");
            await page.SelectAsync("Typ dokumentu", "WZ");
            await page.ScanAsync(product.Ean);
            await page.FillFieldAsync("Ilość (szt)", "7");
            await page.Button("Pokaż podgląd").ClickAsync();

            var rows = page.Locator("table tbody tr");
            await Assertions.Expect(rows.Nth(0)).ToContainTextAsync("WCZESNA");
            await Assertions.Expect(rows.Nth(1)).ToContainTextAsync("POZNA");

            await page.Button("Zatwierdź").ClickAsync();
            await page.DialogButton("Zatwierdź").ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("WZ/");
        });

        Assert.Equal(3m, (await StockOfAsync(product.Id)).Quantity);
    }

    // S8: a code confirmed with Enter adds a line; scanning again adds one; Enter never approves.
    [Fact]
    public async Task Enter_adds_lines_and_never_approves_the_document()
    {
        var product = await NewGoodsAsync();
        var page = await app.NewPageAsync(app.AdminId);

        await page.WithScreenshotOnFailureAsync("enter", async () =>
        {
            await AppFixture.OpenAsync(page, "/stock-documents/new");
            await page.SelectAsync("Typ dokumentu", "PW");
            await page.ScanAsync(product.Ean);
            await page.ScanAsync(product.Ean);
            await Assertions.Expect(page.Field("Ilość (szt)")).ToHaveValueAsync("2");

            await page.Field("Ilość (szt)").PressAsync("Enter");
            await page.Field("Dodaj produkt: nazwa, SKU lub EAN").FillAsync("nie-ma-takiego-kodu");
            await page.Field("Dodaj produkt: nazwa, SKU lub EAN").PressAsync("Enter");
            await Assertions.Expect(page.GetByText("Nie znaleziono produktu o kodzie nie-ma-takiego-kodu")).ToBeVisibleAsync();

            await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/stock-documents/new$"));
            await Assertions.Expect(page.Button("Zatwierdź")).ToBeDisabledAsync();
        });

        Assert.Equal(0m, (await StockOfAsync(product.Id)).Quantity);
    }

    // S10, step 7: tables turn into cards on a phone and nothing is wider than the screen.
    [Fact]
    public async Task Stock_page_fits_a_phone_screen()
    {
        var page = await app.NewPageAsync(app.AdminId, phone: true);

        await page.WithScreenshotOnFailureAsync("phone", async () =>
        {
            await AppFixture.OpenAsync(page, "/stock");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Stany magazynowe" })).ToBeVisibleAsync();
        });

        var overflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.False(overflow, "The page scrolls sideways on a phone.");
    }
}
