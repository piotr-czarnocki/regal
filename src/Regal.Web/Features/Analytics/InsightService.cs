using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Warehouse;

namespace Regal.Web.Features.Analytics;

/// <summary>One product in the ranking: the chosen period beside the one just before it (SPEC 5.20).</summary>
public sealed record ProductRankingRow(
    int ProductId,
    string Name,
    string CategoryName,
    string Unit,
    ProductKind Kind,
    decimal Quantity,
    decimal NetSales,
    decimal Cost,
    int Invoices,
    int Customers,
    decimal PreviousNetSales,
    decimal SharePercent,
    AbcClass Class,
    decimal? Available,
    decimal? DaysOfCover)
{
    public decimal Margin => NetSales - Cost;

    public decimal? MarginPercent => NetSales == 0 ? null : decimal.Round(Margin * 100 / NetSales, 1, MidpointRounding.AwayFromZero);

    /// <summary>Net sales per unit, after discounts and corrections.</summary>
    public decimal? AveragePrice => Quantity <= 0 ? null : decimal.Round(NetSales / Quantity, 2, MidpointRounding.AwayFromZero);

    public decimal? ChangePercent => PlanningRules.ChangePercent(NetSales, PreviousNetSales);
}

public sealed record ProductHeader(
    int Id, string Name, string Sku, string? Ean, string CategoryName, string Unit, ProductKind Kind, decimal NetPrice, decimal AverageCost, bool IsActive);

/// <summary>A product's sales in the period. Sold quantity comes from invoices, not from WZ documents.</summary>
public sealed record ProductSalesFigures(
    decimal Quantity,
    decimal NetSales,
    decimal Cost,
    int Invoices,
    int Customers,
    decimal? AverageDiscountPercent,
    decimal ReturnedQuantity,
    decimal PreviousNetSales,
    DateOnly? FirstSale,
    DateOnly? LastSale)
{
    public decimal Margin => NetSales - Cost;

    public decimal? MarginPercent => NetSales == 0 ? null : decimal.Round(Margin * 100 / NetSales, 1, MidpointRounding.AwayFromZero);

    public decimal? AveragePrice => Quantity <= 0 ? null : decimal.Round(NetSales / Quantity, 2, MidpointRounding.AwayFromZero);

    public decimal? ChangePercent => PlanningRules.ChangePercent(NetSales, PreviousNetSales);
}

/// <summary>Stock now; days of cover at the pace of the chosen period.</summary>
public sealed record ProductStockFigures(decimal OnHand, decimal Unexpired, decimal Reserved, decimal? DaysOfCover, DateOnly? LastReceipt)
{
    public decimal Available => Unexpired - Reserved;
}

public sealed record BuyerRow(int ContractorId, string Name, decimal Quantity, decimal NetSales, int Invoices);

/// <summary>Movements of one kind in the period, net of reversals and returns.</summary>
public sealed record MovementRow(string Kind, decimal Quantity, decimal Value);

public sealed record ReceiptRow(int DocumentId, string Number, DateOnly Date, string? Supplier, decimal Quantity, decimal? UnitCost, bool Reversed);

public sealed record ProductCard(
    ProductHeader Product,
    ProductSalesFigures Sales,
    ProductStockFigures? Stock,
    IReadOnlyList<BuyerRow> TopCustomers,
    IReadOnlyList<MovementRow> Movements,
    IReadOnlyList<ReceiptRow> Receipts);

public sealed record ContractorHeader(int Id, string Name, string? Nip, string? Address, string? Email, string? Phone, bool IsActive);

public sealed record CustomerFigures(
    decimal NetSales,
    decimal Cost,
    int Invoices,
    decimal PreviousNetSales,
    int Corrections,
    decimal CorrectionsNet,
    DateOnly? FirstPurchase,
    DateOnly? LastPurchase,
    int? DaysSinceLastPurchase,
    decimal? AverageDaysBetweenPurchases)
{
    public decimal Margin => NetSales - Cost;

    public decimal? MarginPercent => NetSales == 0 ? null : decimal.Round(Margin * 100 / NetSales, 1, MidpointRounding.AwayFromZero);

    public decimal AverageInvoice => Invoices == 0 ? 0 : decimal.Round(NetSales / Invoices, 2, MidpointRounding.AwayFromZero);

    public decimal? ChangePercent => PlanningRules.ChangePercent(NetSales, PreviousNetSales);
}

