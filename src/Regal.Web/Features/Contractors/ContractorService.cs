using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;

namespace Regal.Web.Features.Contractors;

public sealed class ContractorInput
{
    public string? Name { get; set; }
    public string? Nip { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public sealed record ContractorListItem(int Id, string Name, string? Nip, string? Address, string? Email, string? Phone, bool IsActive);

public sealed class ContractorService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public async Task<PagedResult<ContractorListItem>> SearchAsync(
        string? term, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Contractors.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (SearchPattern.Contains(term) is { } pattern)
        {
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(c.Nip!, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(c.Email!, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(c.Phone!, pattern, SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(c => new ContractorListItem(c.Id, c.Name, c.Nip, c.Address, c.Email, c.Phone, c.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<ContractorListItem>(items, total);
    }

    public async Task<ContractorInput?> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Contractors.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new ContractorInput { Name = c.Name, Nip = c.Nip, Address = c.Address, Email = c.Email, Phone = c.Phone })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SaveResult> SaveAsync(int? id, ContractorInput input, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);

        if (Validate(input) is { } error)
        {
            return SaveResult.Fail(error);
        }

        var name = input.Name!.Trim();
        var nip = Nip.Normalize(input.Nip);
        var address = Clean(input.Address);
        var email = Clean(input.Email);
        var phone = Clean(input.Phone);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (id is null)
        {
            var contractor = new Contractor { Name = name, Nip = nip, Address = address, Email = email, Phone = phone };
            var newId = await AuditTrail.InsertAsync(db, contractor, c => c.Id, userId, timeProvider, cancellationToken);
            return SaveResult.Ok(newId);
        }

        var existing = await db.Contractors.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (existing is null)
        {
            return SaveResult.Fail("Nie znaleziono kontrahenta.");
        }

        existing.Name = name;
        existing.Nip = nip;
        existing.Address = address;
        existing.Email = email;
        existing.Phone = phone;

        if (AuditTrail.AddUpdate(db, existing, existing.Id, userId, timeProvider))
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return SaveResult.Ok(existing.Id);
    }

    public async Task<SaveResult> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var contractor = await db.Contractors.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contractor is null)
        {
            return SaveResult.Fail("Nie znaleziono kontrahenta.");
        }

        if (contractor.IsActive != isActive)
        {
            contractor.IsActive = isActive;
            AuditTrail.AddActivation<Contractor>(db, isActive, contractor.Id, userId, timeProvider);
            await db.SaveChangesAsync(cancellationToken);
        }

        return SaveResult.Ok(contractor.Id);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Validate(ContractorInput input)
    {
        var name = Clean(input.Name);
        if (name is null)
        {
            return "Podaj nazwę kontrahenta.";
        }

        if (name.Length > 200)
        {
            return "Nazwa może mieć najwyżej 200 znaków.";
        }

        var nip = Nip.Normalize(input.Nip);
        if (nip is not null && !Nip.IsValid(nip))
        {
            return "NIP musi mieć 10 cyfr i poprawną sumę kontrolną.";
        }

        if (Clean(input.Address) is { Length: > 500 })
        {
            return "Adres może mieć najwyżej 500 znaków.";
        }

        var email = Clean(input.Email);
        if (email is not null && (email.Length > 320 || !MailAddress.TryCreate(email, out _)))
        {
            return "Podaj poprawny adres e-mail.";
        }

        if (Clean(input.Phone) is { Length: > 30 })
        {
            return "Telefon może mieć najwyżej 30 znaków.";
        }

        return null;
    }
}
