using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Audit;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;
using Regal.Web.Features.Warehouse;
using Regal.Web.Integrations.Fakturownia;

namespace Regal.Web.Features.Invoices;

public sealed record ReceivablesSummary(int OverdueCount, decimal OverdueGross, int UnpaidCount, decimal UnpaidGross);

/// <summary>
/// Emailing an invoice through Fakturownia and reading its payment state back (SPEC 5.13).
/// Neither changes the invoice itself, only the columns the database role may update.
/// </summary>
public sealed class InvoicePaymentService(
    IDbContextFactory<RegalDbContext> dbContextFactory,
    ICurrentUser currentUser,
    IFakturowniaClient fakturownia,
    TimeProvider timeProvider)
{
    /// <summary>A paid invoice is never asked about again; others at most this often by the background pass.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    /// <summary>Older invoices are not followed in the background; a person can still check one.</summary>
    public const int FollowDays = 365;

    public async Task<SaveResult> SendByEmailAsync(int invoiceId, string? email, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.RequireUserIdAsync(Roles.Administrator);
        var address = email?.Trim();
        if (!IsEmail(address))
        {
            return SaveResult.Fail("Podaj poprawny adres e-mail.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is not { Status: InvoiceStatus.Sent, FakturowniaId: { } fakturowniaId })
        {
            return SaveResult.Fail("Mailem można wysłać tylko fakturę, która jest już w Fakturowni.");
        }

        try
        {
            await fakturownia.SendByEmailAsync(fakturowniaId, address!, cancellationToken);
        }
        catch (FakturowniaUnavailableException)
        {
            return SaveResult.Fail("Fakturownia teraz nie odpowiada. Spróbuj za chwilę.");
        }
        catch (FakturowniaRejectedException exception)
        {
            return SaveResult.Fail($"Fakturownia odmówiła wysłania: {exception.Message}");
        }

        invoice.EmailedAt = timeProvider.GetUtcNow().UtcDateTime;
        invoice.EmailedTo = address;
        AuditTrail.AddInvoice(db, AuditActions.InvoiceEmailed, invoiceId, userId, $"to={address}", timeProvider);
        await db.SaveChangesAsync(cancellationToken);
        return SaveResult.Ok(invoiceId);
    }

    /// <summary>Reads the payment state of one invoice now, at a person's request.</summary>
    public async Task<SaveResult> RefreshAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is not { Status: InvoiceStatus.Sent, FakturowniaId: not null })
        {
            return SaveResult.Fail("Płatność można sprawdzić tylko dla faktury, która jest już w Fakturowni.");
        }

        try
        {
            await ApplyAsync(invoice, cancellationToken);
        }
        catch (FakturowniaUnavailableException)
        {
            return SaveResult.Fail("Fakturownia teraz nie odpowiada. Spróbuj za chwilę.");
        }
        catch (FakturowniaRejectedException exception)
        {
            return SaveResult.Fail($"Fakturownia odmówiła odpowiedzi: {exception.Message}");
        }

        await db.SaveChangesAsync(cancellationToken);
        return SaveResult.Ok(invoiceId);
    }

    /// <summary>
    /// The background pass: the unpaid invoices checked longest ago, at most <paramref name="batch"/>.
    /// Stops at the first sign that Fakturownia is unavailable. Returns how many were checked.
    /// </summary>
    public async Task<int> RefreshDueAsync(int batch, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var checkedBefore = now - CheckEvery;
        var since = WarehouseTime.DateOf(timeProvider.GetUtcNow()).AddDays(-FollowDays);

        var due = await db.Invoices
            .Where(i => i.Status == InvoiceStatus.Sent && i.FakturowniaId != null && i.PaymentStatus != InvoicePaymentStatus.Paid
                && i.IssueDate >= since && (i.PaymentCheckedAt == null || i.PaymentCheckedAt < checkedBefore))
            .OrderBy(i => i.PaymentCheckedAt.HasValue).ThenBy(i => i.PaymentCheckedAt).ThenBy(i => i.Id)
            .Take(Math.Clamp(batch, 1, 100))
            .ToListAsync(cancellationToken);

        var done = 0;
        foreach (var invoice in due)
        {
            try
            {
                await ApplyAsync(invoice, cancellationToken);
            }
            catch (FakturowniaUnavailableException)
            {
                break;
            }
            catch (FakturowniaRejectedException)
            {
                // E.g. deleted there: noted as checked, so it does not come back on every pass.
                invoice.PaymentCheckedAt = now;
            }

            done++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return done;
    }

    /// <summary>Unpaid sent VAT invoices, and those of them past their due date (Europe/Warsaw).</summary>
    public async Task<ReceivablesSummary> GetReceivablesAsync(CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var today = WarehouseTime.DateOf(timeProvider.GetUtcNow());

        var open = await db.Invoices.AsNoTracking()
            .Where(i => i.Kind == InvoiceKind.Vat && i.Status == InvoiceStatus.Sent
                && (i.PaymentStatus == InvoicePaymentStatus.Unpaid || i.PaymentStatus == InvoicePaymentStatus.Partial))
            .Select(i => new { i.IssueDate, i.PaymentDays, Due = i.GrossTotal - (i.PaidAmount ?? 0) })
            .ToListAsync(cancellationToken);

        var overdue = open.Where(i => i.IssueDate.AddDays(i.PaymentDays) < today).ToList();
        return new ReceivablesSummary(overdue.Count, overdue.Sum(i => i.Due), open.Count, open.Sum(i => i.Due));
    }

    public static InvoicePaymentStatus StatusOf(RemotePayment payment) => payment.Status switch
    {
        "paid" => InvoicePaymentStatus.Paid,
        "partial" => InvoicePaymentStatus.Partial,
        _ => payment.Paid > 0 ? InvoicePaymentStatus.Partial : InvoicePaymentStatus.Unpaid,
    };

    public static bool IsEmail(string? address) =>
        !string.IsNullOrWhiteSpace(address)
        && address.Length <= 320
        && !address.Contains(',') && !address.Contains(';')
        && MailAddress.TryCreate(address, out var parsed)
        && parsed.Address == address;

    private async Task ApplyAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var payment = await fakturownia.GetPaymentAsync(invoice.FakturowniaId!.Value, cancellationToken);
        invoice.PaymentStatus = StatusOf(payment);
        invoice.PaidAmount = payment.Paid;
        invoice.PaidDate = payment.PaidDate;
        invoice.PaymentCheckedAt = timeProvider.GetUtcNow().UtcDateTime;
    }
}

public sealed class PaymentRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IFakturowniaClient fakturownia,
    TimeProvider timeProvider,
    ILogger<PaymentRefreshWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private const int Batch = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!fakturownia.IsConfigured)
            {
                continue;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<InvoicePaymentService>().RefreshDueAsync(Batch, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The payment refresh pass failed and will run again.");
            }
        }
    }
}
