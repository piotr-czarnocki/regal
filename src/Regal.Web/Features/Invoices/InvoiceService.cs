using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;
using Regal.Web.Features.Warehouse;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.Web.Features.Invoices;

public sealed class InvoiceService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    StockDocumentService stockDocuments,
    IFakturowniaClient fakturownia,
    TimeProvider timeProvider)
{
    private const string UniqueOidIndex = "IX_Invoices_ExternalOrderId";

    /// <summary>Creates the WZ, the invoice and its sending task in one transaction.</summary>
    public async Task<SaveResult> IssueAsync(InvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);

        if (InvoiceRules.Validate(draft) is { } error)
        {
            return SaveResult.Fail(error);
        }

        var oid = InvoiceRules.OidOf(draft.RequestId);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await IssueCoreAsync(db, draft, oid, userId, cancellationToken);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (DbUpdateException exception) when (DbErrors.UniqueViolation(exception) == UniqueOidIndex)
        {
            // Two sends of an invoice with only services: no product lock put them in order.
            db.ChangeTracker.Clear();
            return SaveResult.Ok((await FindByOidAsync(db, oid, cancellationToken))!.Value);
        }
    }

    /// <summary>Sends a failed invoice again from the first attempt.</summary>
    public async Task<SaveResult> RetryAsync(int id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var message = await LockMessageAsync(db, id, cancellationToken);
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null || message is null)
        {
            return SaveResult.Fail("Nie znaleziono faktury.");
        }

        if (invoice.Status != InvoiceStatus.Failed)
        {
            return SaveResult.Fail("Ponowić można tylko fakturę z błędem wysyłki.");
        }

        invoice.Status = InvoiceStatus.ToSend;
        message.Attempts = 0;
        message.NextAttemptAt = timeProvider.GetUtcNow().UtcDateTime;
        message.LockedUntil = null;
        AuditTrail.AddInvoice(db, AuditActions.InvoiceRetried, id, userId, null, timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return SaveResult.Ok(id);
    }

    /// <summary>
    /// Cancels an invoice that does not exist in Fakturownia and reverses its WZ. The check runs
    /// outside any transaction, with the sender kept away by a lease on the sending task.
    /// </summary>
    public async Task<SaveResult> CancelAsync(int id, Guid requestId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        if (requestId == Guid.Empty)
        {
            return SaveResult.Fail("Brak identyfikatora operacji. Odśwież stronę i spróbuj ponownie.");
        }

        var (error, oid) = await ReserveForCancelAsync(id, cancellationToken);
        if (error is not null)
        {
            return SaveResult.Fail(error);
        }

        RemoteInvoice? remote;
        try
        {
            remote = await fakturownia.FindByOidAsync(oid!, cancellationToken);
        }
        catch (Exception exception) when (exception is FakturowniaUnavailableException or FakturowniaRejectedException)
        {
            await ReleaseAsync(id, sendNow: false, cancellationToken);
            return SaveResult.Fail("Nie można teraz sprawdzić faktury w Fakturowni, więc nie można jej anulować. Spróbuj później.");
        }

        if (remote is not null)
        {
            await ReleaseAsync(id, sendNow: true, cancellationToken);
            return SaveResult.Fail("Ta faktura istnieje w Fakturowni, więc nie można jej anulować.");
        }

        return await CompleteCancelAsync(id, requestId, userId, cancellationToken);
    }

    private async Task<SaveResult> IssueCoreAsync(
        RegalDbContext db, InvoiceDraft draft, string oid, int userId, CancellationToken cancellationToken)
    {
        var productIds = draft.Lines.Select(line => line.ProductId).ToArray();

        // The order row is locked before any product, as cancelling an order locks only that row,
        // so the two cannot wait for each other in a cycle (SPEC 5.11).
        CustomerOrder? order = null;
        if (draft.OrderId is { } orderId)
        {
            order = await LockOrderAsync(db, orderId, cancellationToken);

            // A repeated send of an invoice that already completed this order.
            if (await FindByOidAsync(db, oid, cancellationToken) is { } sentId)
            {
                return SaveResult.Ok(sentId);
            }

            if (CheckOrder(order, draft) is { } orderError)
            {
                return SaveResult.Fail(orderError);
            }
        }

        // Kind never changes once a product exists, so it can be read before any lock is taken.
        var goodsIds = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.Kind == ProductKind.Goods)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        int? stockDocumentId = null;
        if (goodsIds.Count > 0)
        {
            var issue = await stockDocuments.PostInTransactionAsync(db, InvoicePayload.IssueDraftFor(draft, goodsIds), cancellationToken);
            if (!issue.IsSuccess)
            {
                return issue;
            }

            stockDocumentId = issue.Id;
        }

        // After the WZ: its product locks make a repeated send wait for the first one to commit.
        if (await FindByOidAsync(db, oid, cancellationToken) is { } existingId)
        {
            return SaveResult.Ok(existingId);
        }

        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var contractor = await db.Contractors.AsNoTracking().SingleOrDefaultAsync(c => c.Id == draft.ContractorId, cancellationToken);
        if (CheckRecords(draft, products, contractor) is { } recordError)
        {
            return SaveResult.Fail(recordError);
        }

        var costs = await LoadUnitCostsAsync(db, stockDocumentId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var invoice = new Invoice
        {
            Kind = InvoiceKind.Vat,
            Status = InvoiceStatus.ToSend,
            ExternalOrderId = oid,
            ContractorId = contractor!.Id,
            StockDocumentId = stockDocumentId,
            UserId = userId,
            PaymentDays = draft.PaymentDays,
            PaymentMethod = draft.PaymentMethod,
            IssueDate = WarehouseTime.DateOf(now),
            IssuedAt = now.UtcDateTime,
            Lines = draft.Lines.Select(line => NewLine(line, products[line.ProductId], costs)).ToList(),
        };
        invoice.NetTotal = invoice.Lines.Sum(l => l.NetValue);
        invoice.VatTotal = invoice.Lines.Sum(l => l.VatValue);
        invoice.GrossTotal = invoice.Lines.Sum(l => l.GrossValue);

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);

        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = InvoiceSender.MessageType,
            InvoiceId = invoice.Id,
            Payload = InvoicePayload.Build(invoice, contractor),
            NextAttemptAt = now.UtcDateTime,
        });
        AuditTrail.AddInvoice(db, AuditActions.InvoiceIssued, invoice.Id, userId, $"oid={oid}", timeProvider);

        if (order is not null)
        {
            // The whole order is done, even if the invoice changed its quantities.
            order.Status = CustomerOrderStatus.Completed;
            order.InvoiceId = invoice.Id;
            AuditTrail.AddOrder(db, AuditActions.OrderCompleted, order.Id, userId, $"number={order.Number};invoice={invoice.Id}", timeProvider);
        }

        await db.SaveChangesAsync(cancellationToken);
        return SaveResult.Ok(invoice.Id);
    }

    private static async Task<CustomerOrder?> LockOrderAsync(RegalDbContext db, int orderId, CancellationToken cancellationToken) =>
        (await db.CustomerOrders
            .FromSql($"""SELECT * FROM "CustomerOrders" WHERE "Id" = {orderId} FOR UPDATE""")
            .ToListAsync(cancellationToken))
        .SingleOrDefault();

    private static string? CheckOrder(CustomerOrder? order, InvoiceDraft draft) => order switch
    {
        null => "Nie znaleziono zamówienia.",
        { Status: CustomerOrderStatus.Completed } => $"Zamówienie {order.Number} jest już zrealizowane.",
        { Status: CustomerOrderStatus.Cancelled } => $"Zamówienie {order.Number} zostało anulowane.",
        _ when order.ContractorId != draft.ContractorId => $"Faktura do zamówienia {order.Number} musi mieć tego samego nabywcę.",
        _ => null,
    };

    private static InvoiceLine NewLine(InvoiceDraftLine line, Product product, Dictionary<int, decimal> costs)
    {
        var price = line.NetPrice ?? product.NetPrice;
        var amounts = InvoiceRules.Amounts(line.Quantity, price, line.DiscountPercent, product.VatRate);
        return new InvoiceLine
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Unit = product.Unit,
            Quantity = line.Quantity,
            NetPrice = price,
            DiscountPercent = line.DiscountPercent,
            VatRate = product.VatRate,
            NetValue = amounts.Net,
            VatValue = amounts.Vat,
            GrossValue = amounts.Gross,
            UnitCost = costs.GetValueOrDefault(product.Id),
        };
    }

    private static string? CheckRecords(InvoiceDraft draft, Dictionary<int, Product> products, Contractor? contractor)
    {
        if (contractor is null)
        {
            return "Nie znaleziono kontrahenta.";
        }

        if (!contractor.IsActive)
        {
            return "Wybrany kontrahent jest nieaktywny.";
        }

        foreach (var line in draft.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                return "Nie znaleziono produktu.";
            }

            if (!product.IsActive)
            {
                return $"{product.Name}: produkt jest nieaktywny.";
            }

            if (StockDraftValidator.ValidateQuantity(line.Quantity, product.Unit) is { } quantityError)
            {
                return $"{product.Name}: {quantityError}";
            }
        }

        return null;
    }

    /// <summary>Every movement of a product on one issue has the same cost: the average at that moment.</summary>
    private static async Task<Dictionary<int, decimal>> LoadUnitCostsAsync(
        RegalDbContext db, int? stockDocumentId, CancellationToken cancellationToken)
    {
        if (stockDocumentId is null)
        {
            return [];
        }

        var rows = await db.StockMovements.AsNoTracking()
            .Where(m => m.DocumentId == stockDocumentId)
            .Select(m => new { m.Batch!.ProductId, m.UnitCost })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.First().UnitCost);
    }

    private static async Task<int?> FindByOidAsync(RegalDbContext db, string oid, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking()
            .Where(i => i.ExternalOrderId == oid)
            .Select(i => (int?)i.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task<OutboxMessage?> LockMessageAsync(RegalDbContext db, int invoiceId, CancellationToken cancellationToken) =>
        (await db.OutboxMessages
            .FromSql($"""SELECT * FROM "OutboxMessages" WHERE "InvoiceId" = {invoiceId} FOR UPDATE""")
            .ToListAsync(cancellationToken))
        .SingleOrDefault();

    private async Task<(string? Error, string? Oid)> ReserveForCancelAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var message = await LockMessageAsync(db, id, cancellationToken);
        var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null || message is null)
        {
            return ("Nie znaleziono faktury.", null);
        }

        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            return ("Ta faktura jest już anulowana.", null);
        }

        if (invoice.Status == InvoiceStatus.Sent)
        {
            return ("Ta faktura została wysłana do Fakturowni, więc nie można jej anulować.", null);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (message.LockedUntil > now)
        {
            return ("Trwa wysyłka tej faktury. Spróbuj za chwilę.", null);
        }

        message.LockedUntil = now + InvoiceSender.Lease;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (null, invoice.ExternalOrderId);
    }

    private async Task ReleaseAsync(int id, bool sendNow, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var message = (await LockMessageAsync(db, id, cancellationToken))!;
        message.LockedUntil = null;
        if (sendNow)
        {
            // The invoice exists, so the sender only has to find it and record its data.
            var invoice = await db.Invoices.SingleAsync(i => i.Id == id, cancellationToken);
            invoice.Status = InvoiceStatus.ToSend;
            message.NextAttemptAt = timeProvider.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SaveResult> CompleteCancelAsync(int id, Guid requestId, int userId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var message = (await LockMessageAsync(db, id, cancellationToken))!;
        var invoice = await db.Invoices.SingleAsync(i => i.Id == id, cancellationToken);

        invoice.Status = InvoiceStatus.Cancelled;
        message.NextAttemptAt = null;
        message.LockedUntil = null;
        AuditTrail.AddInvoice(db, AuditActions.InvoiceCancelled, id, userId, $"oid={invoice.ExternalOrderId}", timeProvider);

        // Saved first: the correction refuses a WZ that still belongs to a live invoice.
        await db.SaveChangesAsync(cancellationToken);

        if (invoice.StockDocumentId is { } stockDocumentId)
        {
            var reversal = await stockDocuments.ReverseInTransactionAsync(db, stockDocumentId, requestId, cancellationToken);
            if (!reversal.IsSuccess)
            {
                return reversal;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return SaveResult.Ok(id);
    }
}
