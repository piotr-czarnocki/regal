using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Warehouse;

public sealed class StockDocumentDraft
{
    /// <summary>Generated with the draft and kept across retries, so a repeated send creates one document.</summary>
    public Guid RequestId { get; set; }

    public StockDocumentType Type { get; set; }

    public int? ContractorId { get; set; }

    public string? Notes { get; set; }

    public List<StockDocumentLine> Lines { get; set; } = [];
}

public sealed class StockDocumentLine
{
    public int ProductId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Receipts only. Required on PZ; on PW the current average is used when empty.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>Receipts only. Empty means the document's own number.</summary>
    public string? BatchNumber { get; set; }

    /// <summary>Receipts only.</summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>Issues only. Empty means first-expired-first-out.</summary>
    public int? BatchId { get; set; }
}

public static class BatchNumbers
{
    /// <summary>Treated like an SKU, so "ab-1" and "AB-1" are one batch. Null when blank.</summary>
    public static string? Normalize(string? number) =>
        string.IsNullOrWhiteSpace(number) ? null : number.Trim().ToUpperInvariant();
}

/// <summary>Rules that need no database. Messages are in Polish, ready to show to the user.</summary>
public static class StockDraftValidator
{
    public const int MaxLines = 200;
    public const int MaxNotesLength = 500;
    public const int MaxBatchNumberLength = 50;

    // Low enough that quantity × cost cannot overflow a decimal.
    public const decimal MaxQuantity = 999_999_999.999m;
    public const decimal MaxUnitCost = 9_999_999_999.9999m;

    private const string WholeUnit = "szt";

    public static string? Validate(StockDocumentDraft draft)
    {
        if (draft.RequestId == Guid.Empty)
        {
            return "Brak identyfikatora dokumentu. Odśwież stronę i spróbuj ponownie.";
        }

        if (!Enum.IsDefined(draft.Type))
        {
            return "Wybierz typ dokumentu.";
        }

        if (draft.Lines.Count == 0)
        {
            return "Dodaj co najmniej jedną pozycję.";
        }

        if (draft.Lines.Count > MaxLines)
        {
            return $"Dokument może mieć najwyżej {MaxLines} pozycji.";
        }

        var notes = draft.Notes?.Trim();
        if (notes?.Length > MaxNotesLength)
        {
            return $"Uwagi mogą mieć najwyżej {MaxNotesLength} znaków.";
        }

        if (draft.Type == StockDocumentType.RW && string.IsNullOrEmpty(notes))
        {
            return "Podaj powód rozchodu wewnętrznego w uwagach.";
        }

        return ValidateContractor(draft) ?? (draft.Type.IsReceipt() ? ValidateReceiptLines(draft) : ValidateIssueLines(draft));
    }

    public static string? ValidateQuantity(decimal quantity, string unit)
    {
        if (quantity <= 0)
        {
            return "ilość musi być większa od zera.";
        }

        if (quantity > MaxQuantity)
        {
            return $"ilość może wynosić najwyżej {MaxQuantity:N3}.";
        }

        if (decimal.Round(quantity, 3) != quantity)
        {
            return "ilość może mieć najwyżej trzy miejsca po przecinku.";
        }

        return string.Equals(unit.Trim(), WholeUnit, StringComparison.OrdinalIgnoreCase) && decimal.Truncate(quantity) != quantity
            ? "dla jednostki „szt” ilość musi być liczbą całkowitą."
            : null;
    }

    public static string? ValidateExpiry(DateOnly? expiryDate, bool hasExpiry, DateOnly documentDate)
    {
        if (expiryDate is null)
        {
            return hasExpiry ? "podaj datę ważności." : null;
        }

        return expiryDate < documentDate ? "data ważności nie może być wcześniejsza niż data dokumentu." : null;
    }

    private static string? ValidateContractor(StockDocumentDraft draft) => draft.Type switch
    {
        StockDocumentType.PZ when draft.ContractorId is null => "Wybierz dostawcę.",
        StockDocumentType.PW or StockDocumentType.RW when draft.ContractorId is not null => $"Dokument {draft.Type} nie może mieć kontrahenta.",
        _ => null,
    };

    private static string? ValidateReceiptLines(StockDocumentDraft draft)
    {
        foreach (var line in draft.Lines)
        {
            if (draft.Type == StockDocumentType.PZ && line.UnitCost is null)
            {
                return "Podaj cenę zakupu każdej pozycji.";
            }

            if (line.UnitCost is { } cost && (cost <= 0 || cost > MaxUnitCost || decimal.Round(cost, 4) != cost))
            {
                return "Koszt musi być większy od zera i mieć najwyżej cztery miejsca po przecinku.";
            }

            if (BatchNumbers.Normalize(line.BatchNumber)?.Length > MaxBatchNumberLength)
            {
                return $"Numer partii może mieć najwyżej {MaxBatchNumberLength} znaków.";
            }
        }

        var distinct = draft.Lines.Select(line => (line.ProductId, Number: BatchNumbers.Normalize(line.BatchNumber))).Distinct().Count();
        return distinct == draft.Lines.Count ? null : "Ten sam produkt może wystąpić tylko raz w jednej partii.";
    }

    private static string? ValidateIssueLines(StockDocumentDraft draft) =>
        draft.Lines.Select(line => line.ProductId).Distinct().Count() == draft.Lines.Count
            ? null
            : "Każdy produkt może wystąpić na dokumencie tylko raz.";
}
