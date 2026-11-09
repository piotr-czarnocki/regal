using Microsoft.Playwright;
using Regal.TestHost;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.E2ETests;

/// <summary>One database, one running application and one browser for all browser tests.</summary>
public sealed class AppFixture : IAsyncLifetime
{
    private sealed class FixedUser(int id) : ICurrentUser
    {
        public Task<int> RequireUserIdAsync(string requiredRole) => Task.FromResult(id);
    }

    private TestDatabase _database = null!;
    private IPlaywright _playwright = null!;

    public RegalTestHost Host { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public int AdminId { get; private set; }

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.StartAsync();
        Host = new RegalTestHost(_database.AppConnectionString).Start();
        AdminId = await Host.CreateUserAsync();

        // Downloads the browser on the first run only; later runs find it in the local cache.
        var installed = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (installed != 0)
        {
            throw new InvalidOperationException($"Playwright could not install Chromium (exit code {installed}).");
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        _playwright.Dispose();
        await Host.DisposeAsync();
        await _database.DisposeAsync();
    }

    public ProductService Products => new(Host.DbContextFactory, new FixedUser(AdminId), TimeProvider.System);

    public CategoryService Categories => new(Host.DbContextFactory, new FixedUser(AdminId), TimeProvider.System);

    public ContractorService Contractors => new(Host.DbContextFactory, new FixedUser(AdminId), TimeProvider.System);

    public StockDocumentService Documents => new(Host.DbContextFactory, new FixedUser(AdminId), TimeProvider.System);

    public InvoiceQueries InvoiceQueries => new(Host.DbContextFactory, new FixedUser(AdminId));

    /// <summary>A browser tab signed in as the given user, or anonymous when null.</summary>
    public async Task<IPage> NewPageAsync(int? userId, bool phone = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Host.BaseAddress.ToString(),
            Locale = "pl-PL",
            ViewportSize = phone ? new ViewportSize { Width = 390, Height = 844 } : new ViewportSize { Width = 1366, Height = 900 },
            IsMobile = phone,
            HasTouch = phone,
        });

        if (userId is { } id)
        {
            await context.AddCookiesAsync(
            [
                new Cookie { Name = TestAuthHandler.CookieName, Value = id.ToString(), Domain = Host.BaseAddress.Host, Path = "/" },
            ]);
        }

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        return page;
    }

    /// <summary>Opens the address and waits until the Blazor circuit handles input.</summary>
    public static async Task OpenAsync(IPage page, string path)
    {
        await page.GotoAsync(path);
        await page.WaitForSelectorAsync("body[data-interactive='true']");
    }

    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
    public const string Name = "App";
}
