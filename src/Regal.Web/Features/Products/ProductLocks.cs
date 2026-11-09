using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Products;

public static class ProductLocks
{
    /// <summary>
    /// Locks the product rows until the end of the current transaction and returns their current values.
    /// Everything that depends on a product's stock or average cost takes this lock first, so such
    /// operations run one at a time per product. Ascending order keeps two documents from deadlocking.
    /// </summary>
    public static Task<List<Product>> LockAsync(RegalDbContext db, IEnumerable<int> productIds, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row lock lasts only as long as a transaction, so one must be open.");
        }

        var ids = productIds.Distinct().Order().ToArray();

        // Not tracked: a context shared with a caller may already hold an older copy of these rows.
        return db.Products
            .FromSql($"""SELECT * FROM "Products" WHERE "Id" = ANY({ids}) ORDER BY "Id" FOR UPDATE""")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
