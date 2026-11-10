using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;
using Regal.Web.Features.Contractors;
using Regal.Web.Features.Products;
using Regal.Web.Features.Warehouse;

namespace Regal.Web.Features.Orders;

public sealed record OrderFilter(CustomerOrderStatus? Status, string? Term);

public sealed record OrderListItem(
    int Id,
    string Number,
    DateOnly OrderDate,
    string ContractorName,
    CustomerOrderStatus Status,
    int LineCount,
    int? InvoiceId);

/// <summary>
/// For an open order, Available is what this order could take now: unexpired stock minus what the
/// other open orders reserve. Null for services and for orders that no longer reserve anything.
/// </summary>
public sealed record OrderLineRow(int ProductId, string ProductName, string Unit, ProductKind Kind, decimal Quantity, decimal? Available)
{
    public decimal Missing => Available is { } available && available < Quantity ? Quantity - available : 0;
}

public sealed record OrderDetails(
    int Id,
    string Number,
    CustomerOrderStatus Status,
    DateOnly OrderDate,
    DateTime CreatedAt,
    ContractorListItem Contractor,
    string UserEmail,
    string? Notes,
    int? InvoiceId,
    string? InvoiceNumber,
    IReadOnlyList<OrderLineRow> Lines);

/// <summary>Customer orders (ZK) with reservation of goods; the rules are in SPEC 5.11.</summary>
public sealed class CustomerOrderService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private const string RequestIdIndex = "IX_CustomerOrders_RequestId";

    public async Task<SaveResult> CreateAsync(OrderDraft draft, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        if (OrderRules.Validate(draft) is { } error)
        {
            return SaveResult.Fail(error);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await CreateCoreAsync(db, draft, userId, cancellationToken);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (DbUpdateException exception) when (DbErrors.UniqueViolation(exception) == RequestIdIndex)
        {
            // Two sends of an order with only services: no product lock put them in order.
            db.ChangeTracker.Clear();
            return SaveResult.Ok((await FindByRequestIdAsync(db, draft.RequestId, cancellationToken))!.Value);
        }
    }

    /// <summary>Releases the reservation of an open order. Cancelling it again changes nothing.</summary>
    public async Task<SaveResult> CancelAsync(int id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Only this row: an invoice from the order locks it first too, so the two go one after the other.
        var order = (await db.CustomerOrders
            .FromSql($"""SELECT * FROM "CustomerOrders" WHERE "Id" = {id} FOR UPDATE""")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

        switch (order?.Status)
        {
            case null:
                return SaveResult.Fail("Nie znaleziono zamówienia.");
            case CustomerOrderStatus.Cancelled:
                return SaveResult.Ok(id);
            case CustomerOrderStatus.Completed:
                return SaveResult.Fail($"Zamówienie {order.Number} jest zrealizowane, więc nie można go anulować. Cofnięcie sprzedaży to anulowanie faktury.");
        }

        order.Status = CustomerOrderStatus.Cancelled;
        AuditTrail.AddOrder(db, AuditActions.OrderCancelled, id, userId, $"number={order.Number}", timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SaveResult.Ok(id);
    }

    public async Task<PagedResult<OrderListItem>> SearchAsync(OrderFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.CustomerOrders.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        if (SearchPattern.Contains(filter.Term) is { } pattern)
        {
            query = query.Where(o =>
                EF.Functions.ILike(o.Number, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(o.Contractor!.Name, pattern, SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(o => new OrderListItem(o.Id, o.Number, o.OrderDate, o.Contractor!.Name, o.Status, o.Lines.Count, o.InvoiceId))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderListItem>(items, total);
    }

    public async Task<OrderDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var order = await db.CustomerOrders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.OrderDate,
                o.CreatedAt,
                Contractor = new ContractorListItem(
                    o.Contractor!.Id, o.Contractor.Name, o.Contractor.Nip, o.Contractor.Address, o.Contractor.Email, o.Contractor.Phone, o.Contractor.IsActive),
                UserEmail = o.User!.Email,
                o.Notes,
                o.InvoiceId,
                InvoiceNumber = o.Invoice!.Number,
                Lines = o.Lines.OrderBy(l => l.Id)
                    .Select(l => new { l.ProductId, l.Product!.Name, l.Product.Unit, l.Product.Kind, l.Quantity })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        Dictionary<int, decimal>? available = null;
        if (order.Status == CustomerOrderStatus.Open)
        {
            var goods = order.Lines.Where(l => l.Kind == ProductKind.Goods).Select(l => l.ProductId).ToArray();
            var stock = await Reservations.UnexpiredStockAsync(db, goods, WarehouseTime.DateOf(timeProvider.GetUtcNow()), cancellationToken);
            var reservedByOthers = await Reservations.ReservedAsync(db, goods, order.Id, cancellationToken);
            available = goods.ToDictionary(p => p, p => Math.Max(0, stock.GetValueOrDefault(p) - reservedByOthers.GetValueOrDefault(p)));
        }

        var lines = order.Lines
            .Select(l => new OrderLineRow(
                l.ProductId, l.Name, l.Unit, l.Kind, WithoutTrailingZeros(l.Quantity),
                available is not null && available.TryGetValue(l.ProductId, out var free) ? free : null))
            .ToList();

        return new OrderDetails(
            order.Id, order.Number, order.Status, order.OrderDate, order.CreatedAt, order.Contractor, order.UserEmail,
            order.Notes, order.InvoiceId, order.InvoiceNumber, lines);
    }

    /// <summary>What an invoice from this order starts with: the buyer and the lines at catalog prices.</summary>
    public async Task<(OrderDetails Order, IReadOnlyList<ProductPick> Products)?> GetForInvoiceAsync(int id, CancellationToken cancellationToken = default)
    {
        var details = await GetDetailsAsync(id, cancellationToken);
        if (details is null)
        {
            return null;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = details.Lines.Select(l => l.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new ProductPick(p.Id, p.Sku, p.Ean, p.Name, p.Unit, p.HasExpiry, p.AverageCost, p.IsActive, p.Kind, p.NetPrice, p.VatRate))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return (details, details.Lines.Select(l => products[l.ProductId]).ToList());
    }

    private async Task<SaveResult> CreateCoreAsync(RegalDbContext db, OrderDraft draft, int userId, CancellationToken cancellationToken)
    {
        // The same lock as stock documents: a reservation and an issue of one product go one at a time,
        // and everything read below includes what the previous holder committed.
        var products = (await ProductLocks.LockAsync(db, draft.Lines.Select(line => line.ProductId), cancellationToken)).ToDictionary(p => p.Id);

        if (await FindByRequestIdAsync(db, draft.RequestId, cancellationToken) is { } existingId)
        {
            return SaveResult.Ok(existingId);
        }

        foreach (var line in draft.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                return SaveResult.Fail("Nie znaleziono produktu.");
            }

            if (!product.IsActive)
            {
                return SaveResult.Fail($"{product.Name}: produkt jest nieaktywny.");
            }

            if (StockDraftValidator.ValidateQuantity(line.Quantity, product.Unit) is { } quantityError)
            {
                return SaveResult.Fail($"{product.Name}: {quantityError}");
            }
        }

        var contractorActive = await db.Contractors.AsNoTracking()
            .Where(c => c.Id == draft.ContractorId)
            .Select(c => (bool?)c.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
        if (contractorActive is not true)
        {
            return SaveResult.Fail(contractorActive is null ? "Nie znaleziono kontrahenta." : "Wybrany kontrahent jest nieaktywny.");
        }

        var now = timeProvider.GetUtcNow();
        var date = WarehouseTime.DateOf(now);
        var goods = products.Values.Where(p => p.Kind == ProductKind.Goods).Select(p => p.Id).ToArray();
        var stock = await Reservations.UnexpiredStockAsync(db, goods, date, cancellationToken);
        var reserved = await Reservations.ReservedAsync(db, goods, exceptOrderId: null, cancellationToken);

        foreach (var line in draft.Lines.Where(l => goods.Contains(l.ProductId)))
        {
            var product = products[line.ProductId];
            var free = Math.Max(0, stock.GetValueOrDefault(product.Id) - reserved.GetValueOrDefault(product.Id));
            if (line.Quantity > free)
            {
                return SaveResult.Fail(
                    $"{product.Name}: można zarezerwować najwyżej {StockLabels.Quantity(free)} {product.Unit} " +
                    $"(stan bez przeterminowanych partii {StockLabels.Quantity(stock.GetValueOrDefault(product.Id))}, " +
                    $"zarezerwowane {StockLabels.Quantity(reserved.GetValueOrDefault(product.Id))}).");
            }
        }

        // ToListAsync, because anything composed on top would wrap the INSERT in a subquery.
        var numbers = await db.Database.SqlQuery<int>($"""
            INSERT INTO "OrderCounters" ("Year", "LastNumber")
            VALUES ({date.Year}, 1)
            ON CONFLICT ("Year") DO UPDATE SET "LastNumber" = "OrderCounters"."LastNumber" + 1
            RETURNING "LastNumber" AS "Value"
            """).ToListAsync(cancellationToken);

        var notes = draft.Notes?.Trim();
        var order = new CustomerOrder
        {
            Number = OrderRules.Number(numbers.Single(), date.Year),
            Status = CustomerOrderStatus.Open,
            ContractorId = draft.ContractorId!.Value,
            UserId = userId,
            CreatedAt = now.UtcDateTime,
            OrderDate = date,
            Notes = string.IsNullOrEmpty(notes) ? null : notes,
            RequestId = draft.RequestId,
            Lines = draft.Lines.Select(l => new CustomerOrderLine { ProductId = l.ProductId, Quantity = l.Quantity }).ToList(),
        };

        db.CustomerOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        AuditTrail.AddOrder(db, AuditActions.OrderCreated, order.Id, userId, $"number={order.Number}", timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        return SaveResult.Ok(order.Id);
    }

    /// <summary>numeric(18,3) comes back as 8.000; an invoice field would then show "8,000" for pieces.</summary>
    private static decimal WithoutTrailingZeros(decimal value) => value / 1.000000000000000000000000000000000m;

    private static async Task<int?> FindByRequestIdAsync(RegalDbContext db, Guid requestId, CancellationToken cancellationToken) =>
        await db.CustomerOrders.AsNoTracking()
            .Where(o => o.RequestId == requestId)
            .Select(o => (int?)o.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
