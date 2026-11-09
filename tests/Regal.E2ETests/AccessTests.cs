using Microsoft.Playwright;

namespace Regal.E2ETests;

[Collection(AppCollection.Name)]
public sealed class AccessTests(AppFixture app)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/stock-documents/new")]
    [InlineData("/invoices")]
    public async Task Anonymous_visitor_is_sent_to_the_login_page(string path)
    {
        var page = await app.NewPageAsync(userId: null);

        await page.GotoAsync(path);

        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
        await Assertions.Expect(page.GetByText("Zaloguj przez Google")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Deactivated_user_gets_no_data()
    {
        var inactive = await app.Host.CreateUserAsync(isActive: false);
        var page = await app.NewPageAsync(inactive);

        await page.GotoAsync("/stock");

        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Fact]
    public async Task Signed_in_administrator_sees_the_dashboard_and_the_menu()
    {
        var page = await app.NewPageAsync(app.AdminId);

        await AppFixture.OpenAsync(page, "/");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Pulpit" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Dokumenty magazynowe" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Faktury", Exact = true })).ToBeVisibleAsync();
    }
}
