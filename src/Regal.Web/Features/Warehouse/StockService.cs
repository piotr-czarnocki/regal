using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;

namespace Regal.Web.Features.Warehouse;

public sealed record StockFilter(int? CategoryId, string? Term, int? ExpiringWithinDays);

public sealed record StockListItem(
    int ProductId,
    string Sku,
    string Name,
    string CategoryName,
    string Unit,
    decimal Quantity,
    decimal AverageCost,
    bool IsActive,
    bool HasExpiredBatch,
    decimal Reserved = 0)
{
    public decimal Value => Quantity * AverageCost;
}

public sealed record StockBatchRow(int BatchId, string Number, DateOnly? ExpiryDate, decimal Quantity, bool IsExpired);

public sealed record ProductMovementRow(
    int DocumentId,
    string DocumentNumber,
    StockDocumentType DocumentType,
    DateOnly DocumentDate,
    string BatchNumber,
    DateOnly? ExpiryDate,
    decimal QuantityChange,
    decimal UnitCost);

public sealed record ProductPick(
    int Id,
    string Sku,
    string? Ean,
    string Name,
    string Unit,
    bool HasExpiry,
    decimal AverageCost,
    bool CanBeAdded = true,
    ProductKind Kind = ProductKind.Goods,
    decimal NetPrice = 0,
    string VatRate = "23");

