using System.Collections.Concurrent;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.TestHost;

/// <summary>Stands in for Fakturownia. Like the real one, it refuses a second invoice with the same oid.</summary>
public sealed class FakeFakturownia : IFakturowniaClient
{
    private readonly ConcurrentDictionary<string, RemoteInvoice> _invoices = new();
    private readonly ConcurrentDictionary<string, int> _creates = new();
    private long _lastId;
    private int _createCalls;
    private int _loseNextCreateReply;
    private int _hideNextFind;

    public bool IsConfigured { get; set; } = true;

    public ConcurrentQueue<Exception> FindErrors { get; } = new();

    public ConcurrentQueue<Exception> CreateErrors { get; } = new();

    public TimeSpan CreateDelay { get; set; }

    public ConcurrentBag<string> Payloads { get; } = [];

    public int TotalCreates => _creates.Values.Sum();

    /// <summary>Every create request, refused duplicates included.</summary>
    public int CreateCalls => Volatile.Read(ref _createCalls);

    public int CreatesFor(string oid) => _creates.GetValueOrDefault(oid);

    public bool Has(string oid) => _invoices.ContainsKey(oid);

    /// <summary>The next create succeeds there, but its reply never arrives.</summary>
    public void LoseNextCreateReply() => Interlocked.Exchange(ref _loseNextCreateReply, 1);

    /// <summary>The next lookup misses an invoice that exists, as if it was still being saved.</summary>
    public void HideNextFind() => Interlocked.Exchange(ref _hideNextFind, 1);

    /// <summary>An invoice created there by an attempt this test did not see.</summary>
    public void Add(string oid) => _invoices[oid] = NewRemote();

    public Task<RemoteInvoice?> FindByOidAsync(string oid, CancellationToken cancellationToken)
    {
        if (FindErrors.TryDequeue(out var error))
        {
            throw error;
        }

        if (Interlocked.Exchange(ref _hideNextFind, 0) == 1)
        {
            return Task.FromResult<RemoteInvoice?>(null);
        }

        return Task.FromResult(_invoices.GetValueOrDefault(oid));
    }

    public async Task<RemoteInvoice> CreateAsync(string requestJson, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCalls);
        if (CreateErrors.TryDequeue(out var error))
        {
            throw error;
        }

        if (CreateDelay > TimeSpan.Zero)
        {
            await Task.Delay(CreateDelay, cancellationToken);
        }

        var oid = System.Text.Json.JsonDocument.Parse(requestJson).RootElement.GetProperty("invoice").GetProperty("oid").GetString()!;
        var remote = NewRemote();
        if (!_invoices.TryAdd(oid, remote))
        {
            throw new FakturowniaRejectedException("HTTP 422: {\"oid\":[\"has already been taken\"]}");
        }

        _creates.AddOrUpdate(oid, 1, (_, count) => count + 1);
        Payloads.Add(requestJson);

        if (Interlocked.Exchange(ref _loseNextCreateReply, 0) == 1)
        {
            throw new FakturowniaUnavailableException("Fakturownia nie odpowiedziała na czas.");
        }

        return remote;
    }

    private readonly ConcurrentDictionary<long, RemotePayment> _payments = new();

    public ConcurrentQueue<Exception> PaymentErrors { get; } = new();

    public ConcurrentQueue<Exception> EmailErrors { get; } = new();

    /// <summary>Every email request that succeeded, in order.</summary>
    public ConcurrentQueue<(long FakturowniaId, string Email)> Emails { get; } = new();

    public int PaymentCalls => Volatile.Read(ref _paymentCalls);

    private int _paymentCalls;

    /// <summary>What the next lookups of this invoice report; unknown invoices are "issued" and unpaid.</summary>
    public void SetPayment(long fakturowniaId, string status, decimal paid, DateOnly? paidDate = null) =>
        _payments[fakturowniaId] = new RemotePayment(status, paid, paidDate);

    public Task<RemotePayment> GetPaymentAsync(long fakturowniaId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _paymentCalls);
        if (PaymentErrors.TryDequeue(out var error))
        {
            throw error;
        }

        return Task.FromResult(_payments.GetValueOrDefault(fakturowniaId, new RemotePayment("issued", 0m, null)));
    }

    public Task SendByEmailAsync(long fakturowniaId, string email, CancellationToken cancellationToken)
    {
        if (EmailErrors.TryDequeue(out var error))
        {
            throw error;
        }

        Emails.Enqueue((fakturowniaId, email));
        return Task.CompletedTask;
    }

    private RemoteInvoice NewRemote()
    {
        var id = Interlocked.Increment(ref _lastId);
        return new RemoteInvoice(id, $"FV {id}/2026", $"https://firma.fakturownia.pl/invoice/t{id}.pdf", null);
    }
}
