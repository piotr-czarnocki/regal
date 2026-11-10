using System.Globalization;
using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Orders;

public sealed class OrderDraft
{
    /// <summary>Generated with the form and kept across retries, so a repeated send creates one order.</summary>
    public Guid RequestId { get; set; }

    public int? ContractorId { get; set; }

    public string? Notes { get; set; }

    public List<OrderDraftLine> Lines { get; set; } = [];
}

public sealed class OrderDraftLine
{
    public int ProductId { get; set; }

    public decimal Quantity { get; set; }
}

/// <summary>Checks that need no database; the service checks products, stock and reservations.</summary>
public static class OrderRules
{
    public const int MaxLines = 200;
    public const int MaxNotesLength = 500;

    public static string? Validate(OrderDraft draft)
    {
        if (draft.RequestId == Guid.Empty)
        {
            return "Brak identyfikatora zamówienia. Odśwież stronę i spróbuj ponownie.";
        }

        if (draft.ContractorId is null)
        {
            return "Wybierz nabywcę.";
        }

        if (draft.Lines.Count == 0)
        {
            return "Dodaj co najmniej jedną pozycję.";
        }

        if (draft.Lines.Count > MaxLines)
        {
            return $"Zamówienie może mieć najwyżej {MaxLines} pozycji.";
        }

        if (draft.Lines.GroupBy(line => line.ProductId).Any(group => group.Count() > 1))
        {
            return "Każdy produkt może wystąpić w zamówieniu tylko raz.";
        }

        if (draft.Notes?.Trim().Length > MaxNotesLength)
        {
            return $"Uwagi mogą mieć najwyżej {MaxNotesLength} znaków.";
        }

        return null;
    }

    public static string Number(int number, int year) => string.Create(CultureInfo.InvariantCulture, $"ZK/{number}/{year}");

    public static string StatusLabel(CustomerOrderStatus status) => status switch
    {
        CustomerOrderStatus.Open => "otwarte",
        CustomerOrderStatus.Completed => "zrealizowane",
        CustomerOrderStatus.Cancelled => "anulowane",
        _ => status.ToString(),
    };
}
