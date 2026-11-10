using System.Globalization;
using System.Text.Json;

namespace Regal.Web.Integrations.VatRegistry;

/// <summary>A company as the Ministry of Finance's VAT register ("biała lista") knows it.</summary>
public sealed record RegisteredCompany(string Name, string? Address, string? VatStatus, string? Regon);

/// <summary>The register could not answer: network, timeout, or an error on its side.</summary>
public sealed class VatRegistryUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The register refused the request, e.g. an invalid NIP (code WL-xxx).</summary>
public sealed class VatRegistryRejectedException(string message) : Exception(message);

public interface IVatRegistry
{
    /// <summary>The company with this NIP on that day, or null when the register has no such subject.</summary>
    Task<RegisteredCompany?> FindByNipAsync(string nip, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>
/// The public API of the VAT register (https://wl-api.mf.gov.pl, no key):
/// GET /api/search/nip/{nip}?date=yyyy-MM-dd. Called only when a person asks, never in the background,
/// because the API documents no request limits.
/// </summary>
public sealed class VatRegistryClient(HttpClient http) : IVatRegistry
{
    public static readonly Uri BaseAddress = new("https://wl-api.mf.gov.pl/");

    public async Task<RegisteredCompany?> FindByNipAsync(string nip, DateOnly date, CancellationToken cancellationToken)
    {
        // The NIP is checked by the caller; digits only, so it is safe in the path.
        if (nip.Length != 10 || !nip.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("A normalized 10-digit NIP is expected.", nameof(nip));
        }

        var path = string.Create(CultureInfo.InvariantCulture, $"api/search/nip/{nip}?date={date:yyyy-MM-dd}");
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri(BaseAddress, path), cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new VatRegistryUnavailableException("Brak połączenia z białą listą podatników VAT.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VatRegistryUnavailableException("Biała lista podatników VAT nie odpowiedziała na czas.", exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                throw new VatRegistryRejectedException(ReadError(body) ?? $"HTTP {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new VatRegistryUnavailableException($"Biała lista odpowiedziała błędem HTTP {(int)response.StatusCode}.");
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                if (!document.RootElement.TryGetProperty("result", out var result)
                    || !result.TryGetProperty("subject", out var subject)
                    || subject.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return new RegisteredCompany(
                    subject.GetProperty("name").GetString()!.Trim(),
                    Text(subject, "workingAddress") ?? Text(subject, "residenceAddress"),
                    Text(subject, "statusVat"),
                    Text(subject, "regon"));
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new VatRegistryUnavailableException("Niezrozumiała odpowiedź białej listy.", exception);
            }
        }
    }

    private static string? Text(JsonElement subject, string name) =>
        subject.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static string? ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var code = document.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = document.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            return message is null ? null : code is null ? message : $"{message} ({code})";
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
