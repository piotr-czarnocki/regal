using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.UnitTests;

public class FakturowniaClientTests
{
    private const string Token = "sekretny-token-123";

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(request);
        }
    }

    private static (FakturowniaClient Client, StubHandler Handler) Create(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, string? domain = "firma", string? token = Token)
    {
        var handler = new StubHandler(respond);
        var options = Options.Create(new FakturowniaOptions { Domain = domain, ApiToken = token });
        return (new FakturowniaClient(new HttpClient(handler), options), handler);
    }

    private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    [Fact]
    public async Task Token_goes_in_the_authorization_header_and_never_in_the_url()
    {
        var (client, handler) = Create(_ => Reply(HttpStatusCode.OK, "[]"));

        await client.FindByOidAsync("regal-1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(Token, request.Headers.Authorization.Parameter);
        Assert.Equal("https://firma.fakturownia.pl/invoices.json?oid=regal-1", request.RequestUri!.ToString());
        Assert.DoesNotContain(Token, request.RequestUri.ToString());
    }

    [Fact]
    public async Task Created_invoice_is_read_with_its_number_and_pdf_link()
    {
        var (client, handler) = Create(_ => Reply(HttpStatusCode.Created, """{"id":42,"number":"FV 7/2026","token":"abc","price_gross":"123.45"}"""));

        var remote = await client.CreateAsync("""{"invoice":{}}""", CancellationToken.None);

        Assert.Equal(new RemoteInvoice(42, "FV 7/2026", "https://firma.fakturownia.pl/invoice/abc.pdf", 123.45m), remote);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
    }

    [Theory]
    [InlineData("""{"id":5,"status":"paid","paid":"123,45","paid_date":"2026-11-09"}""", "paid", 123.45, "2026-11-09")]
    [InlineData("""{"id":5,"status":"partial","paid":"10.00","paid_date":""}""", "partial", 10.00, null)]
    [InlineData("""{"id":5,"status":"issued","paid":0}""", "issued", 0, null)]
    public async Task Payment_is_read_with_a_decimal_comma_or_point(string body, string status, double paid, string? date)
    {
        var (client, handler) = Create(_ => Reply(HttpStatusCode.OK, body));

        var payment = await client.GetPaymentAsync(5, CancellationToken.None);

        Assert.Equal(new RemotePayment(status, (decimal)paid, date is null ? null : DateOnly.Parse(date)), payment);
        Assert.Equal("https://firma.fakturownia.pl/invoices/5.json", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Email_is_requested_with_the_pdf_and_an_escaped_address_and_no_token_in_the_url()
    {
        var (client, handler) = Create(_ => Reply(HttpStatusCode.OK, "{}"));

        await client.SendByEmailAsync(7, "jan+faktury@example.com", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://firma.fakturownia.pl/invoices/7/send_by_email.json?email_to=jan%2Bfaktury%40example.com&email_pdf=true", request.RequestUri!.AbsoluteUri);
        Assert.DoesNotContain(Token, request.RequestUri.ToString());
        Assert.Equal(Token, request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task A_refused_email_is_reported_as_rejected()
    {
        var (client, _) = Create(_ => Reply(HttpStatusCode.UnprocessableEntity, """{"message":"brak adresu"}"""));

        await Assert.ThrowsAsync<FakturowniaRejectedException>(() => client.SendByEmailAsync(7, "a@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_accepts_only_an_exact_oid()
    {
        var (client, _) = Create(_ => Reply(HttpStatusCode.OK, """[{"id":1,"oid":"regal-10"},{"id":2,"oid":"regal-1"}]"""));

        Assert.Equal(2, (await client.FindByOidAsync("regal-1", CancellationToken.None))!.Id);
        Assert.Null(await client.FindByOidAsync("regal-2", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Temporary_statuses_are_retryable(HttpStatusCode status)
    {
        var (client, _) = Create(_ => Reply(status, "{}"));

        await Assert.ThrowsAsync<FakturowniaUnavailableException>(() => client.CreateAsync("{}", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Refusals_are_not_retryable_and_keep_the_reason(HttpStatusCode status)
    {
        var (client, _) = Create(_ => Reply(status, """{"code":"error","message":{"buyer_tax_no":["is invalid"]}}"""));

        var exception = await Assert.ThrowsAsync<FakturowniaRejectedException>(() => client.CreateAsync("{}", CancellationToken.None));

        Assert.Contains("buyer_tax_no", exception.Message);
        Assert.DoesNotContain(Token, exception.Message);
    }

    [Fact]
    public async Task Network_failure_and_timeout_are_retryable()
    {
        var (failing, _) = Create(_ => throw new HttpRequestException("connection refused"));
        var (slow, _) = Create(_ => throw new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<FakturowniaUnavailableException>(() => failing.FindByOidAsync("x", CancellationToken.None));
        await Assert.ThrowsAsync<FakturowniaUnavailableException>(() => slow.FindByOidAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task Unreadable_success_is_retryable_because_the_invoice_may_exist()
    {
        var (client, _) = Create(_ => Reply(HttpStatusCode.OK, "<html>maintenance</html>"));

        await Assert.ThrowsAsync<FakturowniaUnavailableException>(() => client.CreateAsync("{}", CancellationToken.None));
    }

    [Theory]
    [InlineData(null, Token)]
    [InlineData("firma", null)]
    [InlineData("firma.evil.com/x?", Token)]
    public async Task Without_a_valid_configuration_nothing_is_sent(string? domain, string? token)
    {
        var (client, handler) = Create(_ => Reply(HttpStatusCode.OK, "[]"), domain, token);

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<FakturowniaUnavailableException>(() => client.FindByOidAsync("x", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }
}
