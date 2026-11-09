using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Warehouse;

namespace Regal.Web.Features.Analytics;

/// <summary>Both dates are included.</summary>
public sealed record AnalyticsFilter(DateOnly From, DateOnly To, int? CategoryId = null, int? ContractorId = null);

public sealed record SalesSummary(decimal NetSales, decimal Cost, int InvoiceCount)
{
    public decimal Margin => NetSales - Cost;

    public decimal? MarginPercent => NetSales == 0 ? null : decimal.Round(Margin * 100 / NetSales, 1, MidpointRounding.AwayFromZero);
}

public sealed record SalesPoint(DateOnly Start, string Label, decimal NetSales, decimal Margin);

public sealed record ProductSales(int ProductId, string Name, string CategoryName, string Unit, decimal Quantity, decimal NetSales, decimal Cost)
{
    public decimal Margin => NetSales - Cost;
}

public sealed record CategorySales(string CategoryName, decimal NetSales, decimal Cost)
{
    public decimal Margin => NetSales - Cost;
}

public sealed record SlowMovingRow(int ProductId, string Name, string CategoryName, string Unit, decimal Quantity, decimal Value, DateOnly? LastSale);

public sealed record ExpiringBatchRow(int ProductId, string ProductName, string BatchNumber, DateOnly ExpiryDate, decimal Quantity, string Unit, bool IsExpired);

