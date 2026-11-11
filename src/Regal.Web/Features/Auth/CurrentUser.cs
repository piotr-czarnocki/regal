using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Regal.Web.Features.Auth;

public interface ICurrentUser
{
    /// <summary>
    /// Returns the Users.Id of the signed-in user when it has one of the roles (comma-separated,
    /// see Roles.Staff), or throws UnauthorizedAccessException.
    /// </summary>
    Task<int> RequireUserIdAsync(string requiredRole);
}

// Reads the Blazor authentication state: HttpContext is not available inside a circuit.
public sealed class CurrentUser(AuthenticationStateProvider authenticationStateProvider) : ICurrentUser
{
    public async Task<int> RequireUserIdAsync(string requiredRole)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;

        if (user.Identity?.IsAuthenticated == true
            && Roles.AllowsAny(requiredRole, user.IsInRole)
            && int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), CultureInfo.InvariantCulture, out var userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException();
    }
}
