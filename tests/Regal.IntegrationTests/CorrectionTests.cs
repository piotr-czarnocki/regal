using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Analytics;
using Regal.Web.Features.Invoices;

namespace Regal.IntegrationTests;

/// <summary>Proformas and correcting invoices (SPEC 5.12).</summary>
[Collection(PostgresCollection.Name)]
public class CorrectionTests(PostgresFixture postgres) : InvoiceTestBase(postgres)
{
    private AnalyticsService Analytics => new(Postgres.DbContextFactory, new FixedUser(UserId), Clock);

    private async Task<int> SentInvoiceAsync(params InvoiceDraftLine[] lines)
    {
        var id = await IssueOkAsync(Draft(lines));
        await DrainAsync();
        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(id)).Status);
        return id;
    }

    private async Task<CorrectionDraft> CorrectionOfAsync(int invoiceId, Action<CorrectionDraftLine, InvoiceLine> change)
    {
        var invoice = await InvoiceAsync(invoiceId);
        return new CorrectionDraft
        {
            RequestId = Guid.NewGuid(),
            InvoiceId = invoiceId,
            Reason = "Zwrot towaru",
            Lines = invoice.Lines.Select(line =>
            {
                var after = new CorrectionDraftLine { LineId = line.Id, Quantity = line.Quantity, NetPrice = line.NetPrice, DiscountPercent = line.DiscountPercent };
                change(after, line);
                return after;
            }).ToList(),
        };
    }

    private async Task<int> CorrectOkAsync(CorrectionDraft draft)
    {
        var result = await Invoices.CorrectAsync(draft);
        Assert.True(result.IsSuccess, result.Error);
        return result.Id!.Value;
    }

    [Fact]
    public async Task A_proforma_is_sent_but_moves_no_stock_and_is_not_a_sale()
    {
        var goods = await StockedGoodsAsync(10);
        var draft = Draft(Item(goods, 4, netPrice: 20m));
        draft.Kind = InvoiceKind.Proforma;
        var from = Today;

        var id = await IssueOkAsync(draft);
        await DrainAsync();

        var invoice = await InvoiceAsync(id);
        Assert.Equal((InvoiceKind.Proforma, InvoiceStatus.Sent, (int?)null), (invoice.Kind, invoice.Status, invoice.StockDocumentId));
        Assert.Equal(10m, await StockOfAsync(goods));
        Assert.Contains(Fakturownia.Payloads, p => p.Contains("\"kind\":\"proforma\"") && p.Contains(invoice.ExternalOrderId));
        var sales = await Analytics.GetSalesByProductAsync(new AnalyticsFilter(from, Today, CategoryId));
        Assert.DoesNotContain(sales, s => s.ProductId == goods);
    }

    [Fact]
    public async Task A_proforma_cannot_complete_a_customer_order()
    {
        var draft = Draft(Item(await StockedGoodsAsync(10), 1));
        draft.Kind = InvoiceKind.Proforma;
        draft.OrderId = 1;

        var result = await Invoices.IssueAsync(draft);

        Assert.Contains("nie proformę", result.Error);
    }

    [Fact]
    public async Task A_cancelled_proforma_needs_no_stock_reversal()
    {
        var draft = Draft(Item(await StockedGoodsAsync(10), 1));
        draft.Kind = InvoiceKind.Proforma;
        var id = await IssueOkAsync(draft);

        var result = await Invoices.CancelAsync(id, Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(InvoiceStatus.Cancelled, (await InvoiceAsync(id)).Status);
    }

    [Fact]
    public async Task Returning_goods_puts_them_back_on_their_batch_and_records_the_difference()
    {
        var goods = await StockedGoodsAsync(10, cost: 5m);
        var id = await SentInvoiceAsync(Item(goods, 4, netPrice: 20m));

        var correctionId = await CorrectOkAsync(await CorrectionOfAsync(id, (after, _) => after.Quantity = 1));

        var correction = await InvoiceAsync(correctionId);
        var line = Assert.Single(correction.Lines);
        Assert.Equal((InvoiceKind.Correction, InvoiceStatus.ToSend, id), (correction.Kind, correction.Status, correction.CorrectsInvoiceId!.Value));
        Assert.Equal((-3m, -60m, -13.80m, -73.80m, 5m), (line.Quantity, line.NetValue, line.VatValue, line.GrossValue, line.UnitCost));
        Assert.Equal((-60m, -73.80m), (correction.NetTotal, correction.GrossTotal));
        Assert.Equal(9m, await StockOfAsync(goods));
        Assert.Equal(5m, await AverageOfAsync(goods));

        var wz = await DocumentAsync(correction.StockDocumentId!.Value);
        Assert.Equal((StockDocumentType.WZ, (await InvoiceAsync(id)).StockDocumentId), (wz.Type, wz.ReversesDocumentId));
    }

    [Fact]
    public async Task A_price_correction_moves_no_goods()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await SentInvoiceAsync(Item(goods, 2, netPrice: 20m));

        var correctionId = await CorrectOkAsync(await CorrectionOfAsync(id, (after, _) => after.DiscountPercent = 10));

        var line = Assert.Single((await InvoiceAsync(correctionId)).Lines);
        Assert.Equal((0m, -4m, 10m), (line.Quantity, line.NetValue, line.DiscountPercent));
        Assert.Null((await InvoiceAsync(correctionId)).StockDocumentId);
        Assert.Equal(8m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Sales_and_margin_are_net_of_the_correction()
    {
        var goods = await StockedGoodsAsync(10, cost: 5m);
        var from = Today;
        var id = await SentInvoiceAsync(Item(goods, 4, netPrice: 20m));
        await CorrectOkAsync(await CorrectionOfAsync(id, (after, _) => after.Quantity = 1));

        var row = Assert.Single(await Analytics.GetSalesByProductAsync(new AnalyticsFilter(from, Today, CategoryId)), r => r.ProductId == goods);

        Assert.Equal((1m, 20m, 5m, 15m), (row.Quantity, row.NetSales, row.Cost, row.Margin));
    }

    [Fact]
    public async Task The_correction_payload_names_the_corrected_invoice_and_both_states()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await SentInvoiceAsync(Item(goods, 2, netPrice: 10m));
        var original = await InvoiceAsync(id);

        var correctionId = await CorrectOkAsync(await CorrectionOfAsync(id, (after, _) => after.Quantity = 1));

        var payload = (await MessageAsync(correctionId)).Payload;
        using var json = JsonDocument.Parse(payload);
        var invoice = json.RootElement.GetProperty("invoice");
        Assert.Equal("correction", invoice.GetProperty("kind").GetString());
        Assert.Equal(original.FakturowniaId, invoice.GetProperty("invoice_id").GetInt64());
        Assert.Equal(original.FakturowniaId, invoice.GetProperty("from_invoice_id").GetInt64());
        Assert.Equal("Zwrot towaru", invoice.GetProperty("correction_reason").GetString());
        var position = Assert.Single(invoice.GetProperty("positions").EnumerateArray());
        Assert.Equal(2m, position.GetProperty("correction_before_attributes").GetProperty("quantity").GetDecimal());
        Assert.Equal(1m, position.GetProperty("correction_after_attributes").GetProperty("quantity").GetDecimal());
        Assert.Equal(12.30m, position.GetProperty("correction_after_attributes").GetProperty("total_price_gross").GetDecimal());

        await DrainAsync();
        Assert.Equal(InvoiceStatus.Sent, (await InvoiceAsync(correctionId)).Status);
    }

    [Fact]
    public async Task Only_a_sent_vat_invoice_can_be_corrected_and_only_once()
    {
        var goods = await StockedGoodsAsync(10);
        var sent = await SentInvoiceAsync(Item(goods, 2));
        var first = await CorrectOkAsync(await CorrectionOfAsync(sent, (after, _) => after.Quantity = 1));
        await DrainAsync();

        // Issued after the last send, so it is still waiting.
        var waiting = await IssueOkAsync(Draft(Item(goods, 1)));
        var notSent = await Invoices.CorrectAsync(await CorrectionOfAsync(waiting, (after, _) => after.Quantity = 0));
        var second = await Invoices.CorrectAsync(await CorrectionOfAsync(sent, (after, _) => after.Quantity = 0));
        var ofCorrection = await Invoices.CorrectAsync(await CorrectionOfAsync(first, (after, _) => (after.Quantity, after.NetPrice) = (0, 1)));

        Assert.Contains("wysłaną do Fakturowni", notSent.Error);
        Assert.Contains("ma już korektę", second.Error);
        Assert.Contains("tylko fakturę VAT", ofCorrection.Error);
    }

    [Theory]
    [InlineData("more", "tylko zmniejszyć ilość")]
    [InlineData("same", "niczego nie zmienia")]
    [InlineData("no-reason", "przyczynę")]
    [InlineData("half-piece", "całkowit")]
    public async Task Corrections_are_validated(string variant, string message)
    {
        var id = await SentInvoiceAsync(Item(await StockedGoodsAsync(10), 2));
        var draft = await CorrectionOfAsync(id, (after, _) =>
        {
            switch (variant)
            {
                case "more": after.Quantity = 3; break;
                case "half-piece": after.Quantity = 1.5m; break;
            }
        });
        if (variant == "no-reason")
        {
            draft.Reason = "  ";
        }

        var result = await Invoices.CorrectAsync(draft);

        Assert.False(result.IsSuccess);
        Assert.Contains(message, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sending_the_same_correction_twice_creates_one()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await SentInvoiceAsync(Item(goods, 4));
        var draft = await CorrectionOfAsync(id, (after, _) => after.Quantity = 2);

        var first = await CorrectOkAsync(draft);
        var second = await CorrectOkAsync(draft);

        Assert.Equal(first, second);
        Assert.Equal(8m, await StockOfAsync(goods));
    }

    [Fact]
    public async Task Parallel_corrections_of_one_invoice_create_one()
    {
        var goods = await StockedGoodsAsync(10);
        var id = await SentInvoiceAsync(Item(goods, 4));
        var drafts = new List<CorrectionDraft>();
        for (var i = 0; i < 6; i++)
        {
            drafts.Add(await CorrectionOfAsync(id, (after, _) => after.Quantity = 2));
        }

        var invoices = Invoices;
        var results = await Task.WhenAll(drafts.Select(draft => Task.Run(() => invoices.CorrectAsync(draft))));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(8m, await StockOfAsync(goods));
    }

    /// <summary>
    /// Without goods coming back no product is locked, so only the lock on the corrected invoice
    /// puts these in order; without it one of them hits the unique index as an error.
    /// </summary>
    [Fact]
    public async Task Parallel_price_corrections_of_one_invoice_create_one_and_refuse_the_rest_cleanly()
    {
        var id = await SentInvoiceAsync(Item(await StockedGoodsAsync(10), 4, netPrice: 20m));
        var drafts = new List<CorrectionDraft>();
        for (var i = 0; i < 6; i++)
        {
            drafts.Add(await CorrectionOfAsync(id, (after, _) => after.NetPrice = 10m + i)); // all below the invoiced 20 zł, so each one changes something
        }

        var invoices = Invoices;
        var results = await Task.WhenAll(drafts.Select(draft => Task.Run(() => invoices.CorrectAsync(draft))));

        Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Contains("ma już korektę", r.Error));
    }

    [Fact]
    public async Task A_correction_cannot_be_cancelled()
    {
        var id = await SentInvoiceAsync(Item(await StockedGoodsAsync(10), 2));
        var correctionId = await CorrectOkAsync(await CorrectionOfAsync(id, (after, _) => after.Quantity = 1));

        var result = await Invoices.CancelAsync(correctionId, Guid.NewGuid());

        Assert.Contains("Korekty nie można anulować", result.Error);
    }

    [Fact]
    public async Task Database_refuses_a_correction_line_that_adds_quantity()
    {
        var id = await SentInvoiceAsync(Item(await StockedGoodsAsync(10), 2));
        var correctionId = await CorrectOkAsync(await CorrectionOfAsync(id, (after, _) => after.Quantity = 1));
        await using var db = Postgres.CreateAppDbContext();
        var line = await db.InvoiceLines.AsNoTracking().FirstAsync(l => l.InvoiceId == correctionId);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            db.InvoiceLines.Add(new InvoiceLine
            {
                InvoiceId = correctionId, ProductId = line.ProductId, ProductName = "x", Unit = "szt", VatRate = "23",
                Quantity = 1, CorrectsLineId = line.CorrectsLineId,
            });
            await db.SaveChangesAsync();
        });

        Assert.Contains("CK_InvoiceLines_Quantity_Positive", exception.InnerException!.Message);
    }
}
