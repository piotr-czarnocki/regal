using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;
using Regal.Web.Features.Products;

namespace Regal.Web.Features.Warehouse;

public sealed record StocktakeListItem(int Id, string? Number, StocktakeStatus Status, DateTime CreatedAt, int LineCount);

/// <summary>On a draft Expected is the stock now; on an approved sheet, the stock at approval.</summary>
public sealed record StocktakeLineRow(
    int Id, int ProductId, string ProductName, string Unit, string BatchNumber, DateOnly? ExpiryDate, decimal? Counted, decimal Expected)
{
    public decimal? Difference => Counted - Expected;
}

public sealed record StocktakeDetails(
    int Id,
    string? Number,
    StocktakeStatus Status,
    DateTime CreatedAt,
    DateTime? ApprovedAt,
    string UserEmail,
    string? Notes,
    IReadOnlyList<StocktakeLineRow> Lines,
    IReadOnlyList<DocumentLink> Documents);

/// <summary>What one scan did to the sheet, in Polish, ready to show.</summary>
public sealed record StocktakeScanResult(bool Success, string Message);

/// <summary>Count sheets (INW) and the posting of their differences (SPEC 5.14).</summary>
public sealed class StocktakeService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    StockDocumentService documents,
    TimeProvider timeProvider)
{
    private const string Closed = "Ten arkusz jest już zamknięty.";

    public async Task<SaveResult> CreateAsync(string? notes = null, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Staff);
        var trimmed = notes?.Trim();
        if (trimmed?.Length > StockDraftValidator.MaxNotesLength)
        {
            return SaveResult.Fail($"Uwagi mogą mieć najwyżej {StockDraftValidator.MaxNotesLength} znaków.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var stocktake = new Stocktake
        {
            Status = StocktakeStatus.Draft,
            UserId = userId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            Notes = string.IsNullOrEmpty(trimmed) ? null : trimmed,
        };
        db.Stocktakes.Add(stocktake);
        await db.SaveChangesAsync(cancellationToken);
        return SaveResult.Ok(stocktake.Id);
    }

    /// <summary>
    /// Puts the goods' batches with stock on the sheet. With one batch of goods in pieces, every scan
    /// also counts one, so counting can be done by scanning each item; weights are typed in.
    /// </summary>
    public Task<StocktakeScanResult> AddProductAsync(int stocktakeId, int productId, CancellationToken cancellationToken = default) =>
        InDraftAsync(stocktakeId, async (db, _) =>
        {
            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
            if (product is null)
            {
                return new StocktakeScanResult(false, "Nie znaleziono produktu.");
            }

            if (product.Kind != ProductKind.Goods)
            {
                return new StocktakeScanResult(false, $"{product.Name}: usługa nie ma stanu do policzenia.");
            }

            var batches = await db.Stock.AsNoTracking()
                .Where(s => s.WarehouseId == MainWarehouse.Id && s.Quantity > 0 && s.Batch!.ProductId == productId)
                .OrderBy(s => s.Batch!.ExpiryDate == null).ThenBy(s => s.Batch!.ExpiryDate).ThenBy(s => s.BatchId)
                .Select(s => s.BatchId)
                .ToListAsync(cancellationToken);
            if (batches.Count == 0)
            {
                return new StocktakeScanResult(false, $"{product.Name}: brak stanu. Znaleziony towar przyjmij dokumentem PW z partią i datą ważności.");
            }

            var lines = await db.StocktakeLines.Where(l => l.StocktakeId == stocktakeId && batches.Contains(l.BatchId)).ToListAsync(cancellationToken);
            // A scan is one item, so only whole pieces are counted by scanning; a weight is typed in.
            var countsByScan = string.Equals(product.Unit.Trim(), "szt", StringComparison.OrdinalIgnoreCase);
            if (batches.Count == 1 && countsByScan)
            {
                var line = lines.SingleOrDefault();
                if (line is null)
                {
                    db.StocktakeLines.Add(new StocktakeLine { StocktakeId = stocktakeId, BatchId = batches[0], Counted = 1 });
                }
                else
                {
                    line.Counted = (line.Counted ?? 0) + 1;
                }

                await db.SaveChangesAsync(cancellationToken);
                return new StocktakeScanResult(true, $"{product.Name}: policzono {StockLabels.Quantity(line?.Counted ?? 1)} {product.Unit}.");
            }

            foreach (var batchId in batches.Where(b => lines.All(l => l.BatchId != b)))
            {
                db.StocktakeLines.Add(new StocktakeLine { StocktakeId = stocktakeId, BatchId = batchId });
            }

            await db.SaveChangesAsync(cancellationToken);
            return new StocktakeScanResult(true, batches.Count == 1
                ? $"{product.Name}: wpisz ilość w {product.Unit}."
                : $"{product.Name}: {batches.Count} partie, wpisz ilość przy każdej.");
        }, cancellationToken);

    /// <summary>Empty clears the count; zero means nothing was found.</summary>
    public Task<StocktakeScanResult> SetCountedAsync(int stocktakeId, int lineId, decimal? counted, CancellationToken cancellationToken = default) =>
        InDraftAsync(stocktakeId, async (db, _) =>
        {
            var line = await db.StocktakeLines.Include(l => l.Batch).ThenInclude(b => b!.Product)
                .SingleOrDefaultAsync(l => l.Id == lineId && l.StocktakeId == stocktakeId, cancellationToken);
            if (line is null)
            {
                return new StocktakeScanResult(false, "Nie znaleziono pozycji arkusza.");
            }

            if (counted < 0)
            {
                return new StocktakeScanResult(false, "Policzona ilość nie może być ujemna.");
            }

            if (counted is { } quantity && quantity != 0 && StockDraftValidator.ValidateQuantity(quantity, line.Batch!.Product!.Unit) is { } error)
            {
                return new StocktakeScanResult(false, $"{line.Batch.Product.Name}: {error}");
            }

            line.Counted = counted;
            await db.SaveChangesAsync(cancellationToken);
            return new StocktakeScanResult(true, "Zapisano.");
        }, cancellationToken);

    public Task<StocktakeScanResult> RemoveLineAsync(int stocktakeId, int lineId, CancellationToken cancellationToken = default) =>
        InDraftAsync(stocktakeId, async (db, _) =>
        {
            var removed = await db.StocktakeLines.Where(l => l.Id == lineId && l.StocktakeId == stocktakeId).ExecuteDeleteAsync(cancellationToken);
            return new StocktakeScanResult(removed == 1, removed == 1 ? "Usunięto pozycję." : "Nie znaleziono pozycji arkusza.");
        }, cancellationToken);

    public async Task<SaveResult> CancelAsync(int stocktakeId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var sheet = await LockAsync(db, stocktakeId, cancellationToken);
        switch (sheet?.Status)
        {
            case null:
                return SaveResult.Fail("Nie znaleziono arkusza spisu.");
            case StocktakeStatus.Cancelled:
                return SaveResult.Ok(stocktakeId);
            case StocktakeStatus.Approved:
                return SaveResult.Fail("Zatwierdzonego spisu nie można anulować. Różnice poprawia się korektą jego dokumentów RW i PW.");
        }

        sheet.Status = StocktakeStatus.Cancelled;
        AuditTrail.AddStocktake(db, AuditActions.StocktakeCancelled, stocktakeId, userId, null, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SaveResult.Ok(stocktakeId);
    }

    /// <summary>Posts the differences against the stock at this moment and closes the sheet. Repeating it changes nothing.</summary>
    public async Task<SaveResult> ApproveAsync(int stocktakeId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var result = await ApproveCoreAsync(db, stocktakeId, userId, cancellationToken);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<PagedResult<StocktakeListItem>> SearchAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var total = await db.Stocktakes.CountAsync(cancellationToken);
        var items = await db.Stocktakes.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(s => new StocktakeListItem(s.Id, s.Number, s.Status, s.CreatedAt, s.Lines.Count))
            .ToListAsync(cancellationToken);
        return new PagedResult<StocktakeListItem>(items, total);
    }

    public async Task<StocktakeDetails?> GetDetailsAsync(int stocktakeId, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sheet = await db.Stocktakes.AsNoTracking().Where(s => s.Id == stocktakeId)
            .Select(s => new { s.Id, s.Number, s.Status, s.CreatedAt, s.ApprovedAt, UserEmail = s.User!.Email, s.Notes })
            .SingleOrDefaultAsync(cancellationToken);
        if (sheet is null)
        {
            return null;
        }

        var lines = await db.StocktakeLines.AsNoTracking().Where(l => l.StocktakeId == stocktakeId)
            .OrderBy(l => l.Batch!.Product!.Name).ThenBy(l => l.Batch!.ExpiryDate).ThenBy(l => l.Id)
            .Select(l => new
            {
                l.Id,
                l.Batch!.ProductId,
                l.Batch.Product!.Name,
                l.Batch.Product.Unit,
                l.Batch.Number,
                l.Batch.ExpiryDate,
                l.Counted,
                l.Expected,
                Current = db.Stock.Where(s => s.WarehouseId == MainWarehouse.Id && s.BatchId == l.BatchId).Sum(s => (decimal?)s.Quantity) ?? 0,
            })
            .ToListAsync(cancellationToken);

        var posted = await db.StockDocuments.AsNoTracking().Where(d => d.StocktakeId == stocktakeId).OrderBy(d => d.Id)
            .Select(d => new DocumentLink(d.Id, d.Number))
            .ToListAsync(cancellationToken);

        return new StocktakeDetails(
            sheet.Id, sheet.Number, sheet.Status, sheet.CreatedAt, sheet.ApprovedAt, sheet.UserEmail, sheet.Notes,
            lines.Select(l => new StocktakeLineRow(
                l.Id, l.ProductId, l.Name, l.Unit, l.Number, l.ExpiryDate, Trim(l.Counted),
                Trim(sheet.Status == StocktakeStatus.Approved ? l.Expected ?? 0 : l.Current))).ToList(),
            posted);
    }

    private async Task<SaveResult> ApproveCoreAsync(RegalDbContext db, int stocktakeId, int userId, CancellationToken cancellationToken)
    {
        var sheet = await LockAsync(db, stocktakeId, cancellationToken);
        switch (sheet?.Status)
        {
            case null:
                return SaveResult.Fail("Nie znaleziono arkusza spisu.");
            case StocktakeStatus.Approved:
                return SaveResult.Ok(stocktakeId);
            case StocktakeStatus.Cancelled:
                return SaveResult.Fail(Closed);
        }

        // Batches read apart and untracked: posting a document detaches the batches it tracked.
        var lines = await db.StocktakeLines.Where(l => l.StocktakeId == stocktakeId).ToListAsync(cancellationToken);
        var lineBatchIds = lines.Select(l => l.BatchId).ToArray();
        var batchOf = await db.Batches.AsNoTracking().Where(b => lineBatchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
        if (lines.Count == 0)
        {
            return SaveResult.Fail("Arkusz jest pusty. Zeskanuj albo wybierz towary do policzenia.");
        }

        if (lines.Count(l => l.Counted is null) is var uncounted and > 0)
        {
            return SaveResult.Fail($"Policz wszystkie pozycje: {uncounted} bez ilości. Jeśli towaru nie ma, wpisz 0.");
        }

        // All products at once and ascending, before any document: the documents below lock
        // only products this transaction already holds, so their order cannot cause a deadlock.
        var products = (await ProductLocks.LockAsync(db, lines.Select(l => batchOf[l.BatchId].ProductId), cancellationToken)).ToDictionary(p => p.Id);
        var batchIds = lines.Select(l => l.BatchId).ToArray();
        var stock = await db.Stock.AsNoTracking()
            .Where(s => s.WarehouseId == MainWarehouse.Id && batchIds.Contains(s.BatchId))
            .ToDictionaryAsync(s => s.BatchId, s => s.Quantity, cancellationToken);

        var today = WarehouseTime.DateOf(timeProvider.GetUtcNow());
        foreach (var line in lines)
        {
            line.Expected = stock.GetValueOrDefault(line.BatchId);
            var product = products[batchOf[line.BatchId].ProductId];
            if (line.Counted > line.Expected)
            {
                if (BatchAllocator.IsExpired(batchOf[line.BatchId].ExpiryDate, today))
                {
                    return SaveResult.Fail($"{product.Name}, partia {batchOf[line.BatchId].Number}: nadwyżki nie można przyjąć na przeterminowaną partię. Usuń pozycję i załatw ją ręcznie.");
                }

                if (product.AverageCost <= 0)
                {
                    return SaveResult.Fail($"{product.Name}: cena średnia wynosi 0, więc nadwyżki nie da się wycenić. Przyjmij ją ręcznie dokumentem PW z kosztem.");
                }
            }
        }

        var number = await NextNumberAsync(db, today.Year, cancellationToken);
        await db.SaveChangesAsync(cancellationToken); // expected quantities, while the sheet is still a draft

        var surplus = lines.Where(l => l.Counted > l.Expected).ToList();
        foreach (var chunk in surplus.Chunk(StockDraftValidator.MaxLines))
        {
            var posted = await documents.PostInTransactionAsync(db, new StockDocumentDraft
            {
                RequestId = Guid.NewGuid(),
                Type = StockDocumentType.PW,
                StocktakeId = stocktakeId,
                Notes = $"Nadwyżka inwentaryzacyjna {number}",
                Lines = chunk.Select(l => new StockDocumentLine
                {
                    ProductId = batchOf[l.BatchId].ProductId,
                    Quantity = l.Counted!.Value - l.Expected!.Value,
                    BatchNumber = batchOf[l.BatchId].Number,
                    ExpiryDate = batchOf[l.BatchId].ExpiryDate,
                }).ToList(),
            }, cancellationToken);
            if (!posted.IsSuccess)
            {
                return posted;
            }
        }

        // A product may appear on an RW once, so each round takes one short batch per product.
        var shortages = lines.Where(l => l.Counted < l.Expected).GroupBy(l => batchOf[l.BatchId].ProductId).ToList();
        for (var round = 0; shortages.Any(g => g.Count() > round); round++)
        {
            var roundLines = shortages.Where(g => g.Count() > round).Select(g => g.OrderBy(l => l.BatchId).ElementAt(round)).ToList();
            foreach (var chunk in roundLines.Chunk(StockDraftValidator.MaxLines))
            {
                var posted = await documents.PostInTransactionAsync(db, new StockDocumentDraft
                {
                    RequestId = Guid.NewGuid(),
                    Type = StockDocumentType.RW,
                    StocktakeId = stocktakeId,
                    Notes = $"Niedobór inwentaryzacyjny {number}",
                    Lines = chunk.Select(l => new StockDocumentLine
                    {
                        ProductId = batchOf[l.BatchId].ProductId,
                        Quantity = l.Expected!.Value - l.Counted!.Value,
                        BatchId = l.BatchId,
                    }).ToList(),
                }, cancellationToken);
                if (!posted.IsSuccess)
                {
                    return posted;
                }
            }
        }

        sheet.Status = StocktakeStatus.Approved;
        sheet.Number = number;
        sheet.ApprovedAt = timeProvider.GetUtcNow().UtcDateTime;
        AuditTrail.AddStocktake(db, AuditActions.StocktakeApproved, stocktakeId, userId,
            string.Create(CultureInfo.InvariantCulture, $"number={number};surplus={surplus.Count};shortage={shortages.Sum(g => g.Count())}"), timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        return SaveResult.Ok(stocktakeId);
    }

    /// <summary>Every change of a draft locks the sheet's row, so it cannot pass an approval.</summary>
    private async Task<StocktakeScanResult> InDraftAsync(
        int stocktakeId, Func<RegalDbContext, Stocktake, Task<StocktakeScanResult>> change, CancellationToken cancellationToken)
    {
        await currentUser.RequireUserIdAsync(Roles.Staff);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var sheet = await LockAsync(db, stocktakeId, cancellationToken);
        if (sheet is null)
        {
            return new StocktakeScanResult(false, "Nie znaleziono arkusza spisu.");
        }

        if (sheet.Status != StocktakeStatus.Draft)
        {
            return new StocktakeScanResult(false, Closed);
        }

        var result = await change(db, sheet);
        if (result.Success)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    private static async Task<Stocktake?> LockAsync(RegalDbContext db, int stocktakeId, CancellationToken cancellationToken) =>
        (await db.Stocktakes.FromSql($"""SELECT * FROM "Stocktakes" WHERE "Id" = {stocktakeId} FOR UPDATE""").ToListAsync(cancellationToken))
        .SingleOrDefault();

    private static async Task<string> NextNumberAsync(RegalDbContext db, int year, CancellationToken cancellationToken)
    {
        // ToListAsync, because anything composed on top would wrap the INSERT in a subquery.
        var numbers = await db.Database.SqlQuery<int>($"""
            INSERT INTO "StocktakeCounters" ("Year", "LastNumber")
            VALUES ({year}, 1)
            ON CONFLICT ("Year") DO UPDATE SET "LastNumber" = "StocktakeCounters"."LastNumber" + 1
            RETURNING "LastNumber" AS "Value"
            """).ToListAsync(cancellationToken);
        return string.Create(CultureInfo.InvariantCulture, $"INW/{numbers.Single()}/{year}");
    }

    /// <summary>numeric(18,3) comes back as 8.000; shown and edited as 8.</summary>
    private static decimal Trim(decimal value) => value / 1.000000000000000000000000000000000m;

    private static decimal? Trim(decimal? value) => value is { } v ? Trim(v) : null;
}
