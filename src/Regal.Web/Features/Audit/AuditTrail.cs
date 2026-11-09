using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Audit;

/// <summary>Adds journal entries to the same unit of work as the change they describe.</summary>
public static class AuditTrail
{
    /// <summary>Inserts a row and its journal entry in one transaction. The entry needs the generated id.</summary>
    public static async Task<int> InsertAsync<TEntity>(
        RegalDbContext db,
        TEntity entity,
        Func<TEntity, int> getId,
        int userId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var id = getId(entity);
        Add(db, AuditActions.EntityCreated, typeof(TEntity).Name, id, userId, null, timeProvider);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Records which fields changed (names only). Returns false when nothing changed.</summary>
    public static bool AddUpdate<TEntity>(
        RegalDbContext db,
        TEntity entity,
        int id,
        int userId,
        TimeProvider timeProvider)
        where TEntity : class
    {
        db.ChangeTracker.DetectChanges();
        var changed = db.Entry(entity).Properties
            .Where(property => property.IsModified)
            .Select(property => property.Metadata.Name)
            .ToArray();

        if (changed.Length == 0)
        {
            return false;
        }

        Add(db, AuditActions.EntityUpdated, typeof(TEntity).Name, id, userId, $"changed={string.Join(',', changed)}", timeProvider);
        return true;
    }

    public static void AddActivation<TEntity>(RegalDbContext db, bool isActive, int id, int userId, TimeProvider timeProvider) =>
        Add(db, isActive ? AuditActions.EntityActivated : AuditActions.EntityDeactivated, typeof(TEntity).Name, id, userId, null, timeProvider);

    /// <summary>The caller saves it together with the document, in the document's transaction.</summary>
    public static void AddStockDocument(RegalDbContext db, string action, int id, int userId, string details, TimeProvider timeProvider) =>
        Add(db, action, nameof(StockDocument), id, userId, details, timeProvider);

    private static void Add(
        RegalDbContext db,
        string action,
        string entityType,
        int id,
        int userId,
        string? details,
        TimeProvider timeProvider) =>
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = id.ToString(CultureInfo.InvariantCulture),
            Details = details,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
}
