using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.Web.Features.Invoices;

/// <summary>Takes due sending tasks one at a time. Holds no database lock during an HTTP call.</summary>
public sealed class InvoiceSender(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    IFakturowniaClient fakturownia,
    TimeProvider timeProvider,
    ILogger<InvoiceSender> logger)
{
    public const string MessageType = "SendInvoice";

    /// <summary>Well above the HTTP timeout, so a lease cannot expire during a call.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private const int MaxErrorLength = 1000;

    private sealed record Claim(long MessageId, int InvoiceId, string Payload, int Attempts, string Oid);

    public bool IsEnabled => fakturownia.IsConfigured;

    /// <summary>Handles one due task. False when none is waiting.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var claim = await ClaimAsync(cancellationToken);
        if (claim is null)
        {
            return false;
        }

        try
        {
            var remote = await FindOrCreateAsync(claim, cancellationToken);
            await MarkSentAsync(claim, remote, cancellationToken);
        }
        catch (FakturowniaUnavailableException exception)
        {
            logger.LogWarning("Sending invoice {InvoiceId} failed, attempt {Attempts}: {Reason}", claim.InvoiceId, claim.Attempts, exception.Message);
            await MarkUnsentAsync(claim, exception.Message, giveUp: claim.Attempts >= InvoiceRetry.MaxAttempts, cancellationToken);
        }
        catch (FakturowniaRejectedException exception)
        {
            logger.LogWarning("Fakturownia refused invoice {InvoiceId}: {Reason}", claim.InvoiceId, exception.Message);
            await MarkUnsentAsync(claim, exception.Message, giveUp: true, cancellationToken);
        }

        return true;
    }

    /// <summary>In a short transaction: SKIP LOCKED sends parallel senders to different tasks.</summary>
    private async Task<Claim?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var message = (await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM "OutboxMessages"
                WHERE "NextAttemptAt" <= {now} AND ("LockedUntil" IS NULL OR "LockedUntil" <= {now})
                ORDER BY "NextAttemptAt", "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

        if (message is null)
        {
            return null;
        }

        message.Attempts++;
        message.LockedUntil = now + Lease;
        var oid = await db.Invoices.AsNoTracking()
            .Where(i => i.Id == message.InvoiceId)
            .Select(i => i.ExternalOrderId)
            .SingleAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Claim(message.Id, message.InvoiceId, message.Payload, message.Attempts, oid);
    }

    /// <summary>Looks first: an earlier attempt may have succeeded with its reply lost.</summary>
    private async Task<RemoteInvoice> FindOrCreateAsync(Claim claim, CancellationToken cancellationToken)
    {
        if (await fakturownia.FindByOidAsync(claim.Oid, cancellationToken) is { } existing)
        {
            return existing;
        }

        try
        {
            return await fakturownia.CreateAsync(claim.Payload, cancellationToken);
        }
        catch (FakturowniaRejectedException)
        {
            // oid_unique refuses a duplicate: an earlier attempt finished after the lookup above.
            if (await fakturownia.FindByOidAsync(claim.Oid, cancellationToken) is { } created)
            {
                return created;
            }

            throw;
        }
    }

    private async Task MarkSentAsync(Claim claim, RemoteInvoice remote, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var message = await db.OutboxMessages.SingleAsync(m => m.Id == claim.MessageId, cancellationToken);
        var invoice = await db.Invoices.SingleAsync(i => i.Id == claim.InvoiceId, cancellationToken);

        invoice.Status = InvoiceStatus.Sent;
        invoice.FakturowniaId = remote.Id;
        invoice.Number = remote.Number;
        invoice.PdfUrl = remote.PdfUrl;
        invoice.RemoteGrossTotal = remote.GrossTotal;
        message.NextAttemptAt = null;
        message.LockedUntil = null;
        message.LastError = null;

        if (remote.GrossTotal is { } total && total != invoice.GrossTotal)
        {
            logger.LogWarning("Invoice {InvoiceId}: gross total {Local} here, {Remote} in Fakturownia.", invoice.Id, invoice.GrossTotal, total);
        }

        AuditTrail.AddInvoice(db, AuditActions.InvoiceSent, invoice.Id, null, $"number={remote.Number}", timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task MarkUnsentAsync(Claim claim, string reason, bool giveUp, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var message = await db.OutboxMessages.SingleAsync(m => m.Id == claim.MessageId, cancellationToken);
        message.LockedUntil = null;
        message.LastError = reason.Length > MaxErrorLength ? reason[..MaxErrorLength] : reason;

        if (giveUp)
        {
            var invoice = await db.Invoices.SingleAsync(i => i.Id == claim.InvoiceId, cancellationToken);
            invoice.Status = InvoiceStatus.Failed;
            message.NextAttemptAt = null;
            AuditTrail.AddInvoice(db, AuditActions.InvoiceSendFailed, invoice.Id, null, $"attempts={claim.Attempts}", timeProvider);
        }
        else
        {
            message.NextAttemptAt = timeProvider.GetUtcNow().UtcDateTime + InvoiceRetry.DelayAfter(claim.Attempts);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class InvoiceSendingWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<InvoiceSendingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var warned = false;
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<InvoiceSender>();
                if (!sender.IsEnabled)
                {
                    if (!warned)
                    {
                        logger.LogWarning("Fakturownia is not configured, so invoices wait unsent.");
                        warned = true;
                    }

                    continue;
                }

                while (await sender.ProcessNextAsync(stoppingToken))
                {
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The invoice sending pass failed and will run again.");
            }
        }
    }
}
