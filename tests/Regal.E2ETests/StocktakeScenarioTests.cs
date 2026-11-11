using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.E2ETests;

/// <summary>A stocktake done the way it is done in the warehouse: on a phone, with a scanner (SPEC 5.14).</summary>
[Collection(AppCollection.Name)]
public sealed class StocktakeScenarioTests(AppFixture app)
{
    [Fact]
    public async Task Stocktake_on_a_phone_counts_by_scanning_and_books_the_shortage_as_rw()
    {
        var category = await app.Categories.SaveAsync(null, AppFixture.Unique("Kategoria"));
        var goods = await app.Products.CreateAsync(new ProductInput
        {
            Sku = AppFixture.Unique("TOW"), Name = AppFixture.Unique("Kefir"), CategoryId = category.Id, Unit = "szt", NetPrice = 3m,
        });
        var ean = (await app.Products.GetForEditAsync(goods.Id!.Value))!.Ean!;
        await app.Documents.PostAsync(new StockDocumentDraft
        {
            RequestId = Guid.NewGuid(),
            Type = StockDocumentType.PW,
            Lines = [new StockDocumentLine { ProductId = goods.Id.Value, Quantity = 5, UnitCost = 2m }],
        });

        var page = await app.NewPageAsync(app.AdminId, phone: true);
        await page.WithScreenshotOnFailureAsync("stocktake-phone", async () =>
        {
            await AppFixture.OpenAsync(page, "/stocktakes");
            await page.Button("Nowy arkusz spisu").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/stocktakes/\d+$"));
            await page.WaitForSelectorAsync("body[data-interactive='true']");

            // One batch, so each scan counts one piece: 3 found of the 5 on record.
            await page.ScanAsync(ean);
            await page.ScanAsync(ean);
            await page.ScanAsync(ean);
            await Assertions.Expect(page.Field("Policzono (szt)")).ToHaveValueAsync("3");
            await Assertions.Expect(page.Locator(".mud-chip").GetByText("-2")).ToBeVisibleAsync();

            var overflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth + 1");
            Assert.False(overflow, "The stocktake sheet scrolls sideways on a phone.");

            await page.Button("Zatwierdź spis").ClickAsync();
            await page.DialogButton("Zatwierdź").ClickAsync();
            await Assertions.Expect(page.GetByText("Różnice zaksięgowano")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "RW/" })).ToBeVisibleAsync();
        });

        await using var db = await app.Host.DbContextFactory.CreateDbContextAsync();
        Assert.Equal(3m, await db.Stock.Where(s => s.Batch!.ProductId == goods.Id.Value).SumAsync(s => s.Quantity));
    }
}
