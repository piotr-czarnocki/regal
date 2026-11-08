using Microsoft.EntityFrameworkCore;
using Npgsql;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;

namespace Regal.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class AuthServiceTests(PostgresFixture postgres)
{
    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    private async Task<User> AddUserAsync(string email, bool isActive = true)
    {
        await using var db = postgres.CreateAppDbContext();
        var user = new User { Email = email, Role = Roles.Administrator, IsActive = isActive };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<AuditLogEntry> SingleAuditEntryContainingAsync(string email)
    {
        await using var db = postgres.CreateAppDbContext();
        return await db.AuditLog.SingleAsync(e => e.Details != null && e.Details.Contains(email));
    }

    [Fact]
    public async Task Active_user_logs_in_regardless_of_letter_case_and_the_login_is_audited()
    {
        var email = NewEmail();
        var user = await AddUserAsync(email);

        var result = await postgres.CreateAuthService().AuthorizeLoginAsync(email.ToUpperInvariant(), emailVerified: true);

        Assert.True(result.IsAllowed);
        Assert.Equal(Roles.Administrator, result.User!.Role);

        await using var db = postgres.CreateAppDbContext();
        var entry = await db.AuditLog.SingleAsync(e => e.UserId == user.Id);
        Assert.Equal(AuditActions.LoginSucceeded, entry.Action);
        Assert.Equal(DateTimeKind.Utc, entry.CreatedAt.Kind);
    }

    [Fact]
    public async Task Address_outside_the_list_is_denied_and_audited()
    {
        var email = NewEmail();

        var result = await postgres.CreateAuthService().AuthorizeLoginAsync(email, emailVerified: true);

        Assert.Equal(AccessDecision.NotOnAllowList, result.Decision);
        Assert.Null(result.User);

        var entry = await SingleAuditEntryContainingAsync(email);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Null(entry.UserId);
    }

    [Fact]
    public async Task Inactive_user_is_denied_and_audited_against_the_account()
    {
        var email = NewEmail();
        var user = await AddUserAsync(email, isActive: false);

        var result = await postgres.CreateAuthService().AuthorizeLoginAsync(email, emailVerified: true);

        Assert.Equal(AccessDecision.UserInactive, result.Decision);

        var entry = await SingleAuditEntryContainingAsync(email);
        Assert.Equal(AuditActions.AccessDenied, entry.Action);
        Assert.Equal(user.Id, entry.UserId);
    }

    [Fact]
    public async Task Session_stops_being_valid_after_deactivation()
    {
        var user = await AddUserAsync(NewEmail());
        var service = postgres.CreateAuthService();
        var principal = AuthService.CreatePrincipal(user, "Anna");
        Assert.True(await service.IsSessionValidAsync(principal));

        await using (var db = postgres.CreateAppDbContext())
        {
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        Assert.False(await service.IsSessionValidAsync(principal));
    }

    [Fact]
    public async Task Initial_admin_is_created_once_and_a_deactivated_one_stays_deactivated()
    {
        var email = NewEmail();
        var service = postgres.CreateAuthService();

        await service.EnsureInitialAdminAsync(email.ToUpperInvariant());
        await service.EnsureInitialAdminAsync(email);

        await using var db = postgres.CreateAppDbContext();
        var admin = await db.Users.SingleAsync(u => u.Email == email);
        Assert.Equal(Roles.Administrator, admin.Role);
        Assert.True(admin.IsActive);

        await db.Users.Where(u => u.Id == admin.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        await service.EnsureInitialAdminAsync(email);

        Assert.False(await db.Users.Where(u => u.Id == admin.Id).Select(u => u.IsActive).SingleAsync());
    }

    [Theory]
    [InlineData("UPDATE \"AuditLog\" SET \"Action\" = 'tampered'")]
    [InlineData("DELETE FROM \"AuditLog\"")]
    public async Task Application_role_cannot_change_the_audit_log(string sql)
    {
        await using var connection = new NpgsqlConnection(postgres.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
