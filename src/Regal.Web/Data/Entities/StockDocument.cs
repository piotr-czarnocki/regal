namespace Regal.Web.Data.Entities;

public enum StockDocumentType
{
    PZ,
    PW,
    WZ,
    RW,
}

public static class StockDocumentTypeExtensions
{
    public static bool IsReceipt(this StockDocumentType type) => type is StockDocumentType.PZ or StockDocumentType.PW;
}

/// <summary>
/// Never changes once created: mistakes are fixed with a reversing document.
/// The application's database role has no UPDATE or DELETE here.
/// </summary>
public sealed class StockDocument
{
    public int Id { get; set; }

    public StockDocumentType Type { get; set; }

    public required string Number { get; set; }

    public int? ContractorId { get; set; }

    public Contractor? Contractor { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Set on a correction: the document it reverses.</summary>
    public int? ReversesDocumentId { get; set; }

    public StockDocument? ReversesDocument { get; set; }

    /// <summary>UTC. The moment of approval.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The date of approval in Europe/Warsaw.</summary>
    public DateOnly DocumentDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Idempotency key: the same draft sent twice creates one document.</summary>
    public Guid RequestId { get; set; }

    /// <summary>Set on the RW and PW that post the differences of a stocktake.</summary>
    public int? StocktakeId { get; set; }

    public Stocktake? Stocktake { get; set; }

    public List<StockMovement> Movements { get; set; } = [];
}
