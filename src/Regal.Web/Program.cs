using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Regal.Web.Components;
using Regal.Web.Data;
using Regal.Web.Features.Analytics;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Orders;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;
using Regal.Web.Integrations.Fakturownia;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddSingleton(TimeProvider.System);

var connectionString = builder.Configuration.GetConnectionString("Regal")
    ?? throw new InvalidOperationException("Missing connection string 'Regal'. See README.md.");
builder.Services.AddDbContextFactory<RegalDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddRegalAuth(builder.Configuration);

builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<ContractorService>();
builder.Services.AddScoped<StockDocumentService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<StocktakeService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<InvoiceQueries>();
builder.Services.AddScoped<CustomerOrderService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<InvoiceSender>();
builder.Services.AddHostedService<InvoiceSendingWorker>();
builder.Services.AddScoped<InvoicePaymentService>();
builder.Services.AddHostedService<PaymentRefreshWorker>();
builder.Services.Configure<FakturowniaOptions>(builder.Configuration.GetSection(FakturowniaOptions.Section));
builder.Services.AddHttpClient<IFakturowniaClient, FakturowniaClient>(client => client.Timeout = TimeSpan.FromSeconds(30));

// Behind an HTTPS tunnel to a phone: the tunnel ends TLS, so the scheme comes from its headers.
// Only proxies on this machine are trusted, which is where the tunnel agent runs.
var behindTunnel = builder.Configuration.GetValue<bool>("Regal:BehindTunnel");
if (behindTunnel)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
}

var app = builder.Build();

if (behindTunnel)
{
    app.UseForwardedHeaders();
}

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AuthService>()
        .EnsureInitialAdminAsync(app.Configuration["Regal:InitialAdminEmail"]);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Polish number formats everywhere, e.g. a comma in prices typed into forms.
app.UseRequestLocalization(options =>
{
    options.SetDefaultCulture("pl-PL");
    options.AddSupportedCultures("pl-PL");
    options.AddSupportedUICultures("pl-PL");
});

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAuthEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();

/// <summary>Public so the end-to-end and load tests can host the application.</summary>
public partial class Program;
