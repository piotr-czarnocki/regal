using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Regal.TestHost;
using Regal.Web.Components.Layout;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Contractors;
using Regal.Web.Integrations.VatRegistry;

namespace Regal.IntegrationTests;

/// <summary>The contractor dialog filled from the VAT register, with a fake register (SPEC 5.15).</summary>
[Collection(PostgresCollection.Name)]
public sealed class CompanyLookupTests : WarehouseTestBase
{
    private readonly BunitContext _context = new();
    private readonly FakeVatRegistry _registry = new();

    public CompanyLookupTests(PostgresFixture postgres)
        : base(postgres)
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Services.AddScoped(_ => Contractors);
        _context.Services.AddScoped(_ => new CompanyLookupService(_registry, new FixedUser(UserId), Clock));
    }

    public override async Task DisposeAsync() => await _context.DisposeAsync();

    private async Task<IRenderedComponent<MudDialogProvider>> OpenAsync()
    {
        var provider = _context.Render<MudDialogProvider>();
        await provider.InvokeAsync(() => _context.Services.GetRequiredService<IDialogService>().ShowAsync<ContractorDialog>("Nowy kontrahent", DialogDefaults.Form));
        provider.WaitForElement("form");
        return provider;
    }

    private static IRenderedComponent<MudTextField<string>> Field(IRenderedComponent<MudDialogProvider> dialog, string label) =>
        dialog.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == label);

    [Fact]
    public async Task Fetched_data_fill_the_form_and_are_saved_with_the_contractor()
    {
        var name = Unique("PIEKARNIA");
        _registry.Add("1234563218", new RegisteredCompany(name, "UL. LIPOWA 1, 00-001 WARSZAWA", "Czynny", "123456785"));
        var dialog = await OpenAsync();

        Field(dialog, "NIP (opcjonalnie)").Find("input").Change("123-456-32-18");
        dialog.FindAll("button").Single(b => b.TextContent.Contains("Pobierz dane")).Click();
        dialog.WaitForAssertion(() => Assert.Contains("status: Czynny", dialog.Markup), TimeSpan.FromSeconds(10));
        Assert.Equal(name, Field(dialog, "Nazwa").Find("input").GetAttribute("value"));
        dialog.Find("form").Submit();

        await using var db = Postgres.CreateAppDbContext();
        Contractor? saved = null;
        for (var attempt = 0; attempt < 100 && saved is null; attempt++)
        {
            saved = await db.Contractors.AsNoTracking().SingleOrDefaultAsync(c => c.Name == name);
            await Task.Delay(50);
        }

        Assert.NotNull(saved);
        Assert.Equal(("1234563218", "UL. LIPOWA 1, 00-001 WARSZAWA"), (saved.Nip, saved.Address));
    }

    [Fact]
    public async Task An_unavailable_register_leaves_the_form_as_it_was_and_says_so()
    {
        _registry.Errors.Enqueue(new VatRegistryUnavailableException("timeout"));
        var dialog = await OpenAsync();
        Field(dialog, "Nazwa").Find("input").Change("Moja nazwa");

        Field(dialog, "NIP (opcjonalnie)").Find("input").Change("1234563218");
        dialog.FindAll("button").Single(b => b.TextContent.Contains("Pobierz dane")).Click();

        dialog.WaitForAssertion(() => Assert.Contains("teraz nie odpowiada", dialog.Markup), TimeSpan.FromSeconds(10));
        Assert.Equal("Moja nazwa", Field(dialog, "Nazwa").Find("input").GetAttribute("value"));
    }
}
