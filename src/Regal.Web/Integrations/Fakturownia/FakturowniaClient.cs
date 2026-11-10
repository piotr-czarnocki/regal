using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Regal.Web.Integrations.Fakturownia;

public sealed class FakturowniaOptions
{
    public const string Section = "Fakturownia";

    /// <summary>The account's subdomain, as in {Domain}.fakturownia.pl.</summary>
    public string? Domain { get; set; }

    public string? ApiToken { get; set; }
}

public sealed class FakturowniaClient(HttpClient http, IOptions<FakturowniaOptions> options) : IFakturowniaClient
{
    private const int MaxErrorLength = 500;

    private readonly string? _domain = options.Value.Domain?.Trim();
    private readonly string? _token = options.Value.ApiToken?.Trim();

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_token)
        && !string.IsNullOrEmpty(_domain)
        && _domain.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public async Task<RemoteInvoice?> FindByOidAsync(string oid, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, $"invoices.json?oid={Uri.EscapeDataString(oid)}");
        var body = await SendAsync(request, cancellationToken);

        return ParseOrThrow(body, root =>
        {
            // Compared here too: a filter the server ignored must not look like a match.
            foreach (var invoice in root.EnumerateArray())
            {
                if (invoice.TryGetProperty("oid", out var found) && found.ValueKind == JsonValueKind.String && found.GetString() == oid)
                {
                    return ToRemote(invoice);
                }
            }

            return null;
        });
    }

    public async Task<RemoteInvoice> CreateAsync(string requestJson, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Post, "invoices.json");
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var body = await SendAsync(request, cancellationToken);

        return ParseOrThrow(body, ToRemote)!;
    }

    public async Task<RemotePayment> GetPaymentAsync(long fakturowniaId, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, string.Create(CultureInfo.InvariantCulture, $"invoices/{fakturowniaId}.json"));
        var body = await SendAsync(request, cancellationToken);

        return ParseOrThrow(body, invoice => new RemotePayment(
            invoice.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String ? status.GetString() : null,
            ReadAmount(invoice, "paid"),
            invoice.TryGetProperty("paid_date", out var date) && date.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var paidDate)
                ? paidDate
                : null));
    }

    public async Task SendByEmailAsync(long fakturowniaId, string email, CancellationToken cancellationToken)
    {
        // Parameters as documented; the address is escaped, the token still goes only in the header.
        using var request = NewRequest(
            HttpMethod.Post,
            string.Create(CultureInfo.InvariantCulture, $"invoices/{fakturowniaId}/send_by_email.json?email_to={Uri.EscapeDataString(email)}&email_pdf=true"));
        await SendAsync(request, cancellationToken);
    }

    /// <summary>The documentation shows "paid": "0,00", so a decimal comma is accepted as well as a point.</summary>
    private static decimal? ReadAmount(JsonElement invoice, string name)
    {
        if (!invoice.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                value.GetString()?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        if (!IsConfigured)
        {
            throw new FakturowniaUnavailableException("Integracja z Fakturownią nie jest skonfigurowana.");
        }

        var request = new HttpRequestMessage(method, new Uri($"https://{_domain}.fakturownia.pl/{path}"));

        // In a header, so the token never appears in a URL or a log of URLs.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new FakturowniaUnavailableException("Brak połączenia z Fakturownią.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FakturowniaUnavailableException("Fakturownia nie odpowiedziała na czas.", exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            var status = (int)response.StatusCode;
            var details = body.Length > MaxErrorLength ? body[..MaxErrorLength] : body;
            var message = string.Create(CultureInfo.InvariantCulture, $"HTTP {status}: {details}");

            if (status >= 500 || response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout)
            {
                throw new FakturowniaUnavailableException(message);
            }

            throw new FakturowniaRejectedException(message);
        }
    }

    private static T ParseOrThrow<T>(string body, Func<JsonElement, T> parse)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return parse(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // The request may have worked. The next attempt looks the invoice up by its oid first.
            throw new FakturowniaUnavailableException("Niezrozumiała odpowiedź Fakturowni.", exception);
        }
    }

    private RemoteInvoice ToRemote(JsonElement invoice)
    {
        var number = invoice.TryGetProperty("number", out var numberValue) ? numberValue.GetString() : null;
        var token = invoice.TryGetProperty("token", out var tokenValue) ? tokenValue.GetString() : null;
        var pdfUrl = string.IsNullOrEmpty(token) ? null : $"https://{_domain}.fakturownia.pl/invoice/{token}.pdf";

        return new RemoteInvoice(invoice.GetProperty("id").GetInt64(), number, pdfUrl, ReadDecimal(invoice, "price_gross"));
    }

    private static decimal? ReadDecimal(JsonElement invoice, string name)
    {
        if (!invoice.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
