using System.Collections.Concurrent;
using Regal.Web.Integrations.VatRegistry;

namespace Regal.TestHost;

/// <summary>Stands in for the Ministry of Finance's VAT register, so no test ever calls it.</summary>
public sealed class FakeVatRegistry : IVatRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredCompany> _companies = new();

    public ConcurrentQueue<Exception> Errors { get; } = new();

    public ConcurrentQueue<(string Nip, DateOnly Date)> Requests { get; } = new();

    public void Add(string nip, RegisteredCompany company) => _companies[nip] = company;

    public Task<RegisteredCompany?> FindByNipAsync(string nip, DateOnly date, CancellationToken cancellationToken)
    {
        Requests.Enqueue((nip, date));
        if (Errors.TryDequeue(out var error))
        {
            throw error;
        }

        return Task.FromResult(_companies.GetValueOrDefault(nip));
    }
}
