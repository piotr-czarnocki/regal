using Microsoft.Playwright;
using Regal.Web.Features.Auth;

namespace Regal.E2ETests;

/// <summary>Who sees what (SPEC 5.19), and adding a person without SQL.</summary>
[Collection(AppCollection.Name)]
public sealed class RoleScenarioTests(AppFixture app)
{
    [Fact]
    public async Task A_warehouse_worker_sees_only_the_warehouse_and_cannot_open_invoices_by_address()
    {
        var worker = await app.Host.CreateUserAsync(role: Roles.Warehouse);
        var page = await app.NewPageAsync(worker, phone: true);

        await page.WithScreenshotOnFailureAsync("warehouse-role", async () =>
        {
            await AppFixture.OpenAsync(page, "/");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Przyjęcie lub wydanie towaru" })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Partie z bliską datą ważności")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Sprzedaż netto")).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Nowa faktura" })).ToHaveCountAsync(0);
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Faktury", Exact = true })).ToHaveCountAsync(0);

            await AppFixture.OpenAsync(page, "/invoices");
            await Assertions.Expect(page.GetByText("Nie masz uprawnień do tej strony")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Wróć na pulpit" })).ToBeVisibleAsync();

            await AppFixture.OpenAsync(page, "/stock-documents/new");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Nie masz uprawnień")).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task An_administrator_adds_a_warehouse_worker_on_the_users_page()
    {
        var page = await app.NewPageAsync(app.AdminId);
        var email = $"{Guid.NewGuid():N}@example.com";

        await page.WithScreenshotOnFailureAsync("users", async () =>
        {
            await AppFixture.OpenAsync(page, "/users");
            await page.Field("Adres e-mail konta Google").FillAsync(email);
            await page.Button("Dodaj użytkownika").ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Cell, new() { Name = email, Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("tr", new() { HasText = email })).ToContainTextAsync("Magazynier");
        });
    }
}
