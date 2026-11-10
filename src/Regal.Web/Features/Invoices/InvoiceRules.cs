using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Warehouse;

namespace Regal.Web.Features.Invoices;

public sealed class InvoiceDraft
{
    /// <summary>Generated with the draft and kept across retries, so a repeated send creates one invoice.</summary>
    public Guid RequestId { get; set; }

    public int? ContractorId { get; set; }

    public int PaymentDays { get; set; } = InvoiceRules.DefaultPaymentDays;

    public string PaymentMethod { get; set; } = InvoicePaymentMethods.Default;

    /// <summary>The customer order this invoice completes; it uses that order's reservation.</summary>
    public int? OrderId { get; set; }

    /// <summary>A VAT invoice or a proforma; a correction has its own draft.</summary>
    public InvoiceKind Kind { get; set; } = InvoiceKind.Vat;

    public List<InvoiceDraftLine> Lines { get; set; } = [];
}

/// <summary>A correcting invoice: the state of each line after the correction (SPEC 5.12).</summary>
public sealed class CorrectionDraft
{
    /// <summary>Generated with the form and kept across retries, so a repeated send creates one correction.</summary>
    public Guid RequestId { get; set; }

    public int InvoiceId { get; set; }

    public string? Reason { get; set; }

    public List<CorrectionDraftLine> Lines { get; set; } = [];
}

public sealed class CorrectionDraftLine
{
    /// <summary>The line of the corrected invoice.</summary>
    public int LineId { get; set; }

    public decimal Quantity { get; set; }

    public decimal NetPrice { get; set; }

    public decimal DiscountPercent { get; set; }
}

public sealed class InvoiceDraftLine
{
    public int ProductId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Empty means the price from the catalog.</summary>
    public decimal? NetPrice { get; set; }

    public decimal DiscountPercent { get; set; }
}

public static class InvoicePaymentMethods
{
    public const string Default = "transfer";

    /// <summary>Codes as Fakturownia names them.</summary>
    public static readonly IReadOnlyList<string> All = ["transfer", "cash", "card"];
}

public sealed record InvoiceLineAmounts(decimal Net, decimal Vat, decimal Gross);

public static class InvoiceRules
{
    public const int DefaultPaymentDays = 14;
    public const int MaxPaymentDays = 365;
    public const int MaxLines = 200;
    public const decimal MaxNetPrice = 9_999_999_999.99m;

    public static string OidOf(Guid requestId) => $"regal-{requestId:N}";

    /// <summary>"zw" and "np" have no percentage, so no VAT is added.</summary>
    public static decimal VatPercent(string rate) =>
        decimal.TryParse(rate, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) ? percent : 0;

    /// <summary>The discount lowers the net value; VAT is calculated from what remains, per line.</summary>
    public static InvoiceLineAmounts Amounts(decimal quantity, decimal netPrice, decimal discountPercent, string vatRate)
    {
        var net = Round(quantity * netPrice * (100 - discountPercent) / 100);
        var vat = Round(net * VatPercent(vatRate) / 100);
        return new InvoiceLineAmounts(net, vat, net + vat);
    }

