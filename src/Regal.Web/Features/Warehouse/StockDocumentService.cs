using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;
using Regal.Web.Features.Products;

namespace Regal.Web.Features.Warehouse;

public sealed record StockDocumentFilter(StockDocumentType? Type, DateOnly? From, DateOnly? To, string? Term);

public sealed record StockDocumentListItem(
    int Id,
    StockDocumentType Type,
    string Number,
    DateOnly DocumentDate,
    string? ContractorName,
    int? ReversesDocumentId,
    int? ReversedByDocumentId);

public sealed record DocumentLink(int Id, string Number);

public sealed record StockMovementRow(
    int ProductId,
    string ProductName,
    string Unit,
    string BatchNumber,
    DateOnly? ExpiryDate,
    decimal QuantityChange,
    decimal UnitCost)
{
    public decimal Value => QuantityChange * UnitCost;
}

public sealed record StockDocumentDetails(
    int Id,
    StockDocumentType Type,
    string Number,
    DateOnly DocumentDate,
    DateTime CreatedAt,
    string? ContractorName,
    string UserEmail,
    string? Notes,
    DocumentLink? Reverses,
    DocumentLink? ReversedBy,
    IReadOnlyList<StockMovementRow> Movements);

public sealed record IssuePreviewLine(int ProductId, string ProductName, IReadOnlyList<BatchAllocation> Allocations, string? Error);

