using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Regal.Web.Data;
using Regal.Web.Features.Auth;

namespace Regal.TestHost;

/// <summary>
/// Stands in for Google sign-in in browser and load tests: the cookie names an existing user, and
/// only an active one gets in, as with the real cookie. Lives in test code only, never in the app.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDbContextFactory<RegalDbContext> dbContextFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string CookieName = "test-user";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var value)
            || !int.TryParse(value, CultureInfo.InvariantCulture, out var userId))
        {
            return AuthenticateResult.NoResult();
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(Context.RequestAborted);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, Context.RequestAborted);
        return user is null
            ? AuthenticateResult.NoResult()
            : AuthenticateResult.Success(new AuthenticationTicket(AuthService.CreatePrincipal(user, user.Email), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/login");
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/access-denied");
        return Task.CompletedTask;
    }
}
