using System.Globalization;
using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Warehouse;

public static class WarehouseTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    /// <summary>The calendar date in Poland: 31 December 23:30 UTC is already 1 January there.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);
}

public static class DocumentNumber
{
    public static string Format(StockDocumentType type, int number, int year) =>
        string.Create(CultureInfo.InvariantCulture, $"{type}/{number}/{year}");
}

/// <summary>Named because the Warehouse namespace hides the Warehouse entity in this folder.</summary>
internal static class MainWarehouse
{
    public const int Id = Data.Entities.Warehouse.MainWarehouseId;
}
