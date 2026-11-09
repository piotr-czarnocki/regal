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

    public List<InvoiceDraftLine> Lines { get; set; } = [];
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
            if (line.NetPrice is { } price && (price < 0 || price > MaxNetPrice || decimal.Round(price, 2) != price))
            {
                return "Cena netto musi być nieujemna i mieć najwyżej dwa miejsca po przecinku.";
            }

            if (line.DiscountPercent is < 0 or > 100 || decimal.Round(line.DiscountPercent, 2) != line.DiscountPercent)
            {
                return "Rabat musi wynosić od 0 do 100% i mieć najwyżej dwa miejsca po przecinku.";
            }
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
                kind = "vat",
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

    /// <summary>A line needs stock movement only when it is goods.</summary>
    public static StockDocumentDraft IssueDraftFor(InvoiceDraft draft, IEnumerable<int> goodsIds) => new()
    {
        RequestId = draft.RequestId,
        Type = StockDocumentType.WZ,
        ContractorId = draft.ContractorId,
        Lines = draft.Lines
            .Where(line => goodsIds.Contains(line.ProductId))
            .Select(line => new StockDocumentLine { ProductId = line.ProductId, Quantity = line.Quantity })
            .ToList(),
    };
}
