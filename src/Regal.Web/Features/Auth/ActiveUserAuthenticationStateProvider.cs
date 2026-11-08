using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Regal.Web.Features.Auth;

/// <summary>
/// A Blazor circuit can live for hours without a single HTTP request, so the cookie
/// check alone would miss a deactivation. This re-checks the user while the circuit runs.
/// </summary>
public sealed class ActiveUserAuthenticationStateProvider(ILoggerFactory loggerFactory, AuthService authService)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken) =>
        authService.IsSessionValidAsync(authenticationState.User, cancellationToken);
}
