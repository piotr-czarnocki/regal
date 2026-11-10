using System.Text.Json;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Invoices;

namespace Regal.UnitTests;

/// <summary>Proforma and correction rules that need no database (SPEC 5.12).</summary>
public class CorrectionRulesTests
{
    private static CorrectionDraft Correction(params CorrectionDraftLine[] lines) =>
        new() { RequestId = Guid.NewGuid(), InvoiceId = 1, Reason = "Zwrot", Lines = [.. lines] };

    private static CorrectionDraftLine After(int lineId = 1, decimal quantity = 1, decimal price = 10, decimal discount = 0) =>
        new() { LineId = lineId, Quantity = quantity, NetPrice = price, DiscountPercent = discount };

    [Fact]
    public void A_complete_correction_passes() => Assert.Null(InvoiceRules.ValidateCorrection(Correction(After())));

    [Fact]
    public void Zero_quantity_takes_a_whole_line_back() => Assert.Null(InvoiceRules.ValidateCorrection(Correction(After(quantity: 0))));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void A_reason_is_required(string? reason)
    {
        var draft = Correction(After());
        draft.Reason = reason;

        Assert.Equal("Podaj przyczynę korekty.", InvoiceRules.ValidateCorrection(draft));
    }

    [Fact]
    public void A_long_reason_is_refused()
    {
        var draft = Correction(After());
        draft.Reason = new string('x', InvoiceRules.MaxCorrectionReasonLength + 1);

        Assert.Contains("najwyżej 500", InvoiceRules.ValidateCorrection(draft));
    }

    [Theory]
    [InlineData(-1, 10, 0, "ujemna")]
    [InlineData(1, -1, 0, "Cena netto")]
    [InlineData(1, 10.001, 0, "Cena netto")]
    [InlineData(1, 10, 101, "Rabat")]
    public void Line_values_are_checked(decimal quantity, decimal price, decimal discount, string message) =>
        Assert.Contains(message, InvoiceRules.ValidateCorrection(Correction(After(quantity: quantity, price: price, discount: discount))));

    [Fact]
    public void A_line_appears_once() =>
        Assert.Contains("tylko raz", InvoiceRules.ValidateCorrection(Correction(After(), After())));

    [Fact]
    public void A_proforma_cannot_come_from_an_order()
    {
        var draft = new InvoiceDraft
        {
            RequestId = Guid.NewGuid(),
            ContractorId = 1,
            Kind = InvoiceKind.Proforma,
            OrderId = 5,
            Lines = [new InvoiceDraftLine { ProductId = 1, Quantity = 1 }],
        };

        Assert.Contains("nie proformę", InvoiceRules.Validate(draft));
    }

    [Fact]
    public void A_draft_cannot_ask_for_a_correction_kind()
    {
        var draft = new InvoiceDraft
        {
            RequestId = Guid.NewGuid(),
            ContractorId = 1,
            Kind = InvoiceKind.Correction,
            Lines = [new InvoiceDraftLine { ProductId = 1, Quantity = 1 }],
        };

        Assert.Equal("Wybierz rodzaj faktury.", InvoiceRules.Validate(draft));
    }

    [Theory]
    [InlineData(InvoiceKind.Vat, "vat")]
    [InlineData(InvoiceKind.Proforma, "proforma")]
    public void The_payload_names_the_kind(InvoiceKind kind, string expected)
    {
        var invoice = new Invoice { Kind = kind, ExternalOrderId = "regal-1", PaymentMethod = "transfer" };

        using var json = JsonDocument.Parse(InvoicePayload.Build(invoice, new Contractor { Name = "Sklep" }));

        Assert.Equal(expected, json.RootElement.GetProperty("invoice").GetProperty("kind").GetString());
    }

    [Fact]
    public void A_correction_payload_gives_the_line_change_and_both_states()
    {
        var before = new InvoiceLine { Id = 7, ProductName = "Mleko", Unit = "szt", VatRate = "23", Quantity = 4, GrossValue = 98.40m };
        var correction = new Invoice
        {
            Kind = InvoiceKind.Correction,
            ExternalOrderId = "regal-2",
            PaymentMethod = "transfer",
            CorrectionReason = "Zwrot",
            Lines = [new InvoiceLine { ProductName = "Mleko", Unit = "szt", VatRate = "23", Quantity = -3, GrossValue = -73.80m, CorrectsLineId = 7 }],
        };

        using var json = JsonDocument.Parse(InvoicePayload.BuildCorrection(correction, 555, new Dictionary<int, InvoiceLine> { [7] = before }, new Contractor { Name = "Sklep" }));

        var invoice = json.RootElement.GetProperty("invoice");
        Assert.Equal(("correction", 555L, 555L), (invoice.GetProperty("kind").GetString(), invoice.GetProperty("invoice_id").GetInt64(), invoice.GetProperty("from_invoice_id").GetInt64()));
        var position = Assert.Single(invoice.GetProperty("positions").EnumerateArray());
        Assert.Equal((-3m, -73.80m), (position.GetProperty("quantity").GetDecimal(), position.GetProperty("total_price_gross").GetDecimal()));
        Assert.Equal("correction_before", position.GetProperty("correction_before_attributes").GetProperty("kind").GetString());
        Assert.Equal((1m, 24.60m), (
            position.GetProperty("correction_after_attributes").GetProperty("quantity").GetDecimal(),
            position.GetProperty("correction_after_attributes").GetProperty("total_price_gross").GetDecimal()));
    }
}
