using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Regal.Web.Components;
using Regal.Web.Data;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

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

var app = builder.Build();

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
