using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Products;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class InvoicePagesTests : InvoiceTestBase
{
    private readonly BunitContext _context = new();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;

    public InvoicePagesTests(PostgresFixture postgres)
        : base(postgres)
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Services.AddScoped(_ => Invoices);
        _context.Services.AddScoped(_ => new InvoiceQueries(Postgres.DbContextFactory, new FixedUser(UserId)));
        _context.Services.AddScoped(_ => Documents);
        _context.Services.AddScoped(_ => Stock);
        _context.Services.AddScoped(_ => Contractors);
        _context.Services.AddScoped(_ => new InvoicePaymentService(Postgres.DbContextFactory, new FixedUser(UserId), Fakturownia, Clock));
        _context.Services.AddScoped(_ => new Regal.Web.Features.Orders.CustomerOrderService(Postgres.DbContextFactory, new FixedUser(UserId), Clock));
        _context.Render<MudPopoverProvider>();
        _dialogs = _context.Render<MudDialogProvider>();
    }

    public override async Task DisposeAsync() => await _context.DisposeAsync();

    private static void ClickButton<T>(IRenderedComponent<T> fragment, string text)
        where T : IComponent =>
        fragment.FindAll("button").Single(button => button.TextContent.Contains(text)).Click();

    [Fact]
    public async Task List_shows_a_sent_invoice_with_its_number_and_status()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        await DrainAsync();
        var number = (await InvoiceAsync(id)).Number!;

        var page = _context.Render<InvoicesPage>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(number, page.Markup);
            Assert.Contains("Wysłana", page.Markup);
        });
    }

    [Fact]
    public async Task Details_show_lines_margin_wz_link_and_pdf_of_a_sent_invoice()
    {
        var goods = await StockedGoodsAsync(cost: 5m);
        var id = await IssueOkAsync(Draft(Item(goods, 2)));
        await DrainAsync();
        var invoice = await InvoiceAsync(id);
        var wzNumber = (await DocumentAsync(invoice.StockDocumentId!.Value)).Number;

        var page = _context.Render<InvoiceDetailsPage>(parameters => parameters.Add(x => x.Id, id));

        page.WaitForAssertion(() => Assert.Contains(invoice.Number!, page.Markup));
        Assert.Contains(wzNumber, page.Markup);
        Assert.Contains(invoice.PdfUrl!, page.Markup);
        Assert.Contains(StockLabelsMoney(30m), page.Markup); // 2 × 20 net − 2 × 5 cost
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Anuluj fakturę"));
    }

    [Fact]
    public async Task Cancelling_from_the_page_reverses_the_wz()
    {
        var goods = await StockedGoodsAsync();
        var id = await IssueOkAsync(Draft(Item(goods, 3)));

        var page = _context.Render<InvoiceDetailsPage>(parameters => parameters.Add(x => x.Id, id));
        page.WaitForAssertion(() => Assert.Contains("Do wysłania", page.Markup));
        ClickButton(page, "Anuluj fakturę");
        _dialogs.WaitForAssertion(() => Assert.Contains("Tej operacji nie można cofnąć", _dialogs.Markup));
        _dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Anuluj fakturę").Click();

        page.WaitForAssertion(() => Assert.Contains("Anulowana", page.Markup));
        Assert.Equal(InvoiceStatus.Cancelled, (await InvoiceAsync(id)).Status);
        Assert.Equal(100m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Failed_invoice_shows_the_error_and_can_be_retried_from_the_page()
    {
        var id = await IssueOkAsync(Draft(Item(await NewServiceAsync(), 1)));
        Fakturownia.CreateErrors.Enqueue(new FakturowniaRejectedException("HTTP 422: zły NIP"));
        await DrainAsync();

        var page = _context.Render<InvoiceDetailsPage>(parameters => parameters.Add(x => x.Id, id));
        page.WaitForAssertion(() => Assert.Contains("zły NIP", page.Markup));
        ClickButton(page, "Ponów wysyłkę");

        page.WaitForAssertion(() => Assert.Contains("Do wysłania", page.Markup));
        await DrainAsync();
        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
    }

    [Fact]
    public async Task A_correction_from_the_page_returns_goods_and_links_both_invoices()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await IssueOkAsync(Draft(Item(goods, 4, netPrice: 20m)));
        await DrainAsync();

        var details = _context.Render<InvoiceDetailsPage>(parameters => parameters.Add(x => x.Id, id));
        details.WaitForAssertion(() => Assert.Contains(details.FindAll("a"), a => a.TextContent.Contains("Korekta") && a.GetAttribute("href") == InvoiceRoutes.Correction(id)));

        var page = _context.Render<InvoiceCorrectionPage>(parameters => parameters.Add(x => x.Id, id));
        page.WaitForAssertion(() => Assert.Contains("Ilość po korekcie", page.Markup));
        page.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Przyczyna korekty").Find("input").Change("Zwrot 3 szt.");
        page.FindComponents<MudNumericField<decimal?>>().Single(f => f.Instance.Label!.StartsWith("Ilość po korekcie")).Find("input").Change("1");
        page.WaitForAssertion(() => Assert.Contains(StockLabelsMoney(-73.80m), page.Markup));
        ClickButton(page, "Wystaw korektę");
        _dialogs.WaitForAssertion(() => Assert.Contains("Korekty nie można anulować", _dialogs.Markup));
        _dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Wystaw korektę").Click();

        var correction = await WaitForCorrectionAsync(id);
        Assert.Equal(9m, await StockOfAsync(goods));

        var corrected = _context.Render<InvoiceDetailsPage>(parameters => parameters.Add(x => x.Id, correction.Id));
        corrected.WaitForAssertion(() => Assert.Contains("Przyczyna: Zwrot 3 szt.", corrected.Markup));
        Assert.Contains("było 4, jest 1", corrected.Markup);
        Assert.DoesNotContain(corrected.FindAll("button"), b => b.TextContent.Contains("Anuluj fakturę"));
    }

    [Fact]
    public async Task Payment_can_be_checked_and_the_invoice_emailed_from_the_page()
    {
        var buyer = await Contractors.SaveAsync(null, new Regal.Web.Features.Contractors.ContractorInput { Name = Unique("Sklep"), Email = "sklep@example.com" });
        var draft = Draft(Item(await NewServiceAsync(), 1));
        draft.ContractorId = buyer.Id;
        var id = await IssueOkAsync(draft);
        await DrainAsync();
        var invoice = await InvoiceAsync(id);
        Fakturownia.SetPayment(invoice.FakturowniaId!.Value, "paid", invoice.GrossTotal, new DateOnly(2026, 6, 20));

        var page = _context.Render<InvoiceDetailsPage>(parameters => parameters.Add(x => x.Id, id));
        page.WaitForAssertion(() => Assert.Contains("Płatność nieznana", page.Markup));
        Assert.Equal("sklep@example.com", page.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Adres e-mail nabywcy").Find("input").GetAttribute("value"));

        ClickButton(page, "Sprawdź płatność");
        page.WaitForAssertion(() => Assert.Contains("Opłacona", page.Markup));
        Assert.Contains("20.06.2026", page.Markup);

        ClickButton(page, "Wyślij mailem");
        page.WaitForAssertion(() => Assert.Contains("na sklep@example.com", page.Markup));
        Assert.Contains((invoice.FakturowniaId!.Value, "sklep@example.com"), Fakturownia.Emails);
    }

    private async Task<Invoice> WaitForCorrectionAsync(int invoiceId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var db = Postgres.CreateAppDbContext();
            if (await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.CorrectsInvoiceId == invoiceId) is { } correction)
            {
                return correction;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("No correction was created.");
    }

    [Fact]
    public void New_invoice_page_loads_with_its_editor()
    {
        var page = _context.Render<NewInvoicePage>();

        Assert.Contains("Nowa faktura", page.Markup);
        Assert.NotNull(page.FindComponent<InvoiceEditor>());
    }

    [Fact]
    public async Task A_service_can_be_found_for_an_invoice_but_not_for_a_stock_document()
    {
        var service = await NewServiceAsync();
        var sku = (await Products.GetForEditAsync(service))!.Sku!;

        Assert.True((await Stock.FindSellableByCodeAsync(sku))!.CanBeAdded);
        Assert.False((await Stock.FindByCodeAsync(sku))!.CanBeAdded);
        Assert.Contains(await Stock.SearchSellableAsync(sku, 10), p => p.Id == service && p.Kind == ProductKind.Service && p.NetPrice == 100m);
        Assert.DoesNotContain(await Stock.SearchGoodsAsync(sku, 10), p => p.Id == service);
    }

    private static string StockLabelsMoney(decimal value) => Regal.Web.Features.Warehouse.StockLabels.Money(value);
}
