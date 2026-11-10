using Regal.Web.Features.Auth;
using Regal.Web.Features.Warehouse;
using Regal.Web.Integrations.VatRegistry;

namespace Regal.Web.Features.Contractors;

/// <summary>Either the company found, or a message in Polish, ready to show.</summary>
public sealed record CompanyLookupResult(RegisteredCompany? Company, string? Error);

/// <summary>Fills a contractor's name and address from the VAT register by NIP (SPEC 5.15).</summary>
public sealed class CompanyLookupService(IVatRegistry registry, ICurrentUser currentUser, TimeProvider timeProvider)
{
    public async Task<CompanyLookupResult> FindByNipAsync(string? nip, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        var normalized = Nip.Normalize(nip);
        if (!Nip.IsValid(normalized))
        {
            return new CompanyLookupResult(null, "Wpisz poprawny NIP (10 cyfr z prawidłową cyfrą kontrolną).");
        }

        try
        {
            var company = await registry.FindByNipAsync(normalized!, WarehouseTime.DateOf(timeProvider.GetUtcNow()), cancellationToken);
            return company is null
                ? new CompanyLookupResult(null, "Biała lista nie zna podmiotu o tym NIP-ie. Uzupełnij dane ręcznie.")
                : new CompanyLookupResult(company, null);
        }
        catch (VatRegistryUnavailableException)
        {
            return new CompanyLookupResult(null, "Biała lista podatników VAT teraz nie odpowiada. Uzupełnij dane ręcznie albo spróbuj później.");
        }
        catch (VatRegistryRejectedException exception)
        {
            return new CompanyLookupResult(null, $"Biała lista odrzuciła zapytanie: {exception.Message}");
        }
    }
}
