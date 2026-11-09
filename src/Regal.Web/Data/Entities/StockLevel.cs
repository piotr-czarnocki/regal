namespace Regal.Web.Data.Entities;

/// <summary>Quantity of one batch in one warehouse.</summary>
public sealed class StockLevel
{
    public int WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    public int BatchId { get; set; }

    public Batch? Batch { get; set; }

    public decimal Quantity { get; set; }
}
