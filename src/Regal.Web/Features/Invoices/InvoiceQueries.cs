using Microsoft.EntityFrameworkCore;
using Regal.Web.Data;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Common;
using Regal.Web.Features.Warehouse;

namespace Regal.Web.Features.Invoices;

public sealed record InvoiceFilter(InvoiceStatus? Status, DateOnly? From, DateOnly? To, string? Term);

public sealed record InvoiceListItem(
    int Id,
    string? Number,
    InvoiceStatus Status,
    DateOnly IssueDate,
    string ContractorName,
    decimal GrossTotal);

public sealed record InvoiceLineRow(
    string ProductName,
    string Unit,
    decimal Quantity,
    decimal NetPrice,
    decimal DiscountPercent,
    string VatRate,
    decimal NetValue,
    decimal VatValue,
    decimal GrossValue,
    decimal UnitCost)
{
    /// <summary>Net sales value minus the recorded cost.</summary>
    public decimal Margin => NetValue - decimal.Round(Quantity * UnitCost, 2, MidpointRounding.AwayFromZero);
}

public sealed record InvoiceDetails(
    int Id,
    InvoiceStatus Status,
    string? Number,
    string ExternalOrderId,
    string? PdfUrl,
    DateOnly IssueDate,
    DateTime IssuedAt,
    string ContractorName,
    string? ContractorNip,
    string UserEmail,
    int PaymentDays,
    string PaymentMethod,
    DocumentLink? StockDocument,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    decimal? RemoteGrossTotal,
    int Attempts,
    DateTime? NextAttemptAt,
    string? LastError,
    IReadOnlyList<InvoiceLineRow> Lines)
{
    public decimal Margin => Lines.Sum(line => line.Margin);
}

public sealed class InvoiceQueries(IDbContextFactory<RegalDbContext> dbContextFactory, ICurrentUser currentUser)
{
    public async Task<PagedResult<InvoiceListItem>> SearchAsync(
        InvoiceFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Invoices.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(i => i.Status == status);
        }

        if (filter.From is { } from)
        {
            query = query.Where(i => i.IssueDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(i => i.IssueDate <= to);
        }

        if (SearchPattern.Contains(filter.Term) is { } pattern)
        {
            query = query.Where(i =>
                EF.Functions.ILike(i.Number!, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(i.Contractor!.Name, pattern, SearchPattern.Escape)
                || EF.Functions.ILike(i.Contractor!.Nip!, pattern, SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id)
            .Skip(Paging.Skip(page, pageSize)).Take(Paging.Take(pageSize))
            .Select(i => new InvoiceListItem(i.Id, i.Number, i.Status, i.IssueDate, i.Contractor!.Name, i.GrossTotal))
            .ToListAsync(cancellationToken);

        return new PagedResult<InvoiceListItem>(items, total);
    }

    public async Task<InvoiceDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var header = await db.Invoices.AsNoTracking().Where(i => i.Id == id)
            .Select(i => new
            {
                Invoice = i,
                ContractorName = i.Contractor!.Name,
                ContractorNip = i.Contractor.Nip,
                UserEmail = i.User!.Email,
                StockDocument = i.StockDocument == null ? null : new DocumentLink(i.StockDocument.Id, i.StockDocument.Number),
                Message = db.OutboxMessages.Where(m => m.InvoiceId == i.Id)
                    .Select(m => new { m.Attempts, m.NextAttemptAt, m.LastError })
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var lines = await db.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == id).OrderBy(l => l.Id)
            .Select(l => new InvoiceLineRow(
                l.ProductName, l.Unit, l.Quantity, l.NetPrice, l.DiscountPercent, l.VatRate, l.NetValue, l.VatValue, l.GrossValue, l.UnitCost))
            .ToListAsync(cancellationToken);

        var invoice = header.Invoice;
        return new InvoiceDetails(
            invoice.Id, invoice.Status, invoice.Number, invoice.ExternalOrderId, invoice.PdfUrl, invoice.IssueDate, invoice.IssuedAt,
            header.ContractorName, header.ContractorNip, header.UserEmail, invoice.PaymentDays, invoice.PaymentMethod, header.StockDocument,
            invoice.NetTotal, invoice.VatTotal, invoice.GrossTotal, invoice.RemoteGrossTotal,
            header.Message?.Attempts ?? 0, header.Message?.NextAttemptAt, header.Message?.LastError, lines);
    }
}

public static class InvoiceLabels
{
    public static string Status(InvoiceStatus status) => status switch
    {
        InvoiceStatus.ToSend => "Do wysłania",
        InvoiceStatus.Sent => "Wysłana",
        InvoiceStatus.Failed => "Błąd wysyłki",
        InvoiceStatus.Cancelled => "Anulowana",
        _ => status.ToString(),
    };

    public static string PaymentMethod(string code) => code switch
    {
        "transfer" => "Przelew",
        "cash" => "Gotówka",
        "card" => "Karta",
        _ => code,
    };

    public static string VatRate(string rate) => char.IsAsciiDigit(rate[0]) ? $"{rate}%" : rate;
}
