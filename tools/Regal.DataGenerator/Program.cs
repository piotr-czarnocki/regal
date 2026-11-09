using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Regal.Web.Data;
using Regal.Web.Data.Generator;

// Usage, with the database migrated and the application stopped:
//   dotnet run --project tools/Regal.DataGenerator -- --environment Development [--products 200] [--customers 60]
//     [--suppliers 8] [--days 365] [--invoices-per-day 8] [--seed 1] [--prefix DEMO]
// The connection string "Regal" comes from the web project's User Secrets, as for the application.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets(typeof(RegalDbContext).Assembly, optional: true);

DemoDataGenerator.EnsureAllowed(builder.Environment.EnvironmentName);

var connectionString = builder.Configuration.GetConnectionString("Regal")
    ?? throw new InvalidOperationException("Missing connection string 'Regal'. See README.md.");
builder.Services.AddDbContextFactory<RegalDbContext>(options => options.UseNpgsql(connectionString));

var options = new GeneratorOptions();
string? Value(string name) => builder.Configuration[name];
options.Products = int.Parse(Value("products") ?? options.Products.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.Customers = int.Parse(Value("customers") ?? options.Customers.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.Suppliers = int.Parse(Value("suppliers") ?? options.Suppliers.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.Days = int.Parse(Value("days") ?? options.Days.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.InvoicesPerDay = double.Parse(Value("invoices-per-day") ?? options.InvoicesPerDay.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.Seed = int.Parse(Value("seed") ?? options.Seed.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
options.SkuPrefix = Value("prefix") ?? options.SkuPrefix;

using var host = builder.Build();
var factory = host.Services.GetRequiredService<IDbContextFactory<RegalDbContext>>();
var started = TimeProvider.System.GetTimestamp();

var report = await new DemoDataGenerator(factory, options, TimeProvider.System, Console.WriteLine).RunAsync();

Console.WriteLine(
    $"Gotowe w {TimeProvider.System.GetElapsedTime(started):hh\\:mm\\:ss}: {report.Products} produktów, {report.Contractors} kontrahentów, " +
    $"{report.StockDocuments} dokumentów przyjęcia i rozchodu, {report.Invoices} faktur ({report.CancelledInvoices} anulowanych), " +
    $"{report.Movements} ruchów magazynowych w bazie.");
