namespace Regal.Web.Data.Entities;

public enum InvoiceKind
{
    Vat,

    /// <summary>Not a sale: no WZ, no stock change, left out of the analyses.</summary>
    Proforma,

    /// <summary>Corrects a VAT invoice; its lines hold differences (SPEC 5.12).</summary>
    Correction,
}

/// <summary>As last read from Fakturownia; Unknown until the first check.</summary>
public enum InvoicePaymentStatus
{
    Unknown,
    Unpaid,
    Partial,
    Paid,
}

public enum InvoiceStatus
{
    ToSend,
    Sent,
    Failed,
    Cancelled,
}

/// <summary>
/// The application's database role may change only the status and what Fakturownia assigned.
/// Everything else is fixed when the invoice is issued.
/// </summary>
public sealed class Invoice
{
    public int Id { get; set; }

    public InvoiceKind Kind { get; set; }

    public InvoiceStatus Status { get; set; }

    /// <summary>The "oid" sent to Fakturownia: finding it there tells whether a send already succeeded.</summary>
    public required string ExternalOrderId { get; set; }

    public long? FakturowniaId { get; set; }

    public string? Number { get; set; }

    public string? PdfUrl { get; set; }

    public int ContractorId { get; set; }

    public Contractor? Contractor { get; set; }

    /// <summary>Empty when the invoice has only services.</summary>
    public int? StockDocumentId { get; set; }

    public StockDocument? StockDocument { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public decimal NetTotal { get; set; }

    public decimal VatTotal { get; set; }

    public decimal GrossTotal { get; set; }

    /// <summary>The gross total Fakturownia calculated, kept to reveal rounding differences.</summary>
    public decimal? RemoteGrossTotal { get; set; }

    public int PaymentDays { get; set; }

    /// <summary>One of the codes in InvoicePaymentMethods.</summary>
    public required string PaymentMethod { get; set; }

    /// <summary>The date of issue in Europe/Warsaw.</summary>
    public DateOnly IssueDate { get; set; }

    /// <summary>UTC.</summary>
    public DateTime IssuedAt { get; set; }

    public InvoicePaymentStatus PaymentStatus { get; set; }

    public decimal? PaidAmount { get; set; }

    public DateOnly? PaidDate { get; set; }

    /// <summary>UTC. When the payment state was last read from Fakturownia.</summary>
    public DateTime? PaymentCheckedAt { get; set; }

    /// <summary>UTC. When Fakturownia was last asked to email the invoice.</summary>
    public DateTime? EmailedAt { get; set; }

    public string? EmailedTo { get; set; }

    /// <summary>On a correction: the VAT invoice it corrects. Unique, so an invoice is corrected once.</summary>
    public int? CorrectsInvoiceId { get; set; }

    public Invoice? CorrectsInvoice { get; set; }

    public string? CorrectionReason { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];
}

/// <summary>Never changes once created: the application's database role has no UPDATE or DELETE here.</summary>
public sealed class InvoiceLine
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>A copy: renaming the product must not change an issued invoice.</summary>
    public required string ProductName { get; set; }

    public required string Unit { get; set; }

    public decimal Quantity { get; set; }

    public decimal NetPrice { get; set; }

    public decimal DiscountPercent { get; set; }

    public required string VatRate { get; set; }

    public decimal NetValue { get; set; }

    public decimal VatValue { get; set; }

    public decimal GrossValue { get; set; }

    /// <summary>Per unit, from the WZ movements; zero for services.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// On a correction: the line it corrects. Quantity and the values are then differences (after
    /// minus before, quantity never above zero); NetPrice and DiscountPercent are the values after.
    /// </summary>
    public int? CorrectsLineId { get; set; }

    public InvoiceLine? CorrectsLine { get; set; }
}

/// <summary>Work to do after a transaction commits. Written in that transaction, so it cannot be lost.</summary>
public sealed class OutboxMessage
{
    public long Id { get; set; }

    public required string Type { get; set; }

    public int InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    /// <summary>The complete request body, fixed at issue, so later catalog edits do not change a retry.</summary>
    public required string Payload { get; set; }

    public int Attempts { get; set; }

    /// <summary>UTC. Empty means nothing is scheduled: done, cancelled, or waiting for a person.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>UTC. While in the future, someone is working on this message.</summary>
    public DateTime? LockedUntil { get; set; }

    public string? LastError { get; set; }
}
