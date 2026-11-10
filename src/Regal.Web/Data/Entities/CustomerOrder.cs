namespace Regal.Web.Data.Entities;

public enum CustomerOrderStatus
{
    /// <summary>Reserves its goods.</summary>
    Open,

    /// <summary>An invoice was issued from it; the reservation is used up.</summary>
    Completed,

    /// <summary>The reservation was released without a sale.</summary>
    Cancelled,
}

/// <summary>
/// A customer order (ZK). Its lines never change; the application's database role may update only
/// the status and the invoice, and may delete nothing.
/// </summary>
public sealed class CustomerOrder
{
    public int Id { get; set; }

    public required string Number { get; set; }

    public CustomerOrderStatus Status { get; set; }

    public int ContractorId { get; set; }

    public Contractor? Contractor { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Europe/Warsaw.</summary>
    public DateOnly OrderDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Idempotency key: the same form sent twice creates one order.</summary>
    public Guid RequestId { get; set; }

    /// <summary>The invoice that completed the order.</summary>
    public int? InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public List<CustomerOrderLine> Lines { get; set; } = [];
}

public sealed class CustomerOrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public CustomerOrder? Order { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public decimal Quantity { get; set; }
}

/// <summary>The last ZK number used in a year (Europe/Warsaw).</summary>
public sealed class OrderCounter
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}
