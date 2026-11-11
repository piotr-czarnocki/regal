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

public sealed record CustomerSales(int ContractorId, string Name, string? Nip, int InvoiceCount, decimal NetSales, decimal Cost, DateOnly LastPurchase)
{
    public decimal Margin => NetSales - Cost;

    public decimal AverageInvoice => InvoiceCount == 0 ? 0 : decimal.Round(NetSales / InvoiceCount, 2, MidpointRounding.AwayFromZero);
}

public sealed record PeriodSummary(DateOnly From, DateOnly To, SalesSummary Sales);

/// <summary>The filtered period beside the one just before it and the same dates a year earlier.</summary>
public sealed record PeriodComparison(PeriodSummary Current, PeriodSummary Previous, PeriodSummary YearEarlier);

public sealed record ForecastRow(
    int ProductId, string Name, string Unit, decimal SoldInHistory, decimal DailyAverage, decimal Available, decimal? DaysOfCover, decimal SuggestedOrder);

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
        var invoices = await lines.Where(l => l.Invoice!.Kind == InvoiceKind.Vat).Select(l => l.InvoiceId).Distinct().CountAsync(cancellationToken);

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

        // Stock and last sale are each aggregated once for all products and joined, rather than
        // computed per product: as correlated subqueries EF repeated them in the filter and the
        // ordering, which took 590 ms over 10 000 products (docs/analiza-dynamiczna.md, point 5).
        var stock = db.Stock
            .Where(s => s.WarehouseId == Data.Entities.Warehouse.MainWarehouseId)
            .GroupBy(s => s.Batch!.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(s => s.Quantity) })
            .Where(s => s.Quantity > 0);
        // An explicit join: with the navigation inside Max, EF turned the maximum back into a subquery.
        var lastSales =
            from l in db.InvoiceLines
            join i in db.Invoices on l.InvoiceId equals i.Id
            where i.Status != InvoiceStatus.Cancelled && i.Kind == InvoiceKind.Vat // a proforma or a return is not a sale
            group i.IssueDate by l.ProductId into g
            select new { ProductId = g.Key, LastSale = g.Max() };

        var rows = await (
                from p in products
                join s in stock on p.Id equals s.ProductId
                join sale in lastSales on p.Id equals sale.ProductId into sales
                from sale in sales.DefaultIfEmpty()
                select new
                {
                    p.Id,
                    p.Name,
                    CategoryName = p.Category!.Name,
                    p.Unit,
                    p.AverageCost,
                    s.Quantity,
                    LastSale = (DateOnly?)sale.LastSale,
                })
            .Where(r => r.LastSale == null || r.LastSale < since)
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

    /// <summary>Buyers in the period, best first. The contractor filter is ignored: every buyer is listed.</summary>
    public async Task<IReadOnlyList<CustomerSales>> GetCustomersAsync(
        AnalyticsFilter filter, int take = MaxRows, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await Lines(db, filter with { ContractorId = null })
            .GroupBy(l => new { l.Invoice!.ContractorId, l.Invoice.Contractor!.Name, l.Invoice.Contractor.Nip })
            .Select(g => new
            {
                g.Key.ContractorId,
                g.Key.Name,
                g.Key.Nip,
                Invoices = g.Where(l => l.Invoice!.Kind == InvoiceKind.Vat).Select(l => l.InvoiceId).Distinct().Count(),
                Net = g.Sum(l => l.NetValue),
                Cost = g.Sum(l => l.Quantity * l.UnitCost),
                Last = g.Max(l => l.Invoice!.IssueDate),
            })
            .OrderByDescending(r => r.Net).ThenBy(r => r.ContractorId)
            .Take(Math.Clamp(take, 1, MaxRows))
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CustomerSales(r.ContractorId, r.Name, r.Nip, r.Invoices, r.Net, RoundMoney(r.Cost), r.Last)).ToList();
    }

    /// <summary>The same filters over three periods: the chosen one, the one before it, a year earlier.</summary>
    public async Task<PeriodComparison> ComparePeriodsAsync(AnalyticsFilter filter, CancellationToken cancellationToken = default)
    {
        var previous = PlanningRules.PreviousPeriod(filter.From, filter.To);
        var yearEarlier = PlanningRules.YearEarlier(filter.From, filter.To);

        return new PeriodComparison(
            new PeriodSummary(filter.From, filter.To, await GetSummaryAsync(filter, cancellationToken)),
            new PeriodSummary(previous.From, previous.To, await GetSummaryAsync(filter with { From = previous.From, To = previous.To }, cancellationToken)),
            new PeriodSummary(yearEarlier.From, yearEarlier.To, await GetSummaryAsync(filter with { From = yearEarlier.From, To = yearEarlier.To }, cancellationToken)));
    }

    /// <summary>
    /// Goods to order (SPEC 5.16): the average daily sales over the last <paramref name="historyDays"/>
    /// days times the horizon, minus what is available for sale now (unexpired stock less
    /// reservations). Only goods that sell and need ordering, the shortest cover first.
    /// </summary>
    public async Task<IReadOnlyList<ForecastRow>> GetForecastAsync(
        int historyDays, int horizonDays, int? categoryId = null, int take = MaxRows, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        if (PlanningRules.ValidateDays(historyDays, horizonDays) is { } error)
        {
            throw new ArgumentOutOfRangeException(nameof(historyDays), error);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var today = Today;
        var history = new AnalyticsFilter(today.AddDays(1 - historyDays), today, categoryId);

        var sold = await Lines(db, history)
            .Where(l => l.Product!.Kind == ProductKind.Goods && l.Product.IsActive)
            .GroupBy(l => new { l.ProductId, l.Product!.Name, l.Product.Unit })
            .Select(g => new { g.Key.ProductId, g.Key.Name, g.Key.Unit, Quantity = g.Sum(l => l.Quantity) })
            .Where(r => r.Quantity > 0)
            .ToListAsync(cancellationToken);

        var ids = sold.Select(r => r.ProductId).ToArray();
        var stock = await Reservations.UnexpiredStockAsync(db, ids, today, cancellationToken);
        var reserved = await Reservations.ReservedAsync(db, ids, exceptOrderId: null, cancellationToken);

        return sold
            .Select(r =>
            {
                var daily = PlanningRules.DailyAverage(r.Quantity, historyDays);
                var available = stock.GetValueOrDefault(r.ProductId) - reserved.GetValueOrDefault(r.ProductId);
                return new ForecastRow(
                    r.ProductId, r.Name, r.Unit, r.Quantity, decimal.Round(daily, 3, MidpointRounding.AwayFromZero), available,
                    PlanningRules.DaysOfCover(available, daily), PlanningRules.SuggestedOrder(daily, horizonDays, available, r.Unit));
            })
            .Where(r => r.SuggestedOrder > 0)
            .OrderBy(r => r.DaysOfCover ?? 0).ThenBy(r => r.Name).ThenBy(r => r.ProductId)
            .Take(Math.Clamp(take, 1, MaxRows))
            .ToList();
    }

    private static IQueryable<InvoiceLine> Lines(RegalDbContext db, AnalyticsFilter filter)
    {
        var lines = db.InvoiceLines.AsNoTracking().Where(l =>
            l.Invoice!.Status != InvoiceStatus.Cancelled
            && l.Invoice.Kind != InvoiceKind.Proforma // corrections count: their lines are differences
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
