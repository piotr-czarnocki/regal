using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;

namespace Regal.UnitTests;

public class AccessPolicyTests
{
    private const string Email = "anna@example.com";

    private static User AllowedUser(bool isActive = true) =>
        new() { Email = Email, Role = Roles.Administrator, IsActive = isActive };

    [Fact]
    public void Active_user_on_the_list_is_allowed() =>
        Assert.Equal(AccessDecision.Allowed, AccessPolicy.Evaluate(Email, emailVerified: true, AllowedUser()));

    [Fact]
    public void Address_not_on_the_list_is_denied() =>
        Assert.Equal(AccessDecision.NotOnAllowList, AccessPolicy.Evaluate(Email, emailVerified: true, user: null));

    [Fact]
    public void Inactive_user_is_denied() =>
        Assert.Equal(AccessDecision.UserInactive, AccessPolicy.Evaluate(Email, emailVerified: true, AllowedUser(isActive: false)));

    [Fact]
    public void Unverified_address_is_denied_even_for_a_listed_user() =>
        Assert.Equal(AccessDecision.EmailNotVerified, AccessPolicy.Evaluate(Email, emailVerified: false, AllowedUser()));

    [Fact]
    public void Missing_address_is_denied() =>
        Assert.Equal(AccessDecision.MissingEmail, AccessPolicy.Evaluate(null, emailVerified: true, user: null));

    [Theory]
    [InlineData("Anna@Example.COM", "anna@example.com")]
    [InlineData("  anna@example.com ", "anna@example.com")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Addresses_are_normalized(string? input, string? expected) =>
        Assert.Equal(expected, EmailAddress.Normalize(input));
}
