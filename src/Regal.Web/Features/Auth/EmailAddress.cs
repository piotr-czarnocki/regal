namespace Regal.Web.Features.Auth;

public static class EmailAddress
{
    /// <summary>Returns the canonical form used in the Users table, or null when blank.</summary>
    public static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}
