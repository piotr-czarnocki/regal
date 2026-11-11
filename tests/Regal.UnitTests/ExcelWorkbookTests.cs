using ClosedXML.Excel;
using Regal.Web.Features.Analytics;

namespace Regal.UnitTests;

/// <summary>Excel exports (SPEC 5.17): real numbers and dates, and no text that Excel would run.</summary>
public class ExcelWorkbookTests
{
    private static XLWorkbook Open(byte[] content) => new(new MemoryStream(content));

    [Fact]
    public void Headers_numbers_and_dates_keep_their_types()
    {
        var content = ExcelWorkbook.Build(new ExcelSheet("Arkusz", ["Nazwa", "Kwota", "Sztuki", "Data", "Puste"],
            [["Chleb", 12.34m, 3, new DateOnly(2026, 11, 10), null]]));

        using var workbook = Open(content);
        var sheet = workbook.Worksheet("Arkusz");
        Assert.Equal("Nazwa", sheet.Cell(1, 1).GetString());
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 2).DataType);
        Assert.Equal(12.34, sheet.Cell(2, 2).GetDouble());
        Assert.Equal(3, sheet.Cell(2, 3).GetDouble());
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 4).DataType);
        Assert.Equal(new DateTime(2026, 11, 10), sheet.Cell(2, 4).GetDateTime());
        Assert.True(sheet.Cell(2, 5).IsEmpty());
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://zly.example\",\"klik\")")]
    [InlineData("+48 600 000 000")]
    [InlineData("-5% rabatu")]
    [InlineData("@SUM(A1:A2)")]
    [InlineData("\t=1+1")]
    public void Text_that_starts_like_a_formula_stays_text_with_a_quote_prefix(string text)
    {
        var content = ExcelWorkbook.Build(new ExcelSheet("A", ["Nazwa"], [[text]]));

        using var workbook = Open(content);
        var cell = workbook.Worksheet("A").Cell(2, 1);
        Assert.False(cell.HasFormula);
        Assert.Equal(XLDataType.Text, cell.DataType);
        Assert.Equal(text, cell.GetString());
        Assert.True(cell.Style.IncludeQuotePrefix);
    }

    [Fact]
    public void Ordinary_text_has_no_prefix() =>
        Assert.False(Open(ExcelWorkbook.Build(new ExcelSheet("A", ["Nazwa"], [["Chleb"]]))).Worksheet("A").Cell(2, 1).Style.IncludeQuotePrefix);

    [Fact]
    public void The_sales_export_has_products_categories_and_the_comparison()
    {
        var summary = new SalesSummary(100m, 40m, 2);
        var comparison = new PeriodComparison(
            new PeriodSummary(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), summary),
            new PeriodSummary(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 31), summary),
            new PeriodSummary(new DateOnly(2025, 11, 1), new DateOnly(2025, 11, 10), summary));

        var content = AnalyticsExports.Sales(
            [new ProductSales(1, "Chleb", "Pieczywo", "szt", 10, 100m, 40m)], [new CategorySales("Pieczywo", 100m, 40m)], comparison);

        using var workbook = Open(content);
        Assert.Equal(["Produkty", "Kategorie", "Porównanie"], workbook.Worksheets.Select(w => w.Name));
        Assert.Equal(60, workbook.Worksheet("Produkty").Cell(2, 7).GetDouble());
        Assert.Equal("Rok wcześniej", workbook.Worksheet("Porównanie").Cell(4, 1).GetString());
    }

    [Fact]
    public void The_sales_export_leaves_out_a_comparison_that_was_not_shown()
    {
        var content = AnalyticsExports.Sales([new ProductSales(1, "Chleb", "Pieczywo", "szt", 10, 100m, 40m)], [], comparison: null);

        using var workbook = Open(content);
        Assert.Equal(["Produkty", "Kategorie"], workbook.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public void File_names_carry_the_period() =>
        Assert.Equal("klienci-2026-01-01-2026-11-10", AnalyticsExports.Period("klienci", new AnalyticsFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 10))));
}
