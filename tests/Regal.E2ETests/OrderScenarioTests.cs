using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.E2ETests;

/// <summary>Scenario Z1: an order reserves goods, a manual WZ cannot take them, the invoice from the order can.</summary>
[Collection(AppCollection.Name)]
public sealed class OrderScenarioTests(AppFixture app)
{
    [Fact]
    public async Task An_order_reserves_goods_until_its_invoice_takes_them()
    {
        var category = await app.Categories.SaveAsync(null, AppFixture.Unique("Kategoria"));
        var goods = await app.Products.CreateAsync(new ProductInput
        {
            Sku = AppFixture.Unique("TOW"), Name = AppFixture.Unique("Chleb"), CategoryId = category.Id, Unit = "szt", NetPrice = 5m,
        });
        var ean = (await app.Products.GetForEditAsync(goods.Id!.Value))!.Ean!;
        var buyer = AppFixture.Unique("Sklep");
        await app.Contractors.SaveAsync(null, new ContractorInput { Name = buyer });
        await app.Documents.PostAsync(new StockDocumentDraft
        {
            RequestId = Guid.NewGuid(),
            Type = StockDocumentType.PW,
            Lines = [new StockDocumentLine { ProductId = goods.Id.Value, Quantity = 10, UnitCost = 2m }],
        });

        var page = await app.NewPageAsync(app.AdminId);
        await page.WithScreenshotOnFailureAsync("order", async () =>
        {
            // The order reserves 8 of 10.
            await AppFixture.OpenAsync(page, "/orders/new");
            await page.ChooseAsync("Nabywca", buyer);
            await page.ScanAsync(ean);
            await page.FillFieldAsync("Ilość (szt)", "8");
            await page.Button("Zapisz zamówienie").ClickAsync();
            await page.DialogButton("Zapisz").ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("ZK/");
            var orderUrl = page.Url;

            // A manual WZ for 3 does not fit into the 2 left for sale.
            await AppFixture.OpenAsync(page, "/stock-documents/new");
            await page.SelectAsync("Typ dokumentu", "WZ");
            await page.ScanAsync(ean);
            await page.FillFieldAsync("Ilość (szt)", "3");
            await page.Button("Pokaż podgląd").ClickAsync();
            await Assertions.Expect(page.GetByText("zarezerwowano w zamówieniach")).ToBeVisibleAsync();

            // The invoice from the order takes all 8.
            await AppFixture.OpenAsync(page, new Uri(orderUrl).AbsolutePath);
            await page.GetByRole(AriaRole.Link, new() { Name = "Wystaw fakturę" }).ClickAsync();
            await Assertions.Expect(page.GetByText("Faktura do zamówienia")).ToBeVisibleAsync();
            await Assertions.Expect(page.Field("Ilość (szt)")).ToHaveValueAsync("8");
            await page.ApproveAsync("Wystaw fakturę", "Wystaw");
            await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/invoices/\d+$"));
        });

        await using var db = await app.Host.DbContextFactory.CreateDbContextAsync();
        var order = await db.CustomerOrders.SingleAsync(o => o.Contractor!.Name == buyer);
        Assert.Equal(CustomerOrderStatus.Completed, order.Status);
        Assert.NotNull(order.InvoiceId);
        Assert.Equal(2m, await db.Stock.Where(s => s.Batch!.ProductId == goods.Id.Value).SumAsync(s => s.Quantity));
    }
}
