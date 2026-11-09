namespace Regal.Web.Data.Entities;

/// <summary>Never changes once created: the application's database role has no UPDATE or DELETE here.</summary>
public sealed class Batch
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Unique per product: typed by the user or taken from the receipt's number.</summary>
    public required string Number { get; set; }

    /// <summary>Required for products with HasExpiry, optional for others.</summary>
    public DateOnly? ExpiryDate { get; set; }
}
