namespace Regal.Web.Data.Entities;

/// <summary>Never changes once created: the application's database role has no UPDATE or DELETE here.</summary>
public sealed class StockMovement
{
    public long Id { get; set; }

    public int DocumentId { get; set; }

    public StockDocument? Document { get; set; }

    public int BatchId { get; set; }

    public Batch? Batch { get; set; }

    /// <summary>Positive adds stock, negative removes it.</summary>
    public decimal QuantityChange { get; set; }

    /// <summary>Purchase or production cost on a receipt; the average cost at the moment of an issue.</summary>
    public decimal UnitCost { get; set; }
}