public sealed class StockDocumentService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private const string RequestIdIndex = "IX_StockDocuments_RequestId";
    private const string ReversesDocumentIndex = "IX_StockDocuments_ReversesDocumentId";
    private const string Savepoint = "stock_document";
    private const string AlreadyReversed = "Ten dokument ma już korektę.";

    /// <summary>Approves the draft in its own transaction.</summary>
    public async Task<SaveResult> PostAsync(StockDocumentDraft draft, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await InOwnTransactionAsync(db, () => PostCoreAsync(db, draft, userId, cancellationToken), draft.RequestId, cancellationToken);
    }

    /// <summary>
    /// Approves the draft inside the caller's open transaction: the caller commits. A failure undoes
    /// only this document, up to a savepoint. Call it before the transaction locks other products
    /// and only once per transaction, or the ascending lock order no longer prevents deadlocks.
    /// </summary>
    public async Task<SaveResult> PostInTransactionAsync(RegalDbContext db, StockDocumentDraft draft, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        return await InCallerTransactionAsync(db, () => PostCoreAsync(db, draft, userId, cancellationToken), draft.RequestId, cancellationToken);
    }

    /// <summary>The correction counterpart of PostInTransactionAsync, with the same rules for the caller.</summary>
    public async Task<SaveResult> ReverseInTransactionAsync(RegalDbContext db, int documentId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        return await InCallerTransactionAsync(db, () => ReverseCoreAsync(db, documentId, requestId, userId, cancellationToken), requestId, cancellationToken);
    }

    private static async Task<SaveResult> InCallerTransactionAsync(
        RegalDbContext db, Func<Task<SaveResult>> work, Guid requestId, CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("This method needs a transaction opened by the caller.");

        await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
        try
        {
            var result = await work();
            if (result.IsSuccess)
            {
                await transaction.ReleaseSavepointAsync(Savepoint, cancellationToken);
            }
            else
            {
                await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
            }

            return result;
        }
        catch (DbUpdateException exception) when (DbErrors.UniqueViolation(exception) is RequestIdIndex or ReversesDocumentIndex)
        {
            await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
            return DbErrors.UniqueViolation(exception) == RequestIdIndex
                ? SaveResult.Ok((await FindByRequestIdAsync(db, requestId, cancellationToken))!.Value)
                : SaveResult.Fail(AlreadyReversed);
        }
        finally
        {
            ForgetDocumentEntities(db);
        }
    }

    /// <summary>Posts the opposite of a whole document, on the same batches and at the same costs.</summary>
    public async Task<SaveResult> ReverseAsync(int documentId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await InOwnTransactionAsync(db, () => ReverseCoreAsync(db, documentId, requestId, userId, cancellationToken), requestId, cancellationToken);
    }

    /// <summary>What an issue would take right now. Holds no locks, so approval may still allocate differently.</summary>
    public async Task<IReadOnlyList<IssuePreviewLine>> PreviewIssueAsync(StockDocumentDraft draft, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var productIds = draft.Lines.Select(line => line.ProductId).Distinct().ToArray();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var available = await LoadAvailabilityAsync(db, productIds, cancellationToken);
        var reserved = await LoadReservedForSaleAsync(db, draft, productIds, cancellationToken);
        var today = WarehouseTime.DateOf(timeProvider.GetUtcNow());

        return draft.Lines.Select(line =>
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                return new IssuePreviewLine(line.ProductId, string.Empty, [], "Nie znaleziono produktu.");
            }

            var batches = available[line.ProductId].ToList();
            var error = StockDraftValidator.ValidateQuantity(line.Quantity, product.Unit)
                ?? CheckReservation(reserved, line, product, batches, today);
            var plan = error is null ? PlanIssue(draft.Type, line, batches, today) : AllocationPlan.Fail(error);
            return new IssuePreviewLine(product.Id, product.Name, plan.Allocations, plan.Error);
        }).ToList();
    }

    public async Task<PagedResult<StockDocumentListItem>> SearchAsync(
        StockDocumentFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.StockDocuments.AsNoTracking();
        if (filter.Type is { } type)
        {
            query = query.Where(d => d.Type == type);
        }

        if (filter.From is { } from)
        {
            query = query.Where(d => d.DocumentDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(d => d.DocumentDate <= to);
        }

        if (SearchPattern.Contains(filter.Term) is { } pattern)
        {
            query = query.Where(d =>
                EF.Functions.ILike(d.Number, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(d.Contractor!.Name, pattern, SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(d => new StockDocumentListItem(
                d.Id,
                d.Type,
                d.Number,
                d.DocumentDate,
                d.Contractor!.Name,
                d.ReversesDocumentId,
                db.StockDocuments.Where(c => c.ReversesDocumentId == d.Id).Select(c => (int?)c.Id).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedResult<StockDocumentListItem>(items, total);
    }

    public async Task<StockDocumentDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var header = await db.StockDocuments.AsNoTracking().Where(d => d.Id == id)
            .Select(d => new
            {
                d.Id,
                d.Type,
                d.Number,
                d.DocumentDate,
                d.CreatedAt,
                ContractorName = d.Contractor!.Name,
                UserEmail = d.User!.Email,
                d.Notes,
                Reverses = d.ReversesDocument == null ? null : new DocumentLink(d.ReversesDocument.Id, d.ReversesDocument.Number),
                ReversedBy = db.StockDocuments.Where(c => c.ReversesDocumentId == d.Id).Select(c => new DocumentLink(c.Id, c.Number)).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var movements = await db.StockMovements.AsNoTracking().Where(m => m.DocumentId == id).OrderBy(m => m.Id)
            .Select(m => new StockMovementRow(
                m.Batch!.ProductId, m.Batch.Product!.Name, m.Batch.Product.Unit, m.Batch.Number, m.Batch.ExpiryDate, m.QuantityChange, m.UnitCost))
            .ToListAsync(cancellationToken);

        return new StockDocumentDetails(
            header.Id, header.Type, header.Number, header.DocumentDate, header.CreatedAt, header.ContractorName,
            header.UserEmail, header.Notes, header.Reverses, header.ReversedBy, movements);
    }

    private static async Task<SaveResult> InOwnTransactionAsync(
        RegalDbContext db, Func<Task<SaveResult>> work, Guid requestId, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await work();
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (DbUpdateException exception) when (DbErrors.UniqueViolation(exception) is RequestIdIndex or ReversesDocumentIndex)
        {
            // Reached only when two sends share no product, so the product locks did not order them.
            db.ChangeTracker.Clear();
            return DbErrors.UniqueViolation(exception) == RequestIdIndex
                ? SaveResult.Ok((await FindByRequestIdAsync(db, requestId, cancellationToken))!.Value)
                : SaveResult.Fail(AlreadyReversed);
        }
    }

    private async Task<SaveResult> PostCoreAsync(RegalDbContext db, StockDocumentDraft draft, int userId, CancellationToken cancellationToken)
    {
        if (StockDraftValidator.Validate(draft) is { } draftError)
        {
            return SaveResult.Fail(draftError);
        }

        // Everything below sees what the previous holder of these locks has committed.
        var products = (await ProductLocks.LockAsync(db, draft.Lines.Select(line => line.ProductId), cancellationToken)).ToDictionary(p => p.Id);

        if (await FindByRequestIdAsync(db, draft.RequestId, cancellationToken) is { } existingId)
        {
            return SaveResult.Ok(existingId);
        }

        if (CheckProducts(draft, products) is { } productError)
        {
            return SaveResult.Fail(productError);
        }

        if (await CheckContractorAsync(db, draft.ContractorId, cancellationToken) is { } contractorError)
        {
            return SaveResult.Fail(contractorError);
        }

        var now = timeProvider.GetUtcNow();
        return draft.Type.IsReceipt()
            ? await ReceiveAsync(db, draft, products, userId, now, cancellationToken)
            : await IssueAsync(db, draft, products, userId, now, cancellationToken);
    }

    private async Task<SaveResult> ReceiveAsync(
        RegalDbContext db,
        StockDocumentDraft draft,
        Dictionary<int, Product> products,
        int userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var date = WarehouseTime.DateOf(now);
        var stock = await LoadProductStockAsync(db, products.Keys, cancellationToken);
        var averages = products.Values.ToDictionary(p => p.Id, p => p.AverageCost);
        var costs = new List<decimal>(draft.Lines.Count);

        foreach (var line in draft.Lines)
        {
            var product = products[line.ProductId];
            var error = StockDraftValidator.ValidateQuantity(line.Quantity, product.Unit)
                ?? StockDraftValidator.ValidateExpiry(line.ExpiryDate, product.HasExpiry, date);
            if (error is not null)
            {
                return SaveResult.Fail($"{product.Name}: {error}");
            }

            var cost = line.UnitCost ?? averages[product.Id];
            if (cost <= 0)
            {
                return SaveResult.Fail($"{product.Name}: podaj koszt, bo bieżąca cena średnia wynosi 0.");
            }

            var currentStock = stock.GetValueOrDefault(product.Id);
            averages[product.Id] = AverageCost.AfterReceipt(currentStock, averages[product.Id], line.Quantity, cost);
            stock[product.Id] = currentStock + line.Quantity;
            costs.Add(cost);
        }

        var document = await NewDocumentAsync(db, draft.Type, draft.RequestId, draft.ContractorId, draft.Notes?.Trim(), null, userId, now, cancellationToken);

        string NumberOf(StockDocumentLine line) => BatchNumbers.Normalize(line.BatchNumber) ?? document.Number;

        var productIds = products.Keys.ToArray();
        var numbers = draft.Lines.Select(NumberOf).Distinct().ToArray();
        var batches = await db.Batches
            .Where(b => productIds.Contains(b.ProductId) && numbers.Contains(b.Number))
            .ToDictionaryAsync(b => (b.ProductId, b.Number), cancellationToken);

        foreach (var (line, cost) in draft.Lines.Zip(costs))
        {
            var number = NumberOf(line);
            if (!batches.TryGetValue((line.ProductId, number), out var batch))
            {
                batch = new Batch { ProductId = line.ProductId, Number = number, ExpiryDate = line.ExpiryDate };
                batches.Add((line.ProductId, number), batch);
            }
            else if (batch.ExpiryDate != line.ExpiryDate)
            {
                return SaveResult.Fail($"{products[line.ProductId].Name}: partia {number} ma inną datę ważności ({batch.ExpiryDate?.ToString() ?? "brak"}).");
            }

            document.Movements.Add(new StockMovement { Batch = batch, QuantityChange = line.Quantity, UnitCost = cost });
        }

        return await SaveDocumentAsync(db, document, averages, AuditActions.StockDocumentPosted, $"number={document.Number}", userId, cancellationToken);
    }

    private async Task<SaveResult> IssueAsync(
        RegalDbContext db,
        StockDocumentDraft draft,
        Dictionary<int, Product> products,
        int userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = WarehouseTime.DateOf(now);
        var available = await LoadAvailabilityAsync(db, products.Keys, cancellationToken);
        var reserved = await LoadReservedForSaleAsync(db, draft, products.Keys, cancellationToken);
        var movements = new List<StockMovement>();

        foreach (var line in draft.Lines)
        {
            var product = products[line.ProductId];
            var batches = available[line.ProductId].ToList();
            var error = StockDraftValidator.ValidateQuantity(line.Quantity, product.Unit)
                ?? CheckReservation(reserved, line, product, batches, today);
            var plan = error is null ? PlanIssue(draft.Type, line, batches, today) : AllocationPlan.Fail(error);
            if (plan.Error is not null)
            {
                return SaveResult.Fail($"{product.Name}: {plan.Error}");
            }

            movements.AddRange(plan.Allocations.Select(allocation => new StockMovement
            {
                BatchId = allocation.Batch.BatchId,
                QuantityChange = -allocation.Quantity,
                UnitCost = product.AverageCost,
            }));
        }

        var document = await NewDocumentAsync(db, draft.Type, draft.RequestId, draft.ContractorId, draft.Notes?.Trim(), null, userId, now, cancellationToken);
        document.Movements = movements;

        return await SaveDocumentAsync(db, document, [], AuditActions.StockDocumentPosted, $"number={document.Number}", userId, cancellationToken);
    }

    private async Task<SaveResult> ReverseCoreAsync(
        RegalDbContext db, int documentId, Guid requestId, int userId, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return SaveResult.Fail("Brak identyfikatora dokumentu. Odśwież stronę i spróbuj ponownie.");
        }

        var productIds = await db.StockMovements.AsNoTracking()
            .Where(m => m.DocumentId == documentId)
            .Select(m => m.Batch!.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (productIds.Count == 0)
        {
            return SaveResult.Fail("Nie znaleziono dokumentu.");
        }

        var products = (await ProductLocks.LockAsync(db, productIds, cancellationToken)).ToDictionary(p => p.Id);

        if (await FindByRequestIdAsync(db, requestId, cancellationToken) is { } existingId)
        {
            return SaveResult.Ok(existingId);
        }

        var original = await db.StockDocuments.AsNoTracking()
            .Include(d => d.Movements.OrderBy(m => m.Id)).ThenInclude(m => m.Batch)
            .SingleAsync(d => d.Id == documentId, cancellationToken);

        if (original.ReversesDocumentId is not null)
        {
            return SaveResult.Fail("Korekty nie można skorygować.");
        }

        if (await db.StockDocuments.AnyAsync(d => d.ReversesDocumentId == documentId, cancellationToken))
        {
            return SaveResult.Fail(AlreadyReversed);
        }

        // Goods put back while the invoice stands would be invoiced but still in stock.
        if (await db.Invoices.AnyAsync(i => i.StockDocumentId == documentId && i.Status != InvoiceStatus.Cancelled, cancellationToken))
        {
            return SaveResult.Fail("Ten dokument należy do faktury. Aby go cofnąć, anuluj fakturę.");
        }

        Dictionary<int, decimal> averages;
        if (original.Type.IsReceipt())
        {
            if (await FindIssuedBatchAsync(db, original, cancellationToken) is { } issued)
            {
                return SaveResult.Fail($"Nie można skorygować dokumentu {original.Number}: towar z partii {issued} został już wydany.");
            }

            averages = await AveragesAfterReceiptReversalAsync(db, original, products, cancellationToken);
        }
        else
        {
            averages = await AveragesAfterIssueReversalAsync(db, original, products, cancellationToken);
        }

        var notes = $"Korekta dokumentu {original.Number}";
        var document = await NewDocumentAsync(
            db, original.Type, requestId, original.ContractorId, notes, original.Id, userId, timeProvider.GetUtcNow(), cancellationToken);
        document.Movements = original.Movements
            .Select(m => new StockMovement { BatchId = m.BatchId, QuantityChange = -m.QuantityChange, UnitCost = m.UnitCost })
            .ToList();

        return await SaveDocumentAsync(
            db, document, averages, AuditActions.StockDocumentReversed, $"number={document.Number};reverses={original.Number}", userId, cancellationToken);
    }

    /// <summary>The first batch whose current stock no longer covers what this receipt put into it.</summary>
    private static async Task<string?> FindIssuedBatchAsync(RegalDbContext db, StockDocument original, CancellationToken cancellationToken)
    {
        var batchIds = original.Movements.Select(m => m.BatchId).ToArray();
        var stock = await db.Stock.AsNoTracking()
            .Where(s => s.WarehouseId == MainWarehouse.Id && batchIds.Contains(s.BatchId))
            .ToDictionaryAsync(s => s.BatchId, s => s.Quantity, cancellationToken);

        return original.Movements
            .GroupBy(m => m.BatchId)
            .FirstOrDefault(batch => stock.GetValueOrDefault(batch.Key) < batch.Sum(m => m.QuantityChange))
            ?.First().Batch!.Number;
    }

    /// <summary>One calculation per product for the whole document, so "nothing left" applies to the document.</summary>
    private static async Task<Dictionary<int, decimal>> AveragesAfterReceiptReversalAsync(
        RegalDbContext db, StockDocument original, Dictionary<int, Product> products, CancellationToken cancellationToken)
    {
        var stock = await LoadProductStockAsync(db, products.Keys, cancellationToken);

        return original.Movements
            .GroupBy(m => m.Batch!.ProductId)
            .ToDictionary(
                product => product.Key,
                product => AverageCost.AfterReceiptReversal(
                    stock.GetValueOrDefault(product.Key),
                    products[product.Key].AverageCost,
                    product.Sum(m => m.QuantityChange),
                    product.Sum(m => m.QuantityChange * m.UnitCost)));
    }

    private static async Task<Dictionary<int, decimal>> AveragesAfterIssueReversalAsync(
        RegalDbContext db, StockDocument original, Dictionary<int, Product> products, CancellationToken cancellationToken)
    {
        var stock = await LoadProductStockAsync(db, products.Keys, cancellationToken);
        var averages = products.Values.ToDictionary(p => p.Id, p => p.AverageCost);

        foreach (var movement in original.Movements)
        {
            var productId = movement.Batch!.ProductId;
            var returned = -movement.QuantityChange;
            var currentStock = stock.GetValueOrDefault(productId);
            averages[productId] = AverageCost.AfterReceipt(currentStock, averages[productId], returned, movement.UnitCost);
            stock[productId] = currentStock + returned;
        }

        return averages;
    }

    /// <summary>
    /// Only a WZ is a sale, so only a WZ respects reservations. An RW records a loss and a correction
    /// a mistake; both happened whatever was promised (SPEC 5.11). Null when nothing is checked.
    /// </summary>
    private static async Task<Dictionary<int, decimal>?> LoadReservedForSaleAsync(
        RegalDbContext db, StockDocumentDraft draft, IEnumerable<int> productIds, CancellationToken cancellationToken) =>
        draft.Type == StockDocumentType.WZ
            ? await Reservations.ReservedAsync(db, productIds, draft.OrderId, cancellationToken)
            : null;

    private static string? CheckReservation(
        Dictionary<int, decimal>? reserved, StockDocumentLine line, Product product, List<BatchAvailability> batches, DateOnly today)
    {
        if (reserved is null || !reserved.TryGetValue(line.ProductId, out var quantity))
        {
            return null;
        }

        var unexpired = batches.Where(b => !BatchAllocator.IsExpired(b.ExpiryDate, today)).Sum(b => b.Quantity);
        return Reservations.CheckSale(line.Quantity, unexpired, quantity, product.Unit);
    }

    private static AllocationPlan PlanIssue(StockDocumentType type, StockDocumentLine line, List<BatchAvailability> batches, DateOnly today) =>
        BatchAllocator.Allocate(line.Quantity, line.BatchId, batches, allowExpired: type == StockDocumentType.RW, today);

    private static string? CheckProducts(StockDocumentDraft draft, Dictionary<int, Product> products)
    {
        foreach (var line in draft.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                return "Nie znaleziono produktu.";
            }

            if (product.Kind != ProductKind.Goods)
            {
                return $"{product.Name}: usługa nie może wystąpić na dokumencie magazynowym.";
            }

            if (!product.IsActive)
            {
                return $"{product.Name}: produkt jest nieaktywny.";
            }
        }

        return null;
    }

    private static async Task<string?> CheckContractorAsync(RegalDbContext db, int? contractorId, CancellationToken cancellationToken)
    {
        if (contractorId is null)
        {
            return null;
        }

        var isActive = await db.Contractors.AsNoTracking()
            .Where(c => c.Id == contractorId)
            .Select(c => (bool?)c.IsActive)
            .SingleOrDefaultAsync(cancellationToken);

        return isActive switch
        {
            null => "Nie znaleziono kontrahenta.",
            false => "Wybrany kontrahent jest nieaktywny.",
            true => null,
        };
    }

    private static async Task<int?> FindByRequestIdAsync(RegalDbContext db, Guid requestId, CancellationToken cancellationToken) =>
        await db.StockDocuments.AsNoTracking()
            .Where(d => d.RequestId == requestId)
            .Select(d => (int?)d.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task<Dictionary<int, decimal>> LoadProductStockAsync(
        RegalDbContext db, IEnumerable<int> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.ToArray();
        return await db.Stock.AsNoTracking()
            .Where(s => s.WarehouseId == MainWarehouse.Id && ids.Contains(s.Batch!.ProductId))
            .GroupBy(s => s.Batch!.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(s => s.Quantity) })
            .ToDictionaryAsync(row => row.ProductId, row => row.Quantity, cancellationToken);
    }

    private static async Task<ILookup<int, BatchAvailability>> LoadAvailabilityAsync(
        RegalDbContext db, IEnumerable<int> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.ToArray();
        var rows = await db.Stock.AsNoTracking()
            .Where(s => s.WarehouseId == MainWarehouse.Id && s.Quantity > 0 && ids.Contains(s.Batch!.ProductId))
            .Select(s => new { s.Batch!.ProductId, Batch = new BatchAvailability(s.BatchId, s.Batch.Number, s.Batch.ExpiryDate, s.Quantity) })
            .ToListAsync(cancellationToken);

        return rows.ToLookup(row => row.ProductId, row => row.Batch);
    }

    /// <summary>Takes the next number; rolling back the transaction gives it back, so numbers have no gaps.</summary>
    private static async Task<StockDocument> NewDocumentAsync(
        RegalDbContext db,
        StockDocumentType type,
        Guid requestId,
        int? contractorId,
        string? notes,
        int? reversesDocumentId,
        int userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var date = WarehouseTime.DateOf(now);
        var typeName = type.ToString();

        // ToListAsync, because anything composed on top would wrap the INSERT in a subquery.
        var numbers = await db.Database.SqlQuery<int>($"""
            INSERT INTO "DocumentCounters" ("Type", "Year", "LastNumber")
            VALUES ({typeName}, {date.Year}, 1)
            ON CONFLICT ("Type", "Year") DO UPDATE SET "LastNumber" = "DocumentCounters"."LastNumber" + 1
            RETURNING "LastNumber" AS "Value"
            """).ToListAsync(cancellationToken);

        return new StockDocument
        {
            Type = type,
            Number = DocumentNumber.Format(type, numbers.Single(), date.Year),
            RequestId = requestId,
            ContractorId = contractorId,
            Notes = string.IsNullOrEmpty(notes) ? null : notes,
            ReversesDocumentId = reversesDocumentId,
            UserId = userId,
            CreatedAt = now.UtcDateTime,
            DocumentDate = date,
        };
    }

    private async Task<SaveResult> SaveDocumentAsync(
        RegalDbContext db,
        StockDocument document,
        Dictionary<int, decimal> averages,
        string auditAction,
        string auditDetails,
        int userId,
        CancellationToken cancellationToken)
    {
        db.StockDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var movement in document.Movements)
        {
            if (!await ApplyStockChangeAsync(db, movement.BatchId, movement.QuantityChange, cancellationToken))
            {
                return SaveResult.Fail("Brak towaru na partii. Odśwież stan i spróbuj ponownie.");
            }
        }

        foreach (var (productId, average) in averages)
        {
            await db.Products.Where(p => p.Id == productId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.AverageCost, average), cancellationToken);
        }

        AuditTrail.AddStockDocument(db, auditAction, document.Id, userId, auditDetails, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        return SaveResult.Ok(document.Id);
    }

    /// <summary>False when the batch does not hold enough: the conditional UPDATE is the last check before the CHECK constraint.</summary>
    private static async Task<bool> ApplyStockChangeAsync(RegalDbContext db, int batchId, decimal change, CancellationToken cancellationToken)
    {
        if (change > 0)
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "Stock" ("WarehouseId", "BatchId", "Quantity")
                VALUES ({MainWarehouse.Id}, {batchId}, {change})
                ON CONFLICT ("WarehouseId", "BatchId") DO UPDATE SET "Quantity" = "Stock"."Quantity" + EXCLUDED."Quantity"
                """, cancellationToken);
            return true;
        }

        var quantity = -change;
        var updated = await db.Stock
            .Where(s => s.WarehouseId == MainWarehouse.Id && s.BatchId == batchId && s.Quantity >= quantity)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Quantity, s => s.Quantity - quantity), cancellationToken);
        return updated == 1;
    }

    /// <summary>Keeps a context shared with a caller from saving these rows again after a rollback.</summary>
    private static void ForgetDocumentEntities(RegalDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is StockDocument or StockMovement or Batch).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
