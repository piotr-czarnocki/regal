namespace Regal.Web.Features.Auth;

/// <summary>
/// Roles are stored in Users.Role and put into the identity at login (SPEC 4). A service method
/// names the roles it allows; a comma-separated value allows any of them.
/// </summary>
public static class Roles
{
    /// <summary>Everything, including sales, invoices, analytics and users.</summary>
    public const string Administrator = "Administrator";

    /// <summary>Only the warehouse: stock, stock documents and stocktakes (SPEC 5.19).</summary>
    public const string Warehouse = "Warehouse";

    /// <summary>Anyone who works in the warehouse, the administrator included.</summary>
    public const string Staff = Administrator + "," + Warehouse;

    public static readonly IReadOnlyList<string> All = [Administrator, Warehouse];

    public static string Label(string role) => role switch
    {
        Administrator => "Administrator",
        Warehouse => "Magazynier",
        _ => role,
    };

    public static bool AllowsAny(string requiredRoles, Func<string, bool> isInRole) =>
        requiredRoles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(isInRole);
}
