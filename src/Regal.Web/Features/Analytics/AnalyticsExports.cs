using System.Globalization;
using Regal.Web.Features.Warehouse;

namespace Regal.Web.Features.Analytics;

/// <summary>The tables of the analysis pages as Excel sheets, with the same columns as on screen.</summary>
public static class AnalyticsExports
{
    public static string Period(string name, AnalyticsFilter filter) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}-{filter.From:yyyy-MM-dd}-{filter.To:yyyy-MM-dd}");

    public static byte[] Sales(IEnumerable<ProductSales> products, IEnumerable<CategorySales> categories, PeriodComparison? comparison) =>
        ExcelWorkbook.Build(
            new ExcelSheet("Produkty", ["Produkt", "Kategoria", "Ilość", "Jednostka", "Netto", "Koszt", "Marża"],
                products.Select(p => Row(p.Name, p.CategoryName, p.Quantity, p.Unit, p.NetSales, p.Cost, p.Margin))),
            new ExcelSheet("Kategorie", ["Kategoria", "Netto", "Koszt", "Marża"],
                categories.Select(c => Row(c.CategoryName, c.NetSales, c.Cost, c.Margin))),
            new ExcelSheet("Porównanie", ["Okres", "Od", "Do", "Netto", "Marża", "Faktury"],
                comparison is null
                    ? []
                    : new[] { ("Wybrany", comparison.Current), ("Poprzedni", comparison.Previous), ("Rok wcześniej", comparison.YearEarlier) }
                        .Select(p => Row(p.Item1, p.Item2.From, p.Item2.To, p.Item2.Sales.NetSales, p.Item2.Sales.Margin, p.Item2.Sales.InvoiceCount))));

    public static byte[] Abc(IEnumerable<AbcRow> rows) =>
        ExcelWorkbook.Build(new ExcelSheet("ABC", ["Klasa", "Produkt", "Netto", "Udział %", "Udział narastająco %"],
            rows.Select(r => Row(r.Class.ToString(), r.Name, r.NetSales, r.Share, r.CumulativeShare))));

    public static byte[] SlowMoving(IEnumerable<SlowMovingRow> rows) =>
        ExcelWorkbook.Build(new ExcelSheet("Zalegające", ["Produkt", "Kategoria", "Stan", "Jednostka", "Wartość", "Ostatnia sprzedaż"],
            rows.Select(r => Row(r.Name, r.CategoryName, r.Quantity, r.Unit, r.Value, r.LastSale is { } date ? date : "nigdy"))));

    public static byte[] Customers(IEnumerable<CustomerSales> rows) =>
        ExcelWorkbook.Build(new ExcelSheet("Klienci", ["Klient", "NIP", "Faktury", "Netto", "Koszt", "Marża", "Średnia faktura", "Ostatni zakup"],
            rows.Select(r => Row(r.Name, r.Nip, r.InvoiceCount, r.NetSales, r.Cost, r.Margin, r.AverageInvoice, r.LastPurchase))));

    public static byte[] Forecast(IEnumerable<ForecastRow> rows) =>
        ExcelWorkbook.Build(new ExcelSheet("Prognoza", ["Towar", "Jednostka", "Sprzedano w historii", "Średnio dziennie", "Dostępne", "Zapas na (dni)", "Zamówić"],
            rows.Select(r => Row(r.Name, r.Unit, r.SoldInHistory, r.DailyAverage, r.Available, r.DaysOfCover, r.SuggestedOrder))));

    public static string Today(string name, DateOnly today) => string.Create(CultureInfo.InvariantCulture, $"{name}-{today:yyyy-MM-dd}");

    private static IReadOnlyList<object?> Row(params object?[] values) => values;
}
