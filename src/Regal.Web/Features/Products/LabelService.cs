using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Regal.Web.Data;
using Regal.Web.Features.Auth;
using Regal.Web.Features.Invoices;
using Regal.Web.Features.Warehouse;

namespace Regal.Web.Features.Products;

public sealed record LabelRequest(int ProductId, int Copies);

/// <summary>Either the PDF or a message in Polish, ready to show.</summary>
public sealed record LabelResult(byte[]? Pdf, string? Error);

/// <summary>
/// A4 sheets of 3 × 8 labels (70 × 37 mm, a common self-adhesive format): name, SKU, gross price
/// and the EAN-13 as bars and digits (SPEC 5.18). Built with QuestPDF under its Community licence.
/// </summary>
public sealed class LabelService(IDbContextFactory<RegalDbContext> dbContextFactory, ICurrentUser currentUser)
{
    public const int MaxCopies = 100;
    public const int MaxLabels = 480; // 20 sheets
    private const int Columns = 3;

    static LabelService() => QuestPDF.Settings.License = LicenseType.Community;

    public async Task<LabelResult> BuildAsync(IReadOnlyList<LabelRequest> requests, CancellationToken cancellationToken = default)
    {
        await currentUser.RequireUserIdAsync(Roles.Administrator);
        if (requests.Count == 0)
        {
            return new LabelResult(null, "Zaznacz produkty, dla których chcesz wydrukować etykiety.");
        }

        if (requests.Any(r => r.Copies is < 1 or > MaxCopies))
        {
            return new LabelResult(null, $"Liczba kopii musi wynosić od 1 do {MaxCopies}.");
        }

        if (requests.Sum(r => r.Copies) > MaxLabels)
        {
            return new LabelResult(null, $"Jednorazowo można wydrukować najwyżej {MaxLabels} etykiet.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var ids = requests.Select(r => r.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Sku, p.Ean, p.NetPrice, p.VatRate, p.Unit })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        if (products.Count != ids.Distinct().Count())
        {
            return new LabelResult(null, "Nie znaleziono produktu.");
        }

        var labels = requests
            .SelectMany(r => Enumerable.Repeat(products[r.ProductId], r.Copies))
            .Select(p => new Label(p.Name, p.Sku, p.Ean, InvoiceRules.Amounts(1, p.NetPrice, 0, p.VatRate).Gross, p.Unit))
            .ToList();

        return new LabelResult(Render(labels), null);
    }

    private sealed record Label(string Name, string Sku, string? Ean, decimal GrossPrice, string Unit);

    private static byte[] Render(IReadOnlyList<Label> labels) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginVertical(0.5f, Unit.Millimetre); // 8 rows of 37 mm take 296 of 297 mm, as on the label sheets
            page.MarginHorizontal(0);
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (var i = 0; i < Columns; i++)
                    {
                        columns.RelativeColumn();
                    }
                });

                foreach (var label in labels)
                {
                    table.Cell().Height(37, Unit.Millimetre).Padding(3, Unit.Millimetre).Column(column =>
                    {
                        column.Item().Text(label.Name).Bold().FontSize(9).ClampLines(2);
                        // One line each, whatever their length: a long SKU wrapping onto more lines pushed the
                        // label over its 37 mm and the table moved it to the next sheet.
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().AlignBottom().Text(label.Sku).FontSize(7).ClampLines(1);
                            row.AutoItem().PaddingLeft(1, Unit.Millimetre).Text(string.Create(CultureInfo.GetCultureInfo("pl-PL"), $"{label.GrossPrice:N2} zł / {label.Unit}")).FontSize(9).Bold();
                        });
                        if (label.Ean is { } ean && Ean13.IsValid(ean))
                        {
                            // 113 × 40 units in 13 mm of height gives about 37 mm of width: a module of 0.32 mm, close to the
                            // nominal 0.33 mm of EAN-13. The default fit (to the width) made the bars far too tall.
                            column.Item().PaddingTop(1, Unit.Millimetre).Height(13, Unit.Millimetre).AlignCenter().Width(37, Unit.Millimetre)
                                .Svg(Ean13Barcode.Svg(ean, height: 40)).FitArea();
                            column.Item().AlignCenter().Text(ean).FontSize(8).LetterSpacing(0.1f);
                        }
                    });
                }
            });
        })).GeneratePdf();
}
