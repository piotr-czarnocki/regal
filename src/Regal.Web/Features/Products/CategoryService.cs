using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;

namespace Regal.Web.Features.Products;

public sealed record CategoryItem(int Id, string Name);

public sealed class CategoryService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private const string UniqueNameIndex = "IX_Categories_Name_Lower";
    private const string DuplicateMessage = "Kategoria o tej nazwie już istnieje.";

    public async Task<PagedResult<CategoryItem>> SearchAsync(
        string? term, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Categories.AsNoTracking();
        if (SearchPattern.Contains(term) is { } pattern)
        {
            query = query.Where(c => EF.Functions.ILike(c.Name, pattern, SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(c => new CategoryItem(c.Id, c.Name))
            .ToListAsync(cancellationToken);

        return new PagedResult<CategoryItem>(items, total);
    }

    public async Task<IReadOnlyList<CategoryItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Categories.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new CategoryItem(c.Id, c.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<SaveResult> SaveAsync(int? id, string? name, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);

        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return SaveResult.Fail("Podaj nazwę kategorii.");
        }

        if (trimmed.Length > 100)
        {
            return SaveResult.Fail("Nazwa kategorii może mieć najwyżej 100 znaków.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            if (id is null)
            {
                var newId = await AuditTrail.InsertAsync(
                    db, new Category { Name = trimmed }, c => c.Id, userId, timeProvider, cancellationToken);
                return SaveResult.Ok(newId);
            }

            var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (category is null)
            {
                return SaveResult.Fail("Nie znaleziono kategorii.");
            }

            category.Name = trimmed;
            if (AuditTrail.AddUpdate(db, category, category.Id, userId, timeProvider))
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return SaveResult.Ok(category.Id);
        }
        catch (DbUpdateException exception) when (DbErrors.UniqueViolation(exception) == UniqueNameIndex)
        {
            return SaveResult.Fail(DuplicateMessage);
        }
    }
}