    public static string? Validate(InvoiceDraft draft)
    {
        if (draft.RequestId == Guid.Empty)
        {
            return "Brak identyfikatora faktury. Odśwież stronę i spróbuj ponownie.";
        }

        if (draft.ContractorId is null)
        {
            return "Wybierz nabywcę.";
        }

        if (draft.Kind is not (InvoiceKind.Vat or InvoiceKind.Proforma))
        {
            return "Wybierz rodzaj faktury.";
        }

        if (draft.Kind == InvoiceKind.Proforma && draft.OrderId is not null)
        {
            return "Z zamówienia wystawia się fakturę VAT, nie proformę.";
        }

        if (draft.PaymentDays is < 0 or > MaxPaymentDays)
        {
            return $"Termin płatności musi wynosić od 0 do {MaxPaymentDays} dni.";
        }

        if (!InvoicePaymentMethods.All.Contains(draft.PaymentMethod))
        {
            return "Wybierz sposób płatności.";
        }

        if (draft.Lines.Count == 0)
        {
            return "Dodaj co najmniej jedną pozycję.";
        }

        if (draft.Lines.Count > MaxLines)
        {
            return $"Faktura może mieć najwyżej {MaxLines} pozycji.";
        }

        if (draft.Lines.Select(line => line.ProductId).Distinct().Count() != draft.Lines.Count)
        {
            return "Każdy produkt może wystąpić na fakturze tylko raz.";
        }

        foreach (var line in draft.Lines)
        {
            if (ValidatePrice(line.NetPrice, line.DiscountPercent) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    public const int MaxCorrectionReasonLength = 500;

    /// <summary>Checks that need no database; the service compares with the corrected invoice.</summary>
    public static string? ValidateCorrection(CorrectionDraft draft)
    {
        if (draft.RequestId == Guid.Empty)
        {
            return "Brak identyfikatora korekty. Odśwież stronę i spróbuj ponownie.";
        }

        var reason = draft.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return "Podaj przyczynę korekty.";
        }

        if (reason.Length > MaxCorrectionReasonLength)
        {
            return $"Przyczyna korekty może mieć najwyżej {MaxCorrectionReasonLength} znaków.";
        }

        if (draft.Lines.Select(line => line.LineId).Distinct().Count() != draft.Lines.Count)
        {
            return "Każda pozycja faktury może wystąpić w korekcie tylko raz.";
        }

        foreach (var line in draft.Lines)
        {
            if (line.Quantity < 0)
            {
                return "Ilość po korekcie nie może być ujemna.";
            }

            if (ValidatePrice(line.NetPrice, line.DiscountPercent) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    private static string? ValidatePrice(decimal? netPrice, decimal discountPercent)
    {
        if (netPrice is { } price && (price < 0 || price > MaxNetPrice || decimal.Round(price, 2) != price))
        {
            return "Cena netto musi być nieujemna i mieć najwyżej dwa miejsca po przecinku.";
        }

        if (discountPercent is < 0 or > 100 || decimal.Round(discountPercent, 2) != discountPercent)
        {
            return "Rabat musi wynosić od 0 do 100% i mieć najwyżej dwa miejsca po przecinku.";
        }

        return null;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public static class InvoiceRetry
{
    public const int MaxAttempts = 10;

    /// <summary>1, 2, 4, … minutes, at most an hour.</summary>
    public static TimeSpan DelayAfter(int attempts) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Clamp(attempts, 1, 10) - 1)));
}

/// <summary>The request body for Fakturownia. The discount is already in the line value.</summary>
public static class InvoicePayload
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Build(Invoice invoice, Contractor contractor) => JsonSerializer.Serialize(
        new
        {
            invoice = new
            {
                kind = invoice.Kind == InvoiceKind.Proforma ? "proforma" : "vat",
                oid = invoice.ExternalOrderId,
                oid_unique = "yes",
                issue_date = invoice.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                sell_date = invoice.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                payment_to_kind = invoice.PaymentDays,
                payment_type = invoice.PaymentMethod,
                buyer_name = contractor.Name,
                buyer_tax_no = contractor.Nip,
                buyer_street = contractor.Address,
                buyer_email = contractor.Email,
                buyer_company = contractor.Nip is null ? "0" : "1",
                positions = invoice.Lines.Select(line => new
                {
                    name = line.ProductName,
                    tax = line.VatRate,
                    quantity = line.Quantity,
                    quantity_unit = line.Unit,
                    total_price_gross = line.GrossValue,
                }),
            },
        },
        Options);

    /// <summary>
    /// A correcting invoice as the Fakturownia API documents it: the corrected invoice's id in
    /// invoice_id and from_invoice_id, and each changed line with its state before and after.
    /// The line itself carries the difference.
    /// </summary>
    public static string BuildCorrection(Invoice correction, long correctedFakturowniaId, IReadOnlyDictionary<int, InvoiceLine> before, Contractor contractor)
    {
        static object State(string name, string tax, string unit, decimal quantity, decimal gross, string kind) =>
            new { name, tax, quantity, quantity_unit = unit, total_price_gross = gross, kind };

        return JsonSerializer.Serialize(
            new
            {
                invoice = new
                {
                    kind = "correction",
                    oid = correction.ExternalOrderId,
                    oid_unique = "yes",
                    issue_date = correction.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    invoice_id = correctedFakturowniaId,
                    from_invoice_id = correctedFakturowniaId,
                    correction_reason = correction.CorrectionReason,
                    payment_to_kind = correction.PaymentDays,
                    payment_type = correction.PaymentMethod,
                    buyer_name = contractor.Name,
                    buyer_tax_no = contractor.Nip,
                    buyer_street = contractor.Address,
                    buyer_email = contractor.Email,
                    buyer_company = contractor.Nip is null ? "0" : "1",
                    positions = correction.Lines.Select(line =>
                    {
                        var original = before[line.CorrectsLineId!.Value];
                        return new
                        {
                            name = line.ProductName,
                            tax = line.VatRate,
                            quantity = line.Quantity,
                            quantity_unit = line.Unit,
                            total_price_gross = line.GrossValue,
                            kind = "correction",
                            correction_before_attributes = State(
                                original.ProductName, original.VatRate, original.Unit, original.Quantity, original.GrossValue, "correction_before"),
                            correction_after_attributes = State(
                                original.ProductName, original.VatRate, original.Unit,
                                original.Quantity + line.Quantity, original.GrossValue + line.GrossValue, "correction_after"),
                        };
                    }),
                },
            },
            Options);
    }

    /// <summary>A line needs stock movement only when it is goods.</summary>
    public static StockDocumentDraft IssueDraftFor(InvoiceDraft draft, IEnumerable<int> goodsIds) => new()
    {
        RequestId = draft.RequestId,
        Type = StockDocumentType.WZ,
        ContractorId = draft.ContractorId,
        OrderId = draft.OrderId,
        Lines = draft.Lines
            .Where(line => goodsIds.Contains(line.ProductId))
            .Select(line => new StockDocumentLine { ProductId = line.ProductId, Quantity = line.Quantity })
            .ToList(),
    };
}
