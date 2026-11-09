namespace Regal.Web.Data.Entities;

public enum ProductKind
{
    Goods,
    Service,
}

public sealed class Product
{
    public int Id { get; set; }

    public required string Sku { get; set; }

    /// <summary>Always set for goods, always null for services.</summary>
    public string? Ean { get; set; }

    public required string Name { get; set; }

    public int CategoryId { get; set; }

    public Category? Category { get; set; }

    public required string Unit { get; set; }

    public ProductKind Kind { get; set; }

    public decimal NetPrice { get; set; }

    /// <summary>One of the codes in VatRates.</summary>
    public required string VatRate { get; set; }

    public bool HasExpiry { get; set; }

    /// <summary>Maintained by goods receipts, never edited directly.</summary>
    public decimal AverageCost { get; set; }

    public bool IsActive { get; set; } = true;
}