public sealed class StockService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    /// <summary>Products with stock. Inactive ones stay listed: a correction can put goods back on them.</summary>
    public async Task<PagedResult<StockListItem>> SearchAsync(
        StockFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var today = WarehouseTime.DateOf(timeProvider.GetUtcNow());

        var products = db.Products.AsNoTracking().Where(p => p.Kind == ProductKind.Goods);
        if (filter.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (SearchPattern.Contains(filter.Term) is { } pattern)
        {
            products = products.Where(p =>
                EF.Functions.ILike(p.Name, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(p.Sku, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(p.Ean!, pattern, SearchPattern.Escape));
        }

        if (filter.ExpiringWithinDays is { } days)
        {
            var limit = today.AddDays(Math.Max(days, 0));
            products = products.Where(p => db.Stock.Any(s =>
                s.WarehouseId == MainWarehouse.Id && s.Quantity > 0 && s.Batch!.ProductId == p.Id && s.Batch.ExpiryDate <= limit));
        }

        var query = products
            .Select(p => new
            {
                Product = p,
                Quantity = db.Stock.Where(s => s.WarehouseId == MainWarehouse.Id && s.Batch!.ProductId == p.Id).Sum(s => s.Quantity),
            })
            .Where(row => row.Quantity > 0);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(row => row.Product.Name).ThenBy(row => row.Product.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(row => new StockListItem(
                row.Product.Id,
                row.Product.Sku,
                row.Product.Name,
                row.Product.Category!.Name,
                row.Product.Unit,
                row.Quantity,
                row.Product.AverageCost,
                row.Product.IsActive,
                db.Stock.Any(s =>
                    s.WarehouseId == MainWarehouse.Id && s.Quantity > 0 && s.Batch!.ProductId == row.Product.Id && s.Batch.ExpiryDate < today)))
            .ToListAsync(cancellationToken);

        // Open customer orders: shown next to the quantity, so a refused WZ can be understood.
        var reserved = await Reservations.ReservedAsync(db, items.Select(i => i.ProductId), exceptOrderId: null, cancellationToken);
        items = items.Select(i => i with { Reserved = reserved.GetValueOrDefault(i.ProductId) }).ToList();

        return new PagedResult<StockListItem>(items, total);
    }

    /// <summary>Batches that hold stock, in the order an automatic issue would take them.</summary>
    public async Task<IReadOnlyList<StockBatchRow>> GetBatchesAsync(int productId, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var today = WarehouseTime.DateOf(timeProvider.GetUtcNow());

        var rows = await db.Stock.AsNoTracking()
            .Where(s => s.WarehouseId == MainWarehouse.Id && s.Quantity > 0 && s.Batch!.ProductId == productId)
            .OrderBy(s => s.Batch!.ExpiryDate == null).ThenBy(s => s.Batch!.ExpiryDate).ThenBy(s => s.BatchId)
            .Select(s => new { s.BatchId, s.Batch!.Number, s.Batch.ExpiryDate, s.Quantity })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new StockBatchRow(r.BatchId, r.Number, r.ExpiryDate, r.Quantity, BatchAllocator.IsExpired(r.ExpiryDate, today))).ToList();
    }

    public async Task<PagedResult<ProductMovementRow>> GetProductMovementsAsync(
        int productId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.StockMovements.AsNoTracking().Where(m => m.Batch!.ProductId == productId);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(m => m.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(m => new ProductMovementRow(
                m.DocumentId, m.Document!.Number, m.Document.Type, m.Document.DocumentDate, m.Batch!.Number, m.Batch.ExpiryDate, m.QuantityChange, m.UnitCost))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductMovementRow>(items, total);
    }

    /// <summary>Active goods for a document's product field, by name, SKU or EAN.</summary>
    public Task<IReadOnlyList<ProductPick>> SearchGoodsAsync(string? term, int take, CancellationToken cancellationToken = default) =>
        SearchProductsAsync(term, take, includeServices: false, cancellationToken);

    /// <summary>Active goods and services for an invoice's product field.</summary>
    public Task<IReadOnlyList<ProductPick>> SearchSellableAsync(string? term, int take, CancellationToken cancellationToken = default) =>
        SearchProductsAsync(term, take, includeServices: true, cancellationToken);

    /// <summary>
    /// The product with exactly this EAN, otherwise with exactly this SKU. Scanners may report codes
    /// starting with 0 as 12-digit UPC-A. An inactive product or a service is returned with
    /// CanBeAdded false: the code does name it, so the caller must not settle for a similar one.
    /// </summary>
    public Task<ProductPick?> FindByCodeAsync(string? code, CancellationToken cancellationToken = default) =>
        FindProductByCodeAsync(code, includeServices: false, cancellationToken);

    /// <summary>As FindByCodeAsync, but a service can be added too: invoices sell both.</summary>
    public Task<ProductPick?> FindSellableByCodeAsync(string? code, CancellationToken cancellationToken = default) =>
        FindProductByCodeAsync(code, includeServices: true, cancellationToken);

    private async Task<IReadOnlyList<ProductPick>> SearchProductsAsync(
        string? term, int take, bool includeServices, CancellationToken cancellationToken)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Products.AsNoTracking().Where(p => p.IsActive && (includeServices || p.Kind == ProductKind.Goods));
        if (SearchPattern.Contains(term) is { } pattern)
        {
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(p.Sku, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(p.Ean!, pattern, SearchPattern.Escape));
        }

        return await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Take(Paging.Take(take))
            .Select(p => new ProductPick(p.Id, p.Sku, p.Ean, p.Name, p.Unit, p.HasExpiry, p.AverageCost, true, p.Kind, p.NetPrice, p.VatRate))
            .ToListAsync(cancellationToken);
    }

    private async Task<ProductPick?> FindProductByCodeAsync(string? code, bool includeServices, CancellationToken cancellationToken)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);

        var text = code?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var ean = text.Length == 12 && text.All(char.IsAsciiDigit) ? "0" + text : text;
        var sku = text.ToUpperInvariant(); // ProductService stores every SKU in upper case

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var matches = await db.Products.AsNoTracking()
            .Where(p => p.Ean == ean || p.Sku == sku)
            .Select(p => new ProductPick(
                p.Id, p.Sku, p.Ean, p.Name, p.Unit, p.HasExpiry, p.AverageCost,
                p.IsActive && (includeServices || p.Kind == ProductKind.Goods), p.Kind, p.NetPrice, p.VatRate))
            .ToListAsync(cancellationToken);

        return matches.FirstOrDefault(p => p.Ean == ean) ?? matches.FirstOrDefault();
    }
}