/// <summary>
/// Sales are the lines of invoices that were not cancelled, dated by the invoice's issue date.
/// Cost is what the WZ recorded, so margin is net sales minus that cost.
/// </summary>
public sealed class AnalyticsService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private const int MaxRows = 200;
    private const int DailyPointsUpToDays = 62;

    public DateOnly Today => WarehouseTime.DateOf(timeProvider.GetUtcNow());

    public async Task<SalesSummary> GetSummaryAsync(AnalyticsFilter filter, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var lines = Lines(db, filter);
        var totals = await lines
            .GroupBy(_ => 1)
            .Select(g => new { Net = g.Sum(l => l.NetValue), Cost = g.Sum(l => l.Quantity * l.UnitCost) })
            .SingleOrDefaultAsync(cancellationToken);
        var invoices = await lines.Select(l => l.InvoiceId).Distinct().CountAsync(cancellationToken);

        return new SalesSummary(totals?.Net ?? 0, RoundMoney(totals?.Cost ?? 0), invoices);
    }

    /// <summary>One point per day for up to two months, otherwise one per month. Gaps are zeros.</summary>
    public async Task<IReadOnlyList<SalesPoint>> GetSalesOverTimeAsync(AnalyticsFilter filter, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var days = await Lines(db, filter)
            .GroupBy(l => l.Invoice!.IssueDate)
            .Select(g => new { Day = g.Key, Net = g.Sum(l => l.NetValue), Cost = g.Sum(l => l.Quantity * l.UnitCost) })
            .ToDictionaryAsync(d => d.Day, cancellationToken);

        var daily = filter.To.DayNumber - filter.From.DayNumber < DailyPointsUpToDays;
        var points = new List<SalesPoint>();
        for (var start = daily ? filter.From : new DateOnly(filter.From.Year, filter.From.Month, 1);
             start <= filter.To;
             start = daily ? start.AddDays(1) : start.AddMonths(1))
        {
            var end = daily ? start : start.AddMonths(1).AddDays(-1);
            var inPeriod = days.Values.Where(d => d.Day >= start && d.Day <= end).ToList();
            var net = inPeriod.Sum(d => d.Net);
            var cost = RoundMoney(inPeriod.Sum(d => d.Cost));
            points.Add(new SalesPoint(start, daily ? start.ToString("dd.MM") : start.ToString("MM.yyyy"), net, net - cost));
        }

        return points;
    }

    public async Task<IReadOnlyList<ProductSales>> GetSalesByProductAsync(
        AnalyticsFilter filter, int take = MaxRows, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await Lines(db, filter)
            .GroupBy(l => new { l.ProductId, l.Product!.Name, CategoryName = l.Product.Category!.Name, l.Product.Unit })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Name,
                g.Key.CategoryName,
                g.Key.Unit,
                Quantity = g.Sum(l => l.Quantity),
                Net = g.Sum(l => l.NetValue),
                Cost = g.Sum(l => l.Quantity * l.UnitCost),
            })
            .OrderByDescending(r => r.Net).ThenBy(r => r.ProductId)
            .Take(Math.Clamp(take, 1, MaxRows))
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ProductSales(r.ProductId, r.Name, r.CategoryName, r.Unit, r.Quantity, r.Net, RoundMoney(r.Cost))).ToList();
    }

    public async Task<IReadOnlyList<CategorySales>> GetSalesByCategoryAsync(AnalyticsFilter filter, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await Lines(db, filter)
            .GroupBy(l => l.Product!.Category!.Name)
            .Select(g => new { Name = g.Key, Net = g.Sum(l => l.NetValue), Cost = g.Sum(l => l.Quantity * l.UnitCost) })
            .OrderByDescending(r => r.Net)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CategorySales(r.Name, r.Net, RoundMoney(r.Cost))).ToList();
    }

    /// <summary>Every product sold in the period, so the classes cover all of its sales.</summary>
    public async Task<IReadOnlyList<AbcRow>> GetAbcAsync(AnalyticsFilter filter, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var items = await Lines(db, filter)
            .GroupBy(l => new { l.ProductId, l.Product!.Name })
            .Select(g => new AbcItem(g.Key.ProductId, g.Key.Name, g.Sum(l => l.NetValue)))
            .ToListAsync(cancellationToken);

        return AbcClassifier.Classify(items);
    }

    /// <summary>Goods in stock that have not been sold for at least the given number of days, most valuable first.</summary>
    public async Task<IReadOnlyList<SlowMovingRow>> GetSlowMovingAsync(
        int days, int? categoryId = null, int take = MaxRows, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var since = Today.AddDays(-Math.Max(days, 0));

        var products = db.Products.AsNoTracking().Where(p => p.Kind == ProductKind.Goods);
        if (categoryId is { } category)
        {
            products = products.Where(p => p.CategoryId == category);
        }

        var rows = await products
            .Select(p => new
            {
                p.Id,
                p.Name,
                CategoryName = p.Category!.Name,
                p.Unit,
                p.AverageCost,
                Quantity = db.Stock.Where(s => s.WarehouseId == Data.Entities.Warehouse.MainWarehouseId && s.Batch!.ProductId == p.Id).Sum(s => s.Quantity),
                LastSale = db.InvoiceLines
                    .Where(l => l.ProductId == p.Id && l.Invoice!.Status != InvoiceStatus.Cancelled)
                    .Max(l => (DateOnly?)l.Invoice!.IssueDate),
            })
            .Where(r => r.Quantity > 0 && (r.LastSale == null || r.LastSale < since))
            .OrderByDescending(r => r.Quantity * r.AverageCost).ThenBy(r => r.Id)
            .Take(Math.Clamp(take, 1, MaxRows))
            .ToListAsync(cancellationToken);

        return rows.Select(r => new SlowMovingRow(r.Id, r.Name, r.CategoryName, r.Unit, r.Quantity, RoundMoney(r.Quantity * r.AverageCost), r.LastSale)).ToList();
    }

    /// <summary>Batches in stock that expire within the given number of days, expired ones included.</summary>
    public async Task<IReadOnlyList<ExpiringBatchRow>> GetExpiringBatchesAsync(
        int days, int take = MaxRows, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var today = Today;
        var limit = today.AddDays(Math.Max(days, 0));

        var rows = await db.Stock.AsNoTracking()
            .Where(s => s.WarehouseId == Data.Entities.Warehouse.MainWarehouseId && s.Quantity > 0 && s.Batch!.ExpiryDate <= limit)
            .OrderBy(s => s.Batch!.ExpiryDate).ThenBy(s => s.BatchId)
            .Take(Math.Clamp(take, 1, MaxRows))
            .Select(s => new { s.Batch!.ProductId, s.Batch.Product!.Name, s.Batch.Number, Expiry = s.Batch.ExpiryDate!.Value, s.Quantity, s.Batch.Product.Unit })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ExpiringBatchRow(r.ProductId, r.Name, r.Number, r.Expiry, r.Quantity, r.Unit, BatchAllocator.IsExpired(r.Expiry, today))).ToList();
    }

    private static IQueryable<InvoiceLine> Lines(RegalDbContext db, AnalyticsFilter filter)
    {
        var lines = db.InvoiceLines.AsNoTracking().Where(l =>
            l.Invoice!.Status != InvoiceStatus.Cancelled
            && l.Invoice.IssueDate >= filter.From
            && l.Invoice.IssueDate <= filter.To);

        if (filter.CategoryId is { } categoryId)
        {
            lines = lines.Where(l => l.Product!.CategoryId == categoryId);
        }

        if (filter.ContractorId is { } contractorId)
        {
            lines = lines.Where(l => l.Invoice!.ContractorId == contractorId);
        }

        return lines;
    }

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
