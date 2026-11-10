using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Contractors;
using Regal.Web.Integrations.VatRegistry;

namespace Regal.UnitTests;

/// <summary>The VAT register client and the lookup built on it (SPEC 5.15); nothing leaves the test.</summary>
public class VatRegistryTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no network");
    }

    private sealed class StubRegistry(Func<string, RegisteredCompany?> find) : IVatRegistry
    {
        public List<(string Nip, DateOnly Date)> Calls { get; } = [];

        public Task<RegisteredCompany?> FindByNipAsync(string nip, DateOnly date, CancellationToken cancellationToken)
        {
            Calls.Add((nip, date));
            return Task.FromResult(find(nip));
        }
    }

    private sealed class AnyUser : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => Task.FromResult(1);
    }

    private const string Company = """
        {"result":{"subject":{"name":" PIEKARNIA POD LIPĄ SP. Z O.O. ","nip":"1234563218","statusVat":"Czynny","regon":"123456785",
        "residenceAddress":null,"workingAddress":"UL. LIPOWA 1, 00-001 WARSZAWA"},"requestId":"abc"}}
        """;

    [Fact]
    public async Task A_company_is_read_by_nip_and_date()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Company);
        var client = new VatRegistryClient(new HttpClient(handler));

        var company = await client.FindByNipAsync("1234563218", new DateOnly(2026, 11, 10), CancellationToken.None);

        Assert.Equal(new RegisteredCompany("PIEKARNIA POD LIPĄ SP. Z O.O.", "UL. LIPOWA 1, 00-001 WARSZAWA", "Czynny", "123456785"), company);
        Assert.Equal("https://wl-api.mf.gov.pl/api/search/nip/1234563218?date=2026-11-10", Assert.Single(handler.Requests).ToString());
    }

    [Fact]
    public async Task A_person_s_business_address_comes_from_the_residence_field_when_the_other_is_empty()
    {
        var body = """{"result":{"subject":{"name":"Jan Kowalski","workingAddress":null,"residenceAddress":"UL. POLNA 2, 00-002 WARSZAWA"}}}""";
        var client = new VatRegistryClient(new HttpClient(new StubHandler(HttpStatusCode.OK, body)));

        var company = await client.FindByNipAsync("1234563218", new DateOnly(2026, 11, 10), CancellationToken.None);

        Assert.Equal("UL. POLNA 2, 00-002 WARSZAWA", company!.Address);
    }

    [Fact]
    public async Task An_unknown_nip_gives_null()
    {
        var client = new VatRegistryClient(new HttpClient(new StubHandler(HttpStatusCode.OK, """{"result":{"subject":null,"requestId":"x"}}""")));

        Assert.Null(await client.FindByNipAsync("1234563218", new DateOnly(2026, 11, 10), CancellationToken.None));
    }

    [Fact]
    public async Task A_refusal_carries_the_register_s_message_and_code()
    {
        var client = new VatRegistryClient(new HttpClient(new StubHandler(HttpStatusCode.BadRequest, """{"message":"Nieprawidłowy NIP.","code":"WL-113"}""")));

        var exception = await Assert.ThrowsAsync<VatRegistryRejectedException>(() =>
            client.FindByNipAsync("1234563218", new DateOnly(2026, 11, 10), CancellationToken.None));

        Assert.Equal("Nieprawidłowy NIP. (WL-113)", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "<html>")]
    public async Task Server_errors_and_unreadable_answers_mean_unavailable(HttpStatusCode status, string body)
    {
        var client = new VatRegistryClient(new HttpClient(new StubHandler(status, body)));

        await Assert.ThrowsAsync<VatRegistryUnavailableException>(() =>
            client.FindByNipAsync("1234563218", new DateOnly(2026, 11, 10), CancellationToken.None));
    }

    [Fact]
    public async Task No_network_means_unavailable()
    {
        var client = new VatRegistryClient(new HttpClient(new ThrowingHandler()));

        await Assert.ThrowsAsync<VatRegistryUnavailableException>(() =>
            client.FindByNipAsync("1234563218", new DateOnly(2026, 11, 10), CancellationToken.None));
    }

    [Fact]
    public async Task Only_a_normalized_nip_goes_into_the_url()
    {
        var client = new VatRegistryClient(new HttpClient(new StubHandler(HttpStatusCode.OK, Company)));

        await Assert.ThrowsAsync<ArgumentException>(() => client.FindByNipAsync("123/../x", new DateOnly(2026, 11, 10), CancellationToken.None));
    }

    [Theory]
    [InlineData("123-456-32-18")]
    [InlineData(" 1234563218 ")]
    public async Task The_lookup_normalizes_the_nip_and_asks_for_today_in_poland(string typed)
    {
        var registry = new StubRegistry(_ => new RegisteredCompany("Firma", "Adres", "Czynny", null));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero));
        var lookup = new CompanyLookupService(registry, new AnyUser(), clock);

        var result = await lookup.FindByNipAsync(typed);

        Assert.Equal("Firma", result.Company!.Name);
        Assert.Equal(("1234563218", new DateOnly(2027, 1, 1)), Assert.Single(registry.Calls));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1234563219")]
    [InlineData("12345")]
    public async Task An_invalid_nip_is_not_sent(string? typed)
    {
        var registry = new StubRegistry(_ => null);
        var lookup = new CompanyLookupService(registry, new AnyUser(), new FakeTimeProvider());

        var result = await lookup.FindByNipAsync(typed);

        Assert.Contains("poprawny NIP", result.Error);
        Assert.Empty(registry.Calls);
    }

    [Fact]
    public async Task An_unknown_company_says_so()
    {
        var lookup = new CompanyLookupService(new StubRegistry(_ => null), new AnyUser(), new FakeTimeProvider());

        var result = await lookup.FindByNipAsync("1234563218");

        Assert.Contains("nie zna podmiotu", result.Error);
    }
}
