using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.E2ETests;

/// <summary>Scenarios F1 and F6 against the running application and its background sender, with a fake Fakturownia.</summary>
[Collection(AppCollection.Name)]
public sealed class InvoiceScenarioTests(AppFixture app)
{
    [Fact]
    public async Task Invoice_with_goods_and_a_service_issues_a_wz_and_is_sent_in_the_background()
    {
        var category = await app.Categories.SaveAsync(null, AppFixture.Unique("Kategoria"));
        var goods = await app.Products.CreateAsync(new ProductInput
        {
            Sku = AppFixture.Unique("TOW"), Name = AppFixture.Unique("Kawa"), CategoryId = category.Id, Unit = "szt", NetPrice = 20m,
        });
        var serviceSku = AppFixture.Unique("USL").ToUpperInvariant();
        await app.Products.CreateAsync(new ProductInput
        {
            Sku = serviceSku, Name = AppFixture.Unique("Montaż"), CategoryId = category.Id, Unit = "h", Kind = ProductKind.Service, NetPrice = 100m,
        });
        var ean = (await app.Products.GetForEditAsync(goods.Id!.Value))!.Ean!;
        var buyer = AppFixture.Unique("Sklep");
        await app.Contractors.SaveAsync(null, new ContractorInput { Name = buyer, Nip = "1234563218" });
        await app.Documents.PostAsync(new StockDocumentDraft
        {
            RequestId = Guid.NewGuid(),
            Type = StockDocumentType.PW,
            Lines = [new StockDocumentLine { ProductId = goods.Id.Value, Quantity = 20, UnitCost = 8m }],
        });

        var page = await app.NewPageAsync(app.AdminId);
        await page.WithScreenshotOnFailureAsync("invoice", async () =>
        {
            await AppFixture.OpenAsync(page, "/invoices/new");
            await page.ChooseAsync("Nabywca", buyer);
            await page.ScanAsync(ean);
            await page.ScanAsync(serviceSku);
            await page.FillFieldAsync("Ilość (szt)", "3");
            await Assertions.Expect(page.GetByText("Brutto: 196,80 zł")).ToBeVisibleAsync(); // 3 × 20 + 23% and 100 + 23%

            await page.ApproveAsync("Wystaw fakturę", "Wystaw");

            await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/invoices/\d+$"));
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "WZ/", Exact = false })).ToBeVisibleAsync();

            // The sender wakes every 5 seconds, and the page reloads itself while it waits.
            await Assertions.Expect(page.GetByText("Wysłana")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "PDF" })).ToBeVisibleAsync();
        });

        await using var db = await app.Host.DbContextFactory.CreateDbContextAsync();
        var invoice = await db.Invoices.SingleAsync(i => i.Contractor!.Name == buyer);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Equal(1, app.Host.Fakturownia.CreatesFor(invoice.ExternalOrderId));
        Assert.Equal(17m, await db.Stock.Where(s => s.Batch!.ProductId == goods.Id.Value).SumAsync(s => s.Quantity));
    }
}
