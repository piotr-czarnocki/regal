using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Components.Authorization;

namespace Regal.Web.Features.Auth;

public static class AuthSetup
{
    private const string EmailVerifiedClaim = "email_verified";

    public static IServiceCollection AddRegalAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var clientId = RequireSetting(configuration, "Authentication:Google:ClientId");
        var clientSecret = RequireSetting(configuration, "Authentication:Google:ClientSecret");

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/access-denied";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Cookie.Name = "__Host-Regal";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax, not Strict: the cookie must survive the redirect back from Google.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Events.OnValidatePrincipal = ValidateSessionAsync;
            })
            .AddGoogle(options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.SaveTokens = false;
                options.ClaimActions.MapJsonKey(EmailVerifiedClaim, EmailVerifiedClaim);
                options.Events = new OAuthEvents
                {
                    OnTicketReceived = OnGoogleTicketReceivedAsync,
                    OnRemoteFailure = OnGoogleFailure,
                };
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthService>();
        services.AddScoped<AuthenticationStateProvider, ActiveUserAuthenticationStateProvider>();

        return services;
    }

    // Login and logout are plain HTTP endpoints because a cookie cannot be set over the SignalR circuit.
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/login", () => Results.Challenge(
            new GoogleChallengeProperties { RedirectUri = "/", Prompt = "select_account" },
            [GoogleDefaults.AuthenticationScheme]));

        endpoints.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            // Checked by hand: UseAntiforgery only records the result for endpoints that bind form data.
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/login");
        });

        return endpoints;
    }

    private static string RequireSetting(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Missing configuration value '{key}'. See README.md.")
            : value;
    }

    private static async Task OnGoogleTicketReceivedAsync(TicketReceivedContext context)
    {
        var google = context.Principal!;
        var emailVerified = string.Equals(
            google.FindFirstValue(EmailVerifiedClaim), "true", StringComparison.OrdinalIgnoreCase);

        var authService = context.HttpContext.RequestServices.GetRequiredService<AuthService>();
        var result = await authService.AuthorizeLoginAsync(
            google.FindFirstValue(ClaimTypes.Email), emailVerified, context.HttpContext.RequestAborted);

        if (!result.IsAllowed)
        {
            context.Response.Redirect("/access-denied");
            context.HandleResponse();
            return;
        }

        context.Principal = AuthService.CreatePrincipal(result.User!, google.FindFirstValue(ClaimTypes.Name));
    }

    private static Task OnGoogleFailure(RemoteFailureContext context)
    {
        // Only the exception type: the message may echo data from the provider's response.
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthSetup))
            .LogWarning("Google sign-in failed: {FailureType}", context.Failure?.GetType().Name);

        context.Response.Redirect("/login?error=1");
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var authService = context.HttpContext.RequestServices.GetRequiredService<AuthService>();
        if (!await authService.IsSessionValidAsync(context.Principal!, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
