using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Regal.Web.Features.Auth;

namespace Regal.UnitTests;

public class CurrentUserTests
{
    private sealed class FixedState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }

    private static ClaimsPrincipal SignedIn(string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, role)], "test"));

    [Fact]
    public async Task User_with_the_role_is_identified() =>
        Assert.Equal(7, await new CurrentUser(new FixedState(SignedIn(Roles.Administrator))).RequireUserIdAsync(Roles.Administrator));

    [Fact]
    public Task User_without_the_role_is_rejected() =>
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new CurrentUser(new FixedState(SignedIn("Magazynier"))).RequireUserIdAsync(Roles.Administrator));

    [Fact]
    public Task Anonymous_user_is_rejected() =>
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new CurrentUser(new FixedState(new ClaimsPrincipal(new ClaimsIdentity()))).RequireUserIdAsync(Roles.Administrator));
}
