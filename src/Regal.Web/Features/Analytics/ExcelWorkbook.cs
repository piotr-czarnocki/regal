using ClosedXML.Excel;

namespace Regal.Web.Features.Analytics;

/// <summary>One sheet: a header row and the rows of a table as shown on a page.</summary>
public sealed record ExcelSheet(string Name, IReadOnlyList<string> Headers, IEnumerable<IReadOnlyList<object?>> Rows);

/// <summary>
/// Builds .xlsx exports of the analyses (SPEC 5.17). Numbers stay numbers and dates stay dates, so
/// the spreadsheet can sum and sort them. Text is never a formula: text starting like one (=, +, -, @,
/// tab, carriage return) gets Excel's quote prefix, the defence against CSV/Excel injection (SPEC 9).
/// </summary>
public static class ExcelWorkbook
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static byte[] Build(params ExcelSheet[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var sheet in sheets)
        {
            var worksheet = workbook.Worksheets.Add(sheet.Name);
            for (var column = 0; column < sheet.Headers.Count; column++)
            {
                worksheet.Cell(1, column + 1).SetValue(sheet.Headers[column]);
            }

            worksheet.Row(1).Style.Font.Bold = true;
            var row = 2;
            foreach (var values in sheet.Rows)
            {
                for (var column = 0; column < values.Count; column++)
                {
                    Write(worksheet.Cell(row, column + 1), values[column]);
                }

                row++;
            }

            worksheet.SheetView.FreezeRows(1);
            worksheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>True when Excel could read the text as a formula.</summary>
    public static bool LooksLikeFormula(string text) => text.Length > 0 && FormulaStarts.Contains(text[0]);

    private static void Write(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case decimal number:
                cell.SetValue(number);
                break;
            case int number:
                cell.SetValue(number);
                break;
            case DateOnly date:
                cell.SetValue(date.ToDateTime(TimeOnly.MinValue));
                cell.Style.DateFormat.Format = "yyyy-mm-dd";
                break;
            default:
                var text = Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
                cell.SetValue(text);
                if (LooksLikeFormula(text))
                {
                    cell.Style.IncludeQuotePrefix = true;
                }

                break;
        }
    }
}
