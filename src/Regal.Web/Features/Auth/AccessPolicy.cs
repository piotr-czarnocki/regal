using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Auth;

public enum AccessDecision
{
    Allowed,
    MissingEmail,
    EmailNotVerified,
    NotOnAllowList,
    UserInactive,
}

/// <summary>The allow-list rule, free of I/O so it can be unit tested.</summary>
public static class AccessPolicy
{
    /// <param name="normalizedEmail">Address from Google after EmailAddress.Normalize.</param>
    /// <param name="emailVerified">Whether Google confirmed the address.</param>
    /// <param name="user">The Users row with that address, or null when there is none.</param>
    public static AccessDecision Evaluate(string? normalizedEmail, bool emailVerified, User? user)
    {
        if (normalizedEmail is null)
        {
            return AccessDecision.MissingEmail;
        }

        if (!emailVerified)
        {
            return AccessDecision.EmailNotVerified;
        }

        if (user is null)
        {
            return AccessDecision.NotOnAllowList;
        }

        return user.IsActive ? AccessDecision.Allowed : AccessDecision.UserInactive;
    }
}