/// <summary>
/// Payment habits: receivables now, and how fast the paid invoices were paid. Only invoices
/// Fakturownia reported as paid with a date count; an unknown state is normal and left out.
/// </summary>
public sealed record PaymentFigures(ReceivablesSummary Receivables, int PaidInvoices, decimal? AverageDaysToPay, decimal? PaidOnTimePercent);

public sealed record OrderFigures(int Open, int Completed, int Cancelled);

public sealed record SuppliedProductRow(int ProductId, string Name, string Unit, decimal Quantity, decimal Value);

/// <summary>Deliveries (PZ) in the period; a reversed delivery cancels its original.</summary>
public sealed record SupplierFigures(int Deliveries, decimal Value, int Products, DateOnly? LastDelivery, IReadOnlyList<SuppliedProductRow> TopProducts);

public sealed record ContractorCard(
    ContractorHeader Contractor,
    CustomerFigures Customer,
    PaymentFigures Payments,
    OrderFigures Orders,
    SupplierFigures Supplier);

public sealed record SupplierRow(int ContractorId, string Name, string? Nip, int Deliveries, decimal Value, int Products, DateOnly? LastDelivery);

/// <summary>
/// Product and contractor analysis (SPEC 5.20). Every sales figure goes through AnalyticsService.Lines,
/// so a product's or a buyer's total is the same number as in the other analyses.
/// </summary>
public sealed class InsightService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public const int MaxRows = 500;
    private const int TopRows = 10;

    public DateOnly Today => WarehouseTime.DateOf(timeProvider.GetUtcNow());

    /// <summary>
    /// Every product sold in the period or the one before it, best first. One grouped pass over the
    /// lines and two over stock, whatever the number of rows.
    /// </summary>
    public async Task<IReadOnlyList<ProductRankingRow>> GetProductRankingAsync(
        AnalyticsFilter filter, int take = MaxRows, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var (previousFrom, previousTo) = PlanningRules.PreviousPeriod(filter.From, filter.To);

        // Plain sums per product, one query per period, and the distinct counts apart. Conditional or
        // filtered aggregates in one grouping became a correlated subquery per column and product:
        // 1.6 s on a year of data (docs/analiza-dynamiczna.md). Names are read for the kept rows only.
        var current = await AnalyticsService.Lines(db, filter)
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(l => l.Quantity), Net = g.Sum(l => l.NetValue), Cost = g.Sum(l => l.Quantity * l.UnitCost) })
            .ToDictionaryAsync(r => r.ProductId, cancellationToken);
        var previous = await AnalyticsService.Lines(db, filter with { From = previousFrom, To = previousTo })
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Net = g.Sum(l => l.NetValue) })
            .ToDictionaryAsync(r => r.ProductId, r => r.Net, cancellationToken);
        var grouped = current.Keys.Union(previous.Keys)
            .Select(id => new
            {
                ProductId = id,
                Quantity = current.GetValueOrDefault(id)?.Quantity ?? 0,
                Net = current.GetValueOrDefault(id)?.Net ?? 0,
                Cost = current.GetValueOrDefault(id)?.Cost ?? 0,
                Previous = previous.GetValueOrDefault(id),
            })
            .ToList();

        // Distinct counts apart: inside the grouping above they turned every column back into a subquery.
        var sales = AnalyticsService.Lines(db, filter).Where(l => l.Invoice!.Kind == InvoiceKind.Vat);
        var invoices = await sales.Select(l => new { l.ProductId, l.InvoiceId }).Distinct()
            .GroupBy(l => l.ProductId).Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.ProductId, r => r.Count, cancellationToken);
        var customers = await sales.Select(l => new { l.ProductId, l.Invoice!.ContractorId }).Distinct()
            .GroupBy(l => l.ProductId).Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.ProductId, r => r.Count, cancellationToken);

        // Classes over every product, before the list is cut.
        var classes = AbcClassifier.Classify(grouped.Select(r => new AbcItem(r.ProductId, string.Empty, r.Net))).ToDictionary(r => r.ProductId);
        var top = grouped.OrderByDescending(r => r.Net).ThenBy(r => r.ProductId).Take(Math.Clamp(take, 1, MaxRows)).ToList();
        var ids = top.Select(r => r.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, Category = p.Category!.Name, p.Unit, p.Kind })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var goods = top.Where(r => products[r.ProductId].Kind == ProductKind.Goods).Select(r => r.ProductId).ToArray();
        var today = Today;
        var unexpired = await Reservations.UnexpiredStockAsync(db, goods, today, cancellationToken);
        var reserved = await Reservations.ReservedAsync(db, goods, exceptOrderId: null, cancellationToken);
        var days = filter.To.DayNumber - filter.From.DayNumber + 1;

        return top.Select(r =>
        {
            var product = products[r.ProductId];
            decimal? available = product.Kind == ProductKind.Goods ? unexpired.GetValueOrDefault(r.ProductId) - reserved.GetValueOrDefault(r.ProductId) : null;
            var cover = available is { } value ? PlanningRules.DaysOfCover(value, PlanningRules.DailyAverage(r.Quantity, days)) : null;
            var abc = classes[r.ProductId];

            return new ProductRankingRow(
                r.ProductId, product.Name, product.Category, product.Unit, product.Kind, r.Quantity, r.Net, RoundMoney(r.Cost),
                invoices.GetValueOrDefault(r.ProductId), customers.GetValueOrDefault(r.ProductId), r.Previous, abc.Share, abc.Class, available, cover);
        }).ToList();
    }

    public async Task<ProductCard?> GetProductCardAsync(int productId, AnalyticsFilter filter, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var product = await db.Products.AsNoTracking().Where(p => p.Id == productId)
            .Select(p => new ProductHeader(p.Id, p.Name, p.Sku, p.Ean, p.Category!.Name, p.Unit, p.Kind, p.NetPrice, p.AverageCost, p.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
        {
            return null;
        }

        var from = filter.From;
        var (previousFrom, previousTo) = PlanningRules.PreviousPeriod(filter.From, filter.To);
        var mine = filter with { ProductId = productId, ContractorId = null, CategoryId = null };

        var totals = await AnalyticsService.Lines(db, mine with { From = previousFrom })
            .Select(l => new
            {
                l.InvoiceId,
                IsVat = l.Invoice!.Kind == InvoiceKind.Vat,
                l.Invoice.ContractorId,
                Date = l.Invoice.IssueDate,
                l.Quantity,
                l.NetValue,
                Cost = l.Quantity * l.UnitCost,
                l.DiscountPercent,
            })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Quantity = g.Where(l => l.Date >= from).Sum(l => l.Quantity),
                Net = g.Where(l => l.Date >= from).Sum(l => l.NetValue),
                Cost = g.Where(l => l.Date >= from).Sum(l => l.Cost),
                Invoices = g.Where(l => l.IsVat && l.Date >= from).Select(l => l.InvoiceId).Distinct().Count(),
                Customers = g.Where(l => l.IsVat && l.Date >= from).Select(l => l.ContractorId).Distinct().Count(),

                // Discount weighted by quantity, over sales only: a correction line holds a difference.
                SoldOnInvoices = g.Where(l => l.IsVat && l.Date >= from).Sum(l => l.Quantity),
                DiscountTimesQuantity = g.Where(l => l.IsVat && l.Date >= from).Sum(l => l.Quantity * l.DiscountPercent),
                Returned = g.Where(l => !l.IsVat && l.Date >= from && l.Quantity < 0).Sum(l => l.Quantity),
                Previous = g.Where(l => l.Date <= previousTo).Sum(l => l.NetValue),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var ever = await AnalyticsService.Lines(db, new AnalyticsFilter(DateOnly.MinValue, DateOnly.MaxValue, ProductId: productId))
            .Where(l => l.Invoice!.Kind == InvoiceKind.Vat)
            .GroupBy(_ => 1)
            .Select(g => new { First = g.Min(l => l.Invoice!.IssueDate), Last = g.Max(l => l.Invoice!.IssueDate) })
            .SingleOrDefaultAsync(cancellationToken);

        var sales = new ProductSalesFigures(
            totals?.Quantity ?? 0,
            totals?.Net ?? 0,
            RoundMoney(totals?.Cost ?? 0),
            totals?.Invoices ?? 0,
            totals?.Customers ?? 0,
            totals is { SoldOnInvoices: > 0 } t ? decimal.Round(t.DiscountTimesQuantity / t.SoldOnInvoices, 1, MidpointRounding.AwayFromZero) : null,
            -(totals?.Returned ?? 0),
            totals?.Previous ?? 0,
            ever?.First,
            ever?.Last);

        var buyers = await AnalyticsService.Lines(db, mine)
            .GroupBy(l => new { l.Invoice!.ContractorId, l.Invoice.Contractor!.Name })
            .Select(g => new
            {
                g.Key.ContractorId,
                g.Key.Name,
                Quantity = g.Sum(l => l.Quantity),
                Net = g.Sum(l => l.NetValue),
                Invoices = g.Where(l => l.Invoice!.Kind == InvoiceKind.Vat).Select(l => l.InvoiceId).Distinct().Count(),
            })
            .OrderByDescending(b => b.Net).ThenBy(b => b.ContractorId)
            .Take(TopRows)
            .ToListAsync(cancellationToken);
        var topCustomers = buyers.Select(b => new BuyerRow(b.ContractorId, b.Name, b.Quantity, b.Net, b.Invoices)).ToList();

        if (product.Kind != ProductKind.Goods)
        {
            return new ProductCard(product, sales, null, topCustomers, [], []);
        }

        var stock = await StockOfAsync(db, productId, filter, Today, sales.Quantity, cancellationToken);
        var movements = await MovementsAsync(db, productId, filter, cancellationToken);
        var receipts = await ReceiptsAsync(db, productId, cancellationToken);
        return new ProductCard(product, sales, stock with { LastReceipt = receipts.FirstOrDefault(r => !r.Reversed)?.Date }, topCustomers, movements, receipts);
    }

    public async Task<ContractorCard?> GetContractorCardAsync(int contractorId, AnalyticsFilter filter, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var contractor = await db.Contractors.AsNoTracking().Where(c => c.Id == contractorId)
            .Select(c => new ContractorHeader(c.Id, c.Name, c.Nip, c.Address, c.Email, c.Phone, c.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
        if (contractor is null)
        {
            return null;
        }

        var today = Today;
        var customer = await CustomerFiguresAsync(db, contractorId, filter, today, cancellationToken);
        var payments = await PaymentFiguresAsync(db, contractorId, today, cancellationToken);

        var orders = await db.CustomerOrders.AsNoTracking().Where(o => o.ContractorId == contractorId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        var supplier = await SupplierFiguresAsync(db, contractorId, filter, cancellationToken);

        return new ContractorCard(
            contractor,
            customer,
            payments,
            new OrderFigures(
                orders.GetValueOrDefault(CustomerOrderStatus.Open),
                orders.GetValueOrDefault(CustomerOrderStatus.Completed),
                orders.GetValueOrDefault(CustomerOrderStatus.Cancelled)),
            supplier);
    }

    /// <summary>Suppliers by the value of their deliveries in the period, net of reversed deliveries.</summary>
    public async Task<IReadOnlyList<SupplierRow>> GetSuppliersAsync(AnalyticsFilter filter, int take = MaxRows, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await Deliveries(db, filter)
            .Where(m => m.ContractorId != null)
            .GroupBy(m => new { m.ContractorId, m.ContractorName, m.Nip })
            .Select(g => new
            {
                g.Key.ContractorId,
                g.Key.ContractorName,
                g.Key.Nip,
                Value = g.Sum(m => m.Value),
                Originals = g.Where(m => !m.IsReversal).Select(m => m.DocumentId).Distinct().Count(),
                Reversals = g.Where(m => m.IsReversal).Select(m => m.DocumentId).Distinct().Count(),
                Products = g.Select(m => m.ProductId).Distinct().Count(),
                Last = g.Where(m => !m.IsReversal).Max(m => (DateOnly?)m.Date),
            })
            .OrderByDescending(r => r.Value).ThenBy(r => r.ContractorId)
            .Take(Math.Clamp(take, 1, MaxRows))
            .ToListAsync(cancellationToken);

        return rows.Select(r => new SupplierRow(r.ContractorId!.Value, r.ContractorName!, r.Nip, r.Originals - r.Reversals, RoundMoney(r.Value), r.Products, r.Last)).ToList();
    }

    /// <summary>Set with an initializer, not a constructor: EF can then filter and group on its members.</summary>
    private sealed class DeliveryMovement
    {
        public int DocumentId { get; init; }

        public int? ContractorId { get; init; }

        public string? ContractorName { get; init; }

        public string? Nip { get; init; }

        public bool IsReversal { get; init; }

        public DateOnly Date { get; init; }

        public int ProductId { get; init; }

        public decimal Quantity { get; init; }

        public decimal Value { get; init; }
    }

    /// <summary>Movements of PZ documents in the period. A reversal is a PZ of the same supplier with negated movements.</summary>
    private static IQueryable<DeliveryMovement> Deliveries(RegalDbContext db, AnalyticsFilter filter) =>
        db.StockMovements.AsNoTracking()
            .Where(m => m.Document!.Type == StockDocumentType.PZ && m.Document.DocumentDate >= filter.From && m.Document.DocumentDate <= filter.To)
            .Select(m => new DeliveryMovement
            {
                DocumentId = m.DocumentId,
                ContractorId = m.Document!.ContractorId,
                ContractorName = m.Document.Contractor!.Name,
                Nip = m.Document.Contractor.Nip,
                IsReversal = m.Document.ReversesDocumentId != null,
                Date = m.Document.DocumentDate,
                ProductId = m.Batch!.ProductId,
                Quantity = m.QuantityChange,
                Value = m.QuantityChange * m.UnitCost,
            });

    private static async Task<ProductStockFigures> StockOfAsync(
        RegalDbContext db, int productId, AnalyticsFilter filter, DateOnly today, decimal soldInPeriod, CancellationToken cancellationToken)
    {
        var onHand = await db.Stock.AsNoTracking().Where(s => s.Batch!.ProductId == productId).SumAsync(s => s.Quantity, cancellationToken);
        var unexpired = (await Reservations.UnexpiredStockAsync(db, [productId], today, cancellationToken)).GetValueOrDefault(productId);
        var reserved = (await Reservations.ReservedAsync(db, [productId], exceptOrderId: null, cancellationToken)).GetValueOrDefault(productId);
        var days = filter.To.DayNumber - filter.From.DayNumber + 1;
        var cover = PlanningRules.DaysOfCover(unexpired - reserved, PlanningRules.DailyAverage(soldInPeriod, days));
        return new ProductStockFigures(onHand, unexpired, reserved, cover, null);
    }

    private static async Task<IReadOnlyList<MovementRow>> MovementsAsync(RegalDbContext db, int productId, AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        var groups = await db.StockMovements.AsNoTracking()
            .Where(m => m.Batch!.ProductId == productId && m.Document!.DocumentDate >= filter.From && m.Document.DocumentDate <= filter.To)
            .GroupBy(m => new { m.Document!.Type, FromStocktake = m.Document.StocktakeId != null })
            .Select(g => new { g.Key.Type, g.Key.FromStocktake, Quantity = g.Sum(m => m.QuantityChange), Value = g.Sum(m => m.QuantityChange * m.UnitCost) })
            .ToListAsync(cancellationToken);

        return groups
            .Select(g => (Order: MovementOrder(g.Type, g.FromStocktake), Row: new MovementRow(MovementKind(g.Type, g.FromStocktake), g.Quantity, RoundMoney(g.Value))))
            .OrderBy(r => r.Order)
            .Select(r => r.Row)
            .ToList();
    }

    private static async Task<IReadOnlyList<ReceiptRow>> ReceiptsAsync(RegalDbContext db, int productId, CancellationToken cancellationToken)
    {
        var rows = await db.StockDocuments.AsNoTracking()
            .Where(d => (d.Type == StockDocumentType.PZ || (d.Type == StockDocumentType.PW && d.StocktakeId == null))
                && d.ReversesDocumentId == null
                && d.Movements.Any(m => m.Batch!.ProductId == productId))
            .OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.Id)
            .Take(TopRows)
            .Select(d => new
            {
                d.Id,
                d.Number,
                d.DocumentDate,
                Supplier = d.Contractor!.Name,
                Quantity = d.Movements.Where(m => m.Batch!.ProductId == productId).Sum(m => m.QuantityChange),
                Value = d.Movements.Where(m => m.Batch!.ProductId == productId).Sum(m => m.QuantityChange * m.UnitCost),
                Reversed = db.StockDocuments.Any(r => r.ReversesDocumentId == d.Id),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ReceiptRow(
            r.Id, r.Number, r.DocumentDate, r.Supplier, r.Quantity,
            r.Quantity == 0 ? null : decimal.Round(r.Value / r.Quantity, 4, MidpointRounding.AwayFromZero), r.Reversed)).ToList();
    }

    private static async Task<CustomerFigures> CustomerFiguresAsync(
        RegalDbContext db, int contractorId, AnalyticsFilter filter, DateOnly today, CancellationToken cancellationToken)
    {
        var from = filter.From;
        var (previousFrom, previousTo) = PlanningRules.PreviousPeriod(filter.From, filter.To);
        var mine = filter with { ContractorId = contractorId, CategoryId = null, ProductId = null };

        var totals = await AnalyticsService.Lines(db, mine with { From = previousFrom })
            .Select(l => new { l.InvoiceId, l.Invoice!.Kind, Date = l.Invoice.IssueDate, l.NetValue, Cost = l.Quantity * l.UnitCost })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Net = g.Where(l => l.Date >= from).Sum(l => l.NetValue),
                Cost = g.Where(l => l.Date >= from).Sum(l => l.Cost),
                Invoices = g.Where(l => l.Kind == InvoiceKind.Vat && l.Date >= from).Select(l => l.InvoiceId).Distinct().Count(),
                Corrections = g.Where(l => l.Kind == InvoiceKind.Correction && l.Date >= from).Select(l => l.InvoiceId).Distinct().Count(),
                CorrectionsNet = g.Where(l => l.Kind == InvoiceKind.Correction && l.Date >= from).Sum(l => l.NetValue),
                Previous = g.Where(l => l.Date <= previousTo).Sum(l => l.NetValue),
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Purchases are VAT invoices that were not cancelled: the same invoices Lines counts.
        var purchases = db.Invoices.AsNoTracking()
            .Where(i => i.ContractorId == contractorId && i.Kind == InvoiceKind.Vat && i.Status != InvoiceStatus.Cancelled);
        var ever = await purchases.GroupBy(_ => 1)
            .Select(g => new { First = g.Min(i => i.IssueDate), Last = g.Max(i => i.IssueDate) })
            .SingleOrDefaultAsync(cancellationToken);
        var days = await purchases.Where(i => i.IssueDate >= filter.From && i.IssueDate <= filter.To)
            .Select(i => i.IssueDate).Distinct().OrderBy(d => d).ToListAsync(cancellationToken);

        return new CustomerFigures(
            totals?.Net ?? 0,
            RoundMoney(totals?.Cost ?? 0),
            totals?.Invoices ?? 0,
            totals?.Previous ?? 0,
            totals?.Corrections ?? 0,
            totals?.CorrectionsNet ?? 0,
            ever?.First,
            ever?.Last,
            ever is null ? null : today.DayNumber - ever.Last.DayNumber,
            InsightRules.AverageGapDays(days));
    }

    private static async Task<PaymentFigures> PaymentFiguresAsync(RegalDbContext db, int contractorId, DateOnly today, CancellationToken cancellationToken)
    {
        var invoices = db.Invoices.Where(i => i.ContractorId == contractorId);
        var receivables = await InvoicePaymentService.SummarizeReceivablesAsync(invoices, today, cancellationToken);
        var paid = await invoices.AsNoTracking()
            .Where(i => i.Kind == InvoiceKind.Vat && i.Status == InvoiceStatus.Sent && i.PaymentStatus == InvoicePaymentStatus.Paid && i.PaidDate != null)
            .Select(i => new { i.IssueDate, i.PaymentDays, PaidDate = i.PaidDate!.Value })
            .ToListAsync(cancellationToken);

        var (average, onTime) = InsightRules.PaymentHabits(paid.Select(p => (p.IssueDate, p.PaymentDays, p.PaidDate)).ToList());
        return new PaymentFigures(receivables, paid.Count, average, onTime);
    }

    private static async Task<SupplierFigures> SupplierFiguresAsync(RegalDbContext db, int contractorId, AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        var mine = Deliveries(db, filter).Where(m => m.ContractorId == contractorId);
        var totals = await mine.GroupBy(_ => 1)
            .Select(g => new
            {
                Value = g.Sum(m => m.Value),
                Originals = g.Where(m => !m.IsReversal).Select(m => m.DocumentId).Distinct().Count(),
                Reversals = g.Where(m => m.IsReversal).Select(m => m.DocumentId).Distinct().Count(),
                Products = g.Select(m => m.ProductId).Distinct().Count(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var products = await mine
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(m => m.Quantity), Value = g.Sum(m => m.Value) })
            .OrderByDescending(r => r.Value).ThenBy(r => r.ProductId)
            .Take(TopRows)
            .ToListAsync(cancellationToken);
        var ids = products.Select(p => p.ProductId).ToArray();
        var names = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => (p.Name, p.Unit), cancellationToken);

        var last = await db.StockDocuments.AsNoTracking()
            .Where(d => d.Type == StockDocumentType.PZ && d.ContractorId == contractorId && d.ReversesDocumentId == null
                && !db.StockDocuments.Any(r => r.ReversesDocumentId == d.Id))
            .MaxAsync(d => (DateOnly?)d.DocumentDate, cancellationToken);

        return new SupplierFigures(
            (totals?.Originals ?? 0) - (totals?.Reversals ?? 0),
            RoundMoney(totals?.Value ?? 0),
            totals?.Products ?? 0,
            last,
            products.Select(p => new SuppliedProductRow(p.ProductId, names[p.ProductId].Name, names[p.ProductId].Unit, p.Quantity, RoundMoney(p.Value))).ToList());
    }

    private static int MovementOrder(StockDocumentType type, bool fromStocktake) => (type, fromStocktake) switch
    {
        (StockDocumentType.PZ, _) => 0,
        (StockDocumentType.PW, false) => 1,
        (StockDocumentType.WZ, _) => 2,
        (StockDocumentType.RW, false) => 3,
        (StockDocumentType.PW, true) => 4,
        _ => 5,
    };

    private static string MovementKind(StockDocumentType type, bool fromStocktake) => (type, fromStocktake) switch
    {
        (StockDocumentType.PZ, _) => "Przyjęcia od dostawców (PZ)",
        (StockDocumentType.PW, false) => "Produkcja i przyjęcia wewnętrzne (PW)",
        (StockDocumentType.WZ, _) => "Wydania i sprzedaż (WZ)",
        (StockDocumentType.RW, false) => "Straty i zużycie (RW)",
        (StockDocumentType.PW, true) => "Nadwyżki ze spisu (PW)",
        _ => "Niedobory ze spisu (RW)",
    };

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
