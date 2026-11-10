using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;
using Regal.Web.Integrations.Fakturownia;
using Regal.Web.Integrations.VatRegistry;

namespace Regal.TestHost;

/// <summary>
/// The real application on Kestrel, with the test sign-in and a fake Fakturownia in place of the
/// real ones. Everything else, including the background invoice sender, runs as in production.
/// </summary>
public sealed class RegalTestHost(string connectionString) : WebApplicationFactory<Program>
{
    public FakeFakturownia Fakturownia { get; } = new();

    public FakeVatRegistry VatRegistry { get; } = new();

    /// <summary>Available after Start, e.g. http://127.0.0.1:51234.</summary>
    public Uri BaseAddress { get; private set; } = null!;

    public IDbContextFactory<RegalDbContext> DbContextFactory => Services.GetRequiredService<IDbContextFactory<RegalDbContext>>();

    public RegalTestHost Start(int port = 0)
    {
        // Port 0 lets the system pick a free port, so parallel runs never collide.
        UseKestrel(port);
        StartServer();
        var address = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseAddress = new Uri(address.Replace("[::]", "127.0.0.1", StringComparison.Ordinal).Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal));
        return this;
    }

    /// <summary>An allowed user; put its id in the TestAuthHandler cookie to act as it.</summary>
    public async Task<int> CreateUserAsync(bool isActive = true)
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", Role = Roles.Administrator, IsActive = isActive };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Outside Development the framework and package files (blazor.web.js, MudBlazor) are not
        // mapped from the build output on their own.
        builder.UseStaticWebAssets();
        builder.UseSetting("ConnectionStrings:Regal", connectionString);

        // Required by the real sign-in setup at startup; never used, the test scheme replaces it.
        builder.UseSetting("Authentication:Google:ClientId", "test");
        builder.UseSetting("Authentication:Google:ClientSecret", "test");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });
            services.AddSingleton<IFakturowniaClient>(Fakturownia);
            services.AddSingleton<IVatRegistry>(VatRegistry);
        });
    }
}
