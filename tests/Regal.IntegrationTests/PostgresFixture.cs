using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Regal.Web.Data;
using Regal.Web.Features.Auth;
using Testcontainers.PostgreSql;

namespace Regal.IntegrationTests;

/// <summary>
/// A real PostgreSQL set up the way docker-compose.yml does it: same image, same init
/// script, migrations applied as the owner, and the tests connected as the restricted role.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _appPassword = Guid.NewGuid().ToString("N");
    private readonly PostgreSqlContainer _container;

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder("postgres:18.6")
            .WithDatabase("regal")
            .WithUsername("regal_owner")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .WithEnvironment("REGAL_APP_PASSWORD", _appPassword)
            .WithResourceMapping(new FileInfo(InitScriptPath()), "/docker-entrypoint-initdb.d/")
            .Build();
    }

    public string AppConnectionString => new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
    {
        Username = "regal_app",
        Password = _appPassword,
    }.ConnectionString;

    public AuthService CreateAuthService() =>
        new(new AppDbContextFactory(AppConnectionString), TimeProvider.System, NullLogger<AuthService>.Instance);

    public RegalDbContext CreateAppDbContext() => new AppDbContextFactory(AppConnectionString).CreateDbContext();

    public IDbContextFactory<RegalDbContext> DbContextFactory => new AppDbContextFactory(AppConnectionString);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<RegalDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        await using var ownerDb = new RegalDbContext(options);
        await ownerDb.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

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

    private sealed class AppDbContextFactory(string connectionString) : IDbContextFactory<RegalDbContext>
    {
        public RegalDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<RegalDbContext>().UseNpgsql(connectionString).Options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
