using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Regal.Web.Data;

/// <summary>
/// Used only by the dotnet-ef tool. Migrations run as the owner role, from the
/// "Migrations" connection string, never as the role used by the running app.
/// </summary>
public sealed class RegalDbContextDesignFactory : IDesignTimeDbContextFactory<RegalDbContext>
{
    public RegalDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<RegalDbContextDesignFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        // The fallback has no password: enough to generate a migration, not to apply one.
        var connectionString = configuration.GetConnectionString("Migrations")
            ?? "Host=127.0.0.1;Database=regal;Username=regal_owner";

        var options = new DbContextOptionsBuilder<RegalDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new RegalDbContext(options);
    }
}
