using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Common;

namespace Regal.Web.Features.Auth;

public sealed record UserListItem(int Id, string Email, string Role, bool IsActive, bool IsMe);

/// <summary>
/// The allow-list kept from the application instead of by SQL (SPEC 4). A change of role or a
/// deactivation ends that person's open sessions, because AuthService.IsSessionValidAsync compares
/// both with the database. Nobody can change their own role or switch themselves off, so the
/// administrator doing it always keeps access.
/// </summary>
public sealed class UserService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private const string EmailIndex = "IX_Users_Email";

    /// <summary>Owns generated data and can never sign in (SPEC 5.9), so it is not shown.</summary>
    private const string GeneratorEmail = Data.Generator.DemoDataGenerator.UserEmail;

    public async Task<IReadOnlyList<UserListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var me = await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking()
            .Where(u => u.Email != GeneratorEmail)
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.Email)
            .Select(u => new UserListItem(u.Id, u.Email, u.Role, u.IsActive, u.Id == me))
            .ToListAsync(cancellationToken);
    }

    public async Task<SaveResult> AddAsync(string? email, string? role, CancellationToken cancellationToken = default)
    {
        var me = await currentUser.RequireUserIdAsync(Roles.Administrator);
        var address = EmailAddress.Normalize(email);
        if (!IsEmail(address))
        {
            return SaveResult.Fail("Podaj poprawny adres e-mail konta Google.");
        }

        if (role is null || !Roles.All.Contains(role))
        {
            return SaveResult.Fail("Wybierz rolę.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Users.AnyAsync(u => u.Email == address, cancellationToken))
        {
            return SaveResult.Fail($"Adres {address} jest już na liście.");
        }

        try
        {
            var id = await AuditTrail.InsertAsync(db, new User { Email = address!, Role = role, IsActive = true }, u => u.Id, me, timeProvider, cancellationToken);
            return SaveResult.Ok(id);
        }
        catch (DbUpdateException exception) when (DbErrors.UniqueViolation(exception) == EmailIndex)
        {
            return SaveResult.Fail($"Adres {address} jest już na liście.");
        }
    }

    public async Task<SaveResult> SetRoleAsync(int id, string? role, CancellationToken cancellationToken = default)
    {
        var me = await currentUser.RequireUserIdAsync(Roles.Administrator);
        if (role is null || !Roles.All.Contains(role))
        {
            return SaveResult.Fail("Wybierz rolę.");
        }

        if (id == me)
        {
            return SaveResult.Fail("Nie możesz zmienić własnej roli. Poproś o to innego administratora.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Email != GeneratorEmail, cancellationToken);
        if (user is null)
        {
            return SaveResult.Fail("Nie znaleziono użytkownika.");
        }

        user.Role = role;
        if (AuditTrail.AddUpdate(db, user, id, me, timeProvider))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return SaveResult.Ok(id);
    }

    public async Task<SaveResult> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var me = await currentUser.RequireUserIdAsync(Roles.Administrator);
        if (id == me)
        {
            return SaveResult.Fail("Nie możesz wyłączyć własnego konta.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Email != GeneratorEmail, cancellationToken);
        if (user is null)
        {
            return SaveResult.Fail("Nie znaleziono użytkownika.");
        }

        if (user.IsActive != isActive)
        {
            user.IsActive = isActive;
            AuditTrail.AddActivation<User>(db, isActive, id, me, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
        }

        return SaveResult.Ok(id);
    }

    private static bool IsEmail(string? address) =>
        address is { Length: <= 320 }
        && MailAddress.TryCreate(address, out var parsed)
        && parsed.Address == address;
}
