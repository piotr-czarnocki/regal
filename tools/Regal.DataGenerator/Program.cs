using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Regal.Web.Data;
using Regal.Web.Data.Generator;

// Usage, with the database migrated and the application stopped:
//   dotnet run --project tools/Regal.DataGenerator -- --environment Development [--products 200] [--customers 60]
//     [--suppliers 8] [--days 365] [--invoices-per-day 8] [--seed 1] [--prefix DEMO]
// The connection string "Regal" comes from the web project's User Secrets, as for the application.
// An environment variable (ConnectionStrings__Regal) or --ConnectionStrings:Regal overrides it.

// Built by hand, so the order is certain: command line over environment over User Secrets.
// An earlier version added User Secrets last, so they silently won over an explicit setting.
var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(RegalDbContext).Assembly, optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var environment = configuration["environment"] ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
DemoDataGenerator.EnsureAllowed(environment);

var connectionString = configuration.GetConnectionString("Regal")
    ?? throw new InvalidOperationException("Missing connection string 'Regal'. See README.md.");

var options = new GeneratorOptions();
string? Value(string name) => configuration[name];
options.Products = int.Parse(Value("products") ?? options.Products.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.Customers = int.Parse(Value("customers") ?? options.Customers.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.Suppliers = int.Parse(Value("suppliers") ?? options.Suppliers.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.Days = int.Parse(Value("days") ?? options.Days.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.InvoicesPerDay = double.Parse(Value("invoices-per-day") ?? options.InvoicesPerDay.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.Seed = int.Parse(Value("seed") ?? options.Seed.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.SkuPrefix = Value("prefix") ?? options.SkuPrefix;

// Says where the data goes before writing anything; the password is never printed.
var target = new NpgsqlConnectionStringBuilder(connectionString);
Console.WriteLine($"Baza docelowa: {target.Host}:{target.Port}/{target.Database} (użytkownik {target.Username}).");

var dbOptions = new DbContextOptionsBuilder<RegalDbContext>().UseNpgsql(connectionString).Options;
var factory = new GeneratorDbContextFactory(dbOptions);
var started = TimeProvider.System.GetTimestamp();

var report = await new DemoDataGenerator(factory, options, TimeProvider.System, Console.WriteLine).RunAsync();

Console.WriteLine(
    $"Gotowe w {TimeProvider.System.GetElapsedTime(started):hh\\:mm\\:ss}: {report.Products} produktów, {report.Contractors} kontrahentów, " +
    $"{report.StockDocuments} dokumentów przyjęcia i rozchodu, {report.Invoices} faktur ({report.CancelledInvoices} anulowanych), " +
    $"{report.Movements} ruchów magazynowych w bazie.");

internal sealed class GeneratorDbContextFactory(DbContextOptions<RegalDbContext> options) : IDbContextFactory<RegalDbContext>
{
    public RegalDbContext CreateDbContext() => new(options);
}
