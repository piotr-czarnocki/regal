using Microsoft.EntityFrameworkCore;
using Npgsql;
using Regal.Web.Data;
using Testcontainers.PostgreSql;

namespace Regal.TestHost;

/// <summary>
/// A real PostgreSQL set up the way docker-compose.yml does it: same image, same init script,
/// migrations applied as the owner, and the application connected as the restricted role.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;
    private readonly string _appPassword;

    private TestDatabase(PostgreSqlContainer container, string appPassword)
    {
        _container = container;
        _appPassword = appPassword;
    }

    public string AppConnectionString => new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
    {
        Username = "regal_app",
        Password = _appPassword,
    }.ConnectionString;

    public static async Task<TestDatabase> StartAsync()
    {
        var appPassword = Guid.NewGuid().ToString("N");
        var container = new PostgreSqlBuilder("postgres:18.6")
            .WithDatabase("regal")
            .WithUsername("regal_owner")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .WithEnvironment("REGAL_APP_PASSWORD", appPassword)
            .WithResourceMapping(new FileInfo(InitScriptPath()), "/docker-entrypoint-initdb.d/")
            .Build();
        await container.StartAsync();

        var options = new DbContextOptionsBuilder<RegalDbContext>().UseNpgsql(container.GetConnectionString()).Options;
        await using (var ownerDb = new RegalDbContext(options))
        {
            await ownerDb.Database.MigrateAsync();
        }

        return new TestDatabase(container, appPassword);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private static string InitScriptPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Regal.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("Repository root not found.")
            : Path.Combine(directory.FullName, "docker", "postgres", "init", "01-create-app-role.sh");
    }
}
