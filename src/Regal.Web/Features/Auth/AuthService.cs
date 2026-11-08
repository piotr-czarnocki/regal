using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;

namespace Regal.Web.Features.Auth;

public sealed record LoginResult(AccessDecision Decision, User? User)
{
    public bool IsAllowed => Decision == AccessDecision.Allowed;
}

public sealed class AuthService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    TimeProvider timeProvider,
    ILogger<AuthService> logger)
{
    /// <summary>Applies the allow-list to a Google login and records the outcome in AuditLog.</summary>
    public async Task<LoginResult> AuthorizeLoginAsync(
        string? email,
        bool emailVerified,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = EmailAddress.Normalize(email);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = normalizedEmail is null
            ? null
            : await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        var decision = AccessPolicy.Evaluate(normalizedEmail, emailVerified, user);

        // An unverified address does not prove who is logging in, so it is not linked to the account.
        var actor = decision is AccessDecision.Allowed or AccessDecision.UserInactive ? user : null;

        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = actor?.Id,
            Action = decision == AccessDecision.Allowed ? AuditActions.LoginSucceeded : AuditActions.AccessDenied,
            EntityType = actor is null ? null : nameof(User),
            EntityId = actor?.Id.ToString(CultureInfo.InvariantCulture),
            Details = decision == AccessDecision.Allowed ? null : $"reason={decision}; email={normalizedEmail}",
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(cancellationToken);

        return new LoginResult(decision, decision == AccessDecision.Allowed ? user : null);
    }

    /// <summary>Builds the identity kept in the cookie. Nothing from Google but the display name is retained.</summary>
    public static ClaimsPrincipal CreatePrincipal(User user, string? displayName)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(displayName) ? user.Email : displayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
        ];

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>False when the user was deactivated or had the role changed after logging in.</summary>
    public async Task<bool> IsSessionValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var userId))
        {
            return false;
        }

        var role = principal.FindFirstValue(ClaimTypes.Role);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.Role == role, cancellationToken);
    }

    /// <summary>
    /// Creates the first administrator when missing. An existing row is left alone,
    /// so restarting the app cannot bring back an account that was deactivated.
    /// </summary>
    public async Task EnsureInitialAdminAsync(string? configuredEmail, CancellationToken cancellationToken = default)
    {
        var email = EmailAddress.Normalize(configuredEmail);
        if (email is null)
        {
            logger.LogWarning("Regal:InitialAdminEmail is not set; no initial administrator was created.");
            return;
        }

        if (!email.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Regal:InitialAdminEmail is not a valid e-mail address.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return;
        }

        db.Users.Add(new User { Email = email, Role = Roles.Administrator, IsActive = true });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Initial administrator created.");
    }
}
