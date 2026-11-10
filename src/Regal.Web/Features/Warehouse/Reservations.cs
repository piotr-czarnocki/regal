using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Warehouse;

/// <summary>
/// Goods reserved by open customer orders (SPEC 5.11). Available for sale = stock in batches that
/// have not expired minus what is reserved. Read under the product locks when the result decides
/// a write, so it reflects every order and issue committed before.
/// </summary>
public static class Reservations
{
    /// <summary>Reserved quantity per product, leaving out the order an issue is fulfilling.</summary>
    public static async Task<Dictionary<int, decimal>> ReservedAsync(
        RegalDbContext db, IEnumerable<int> productIds, int? exceptOrderId, CancellationToken cancellationToken)
    {
        var ids = productIds.ToArray();
        return await db.CustomerOrderLines.AsNoTracking()
            .Where(l => ids.Contains(l.ProductId) && l.Order!.Status == CustomerOrderStatus.Open && l.OrderId != exceptOrderId)
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(row => row.ProductId, row => row.Quantity, cancellationToken);
    }

    /// <summary>Stock per product in batches that can still be sold today.</summary>
    public static async Task<Dictionary<int, decimal>> UnexpiredStockAsync(
        RegalDbContext db, IEnumerable<int> productIds, DateOnly today, CancellationToken cancellationToken)
    {
        var ids = productIds.ToArray();
        return await db.Stock.AsNoTracking()
            .Where(s => s.WarehouseId == MainWarehouse.Id && s.Quantity > 0 && ids.Contains(s.Batch!.ProductId)
                && (s.Batch.ExpiryDate == null || s.Batch.ExpiryDate >= today))
            .GroupBy(s => s.Batch!.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(s => s.Quantity) })
            .ToDictionaryAsync(row => row.ProductId, row => row.Quantity, cancellationToken);
    }

    /// <summary>The message for a sale that would take reserved goods, or null when it fits.</summary>
    public static string? CheckSale(decimal quantity, decimal unexpiredStock, decimal reserved, string unit)
    {
        var free = Math.Max(0, unexpiredStock - reserved);
        return quantity <= free || reserved == 0
            ? null
            : $"dostępne do sprzedaży {StockLabels.Quantity(free)} {unit}, bo {StockLabels.Quantity(reserved)} {unit} zarezerwowano w zamówieniach.";
    }
}
