namespace Regal.Web.Integrations.Fakturownia;

public sealed record RemoteInvoice(long Id, string? Number, string? PdfUrl, decimal? GrossTotal);

/// <summary>What Fakturownia knows about payment: status "issued", "sent", "paid", "partial" or "rejected".</summary>
public sealed record RemotePayment(string? Status, decimal? Paid, DateOnly? PaidDate);

public interface IFakturowniaClient
{
    /// <summary>False until a domain and token are configured; nothing is sent until then.</summary>
    bool IsConfigured { get; }

    /// <summary>The invoice created with this oid, or null when there is none.</summary>
    Task<RemoteInvoice?> FindByOidAsync(string oid, CancellationToken cancellationToken);

    /// <param name="requestJson">The complete request body.</param>
    Task<RemoteInvoice> CreateAsync(string requestJson, CancellationToken cancellationToken);

    /// <summary>The payment state of an invoice created there (GET invoices/{id}.json).</summary>
    Task<RemotePayment> GetPaymentAsync(long fakturowniaId, CancellationToken cancellationToken);

    /// <summary>Fakturownia emails the invoice with its PDF to this address (POST invoices/{id}/send_by_email.json).</summary>
    Task SendByEmailAsync(long fakturowniaId, string email, CancellationToken cancellationToken);
}

/// <summary>The outcome is unknown or temporary (network, timeout, 429, 5xx): trying again later is safe.</summary>
public sealed class FakturowniaUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Fakturownia understood the request and refused it: repeating it would give the same answer.</summary>
public sealed class FakturowniaRejectedException(string message) : Exception(message);
