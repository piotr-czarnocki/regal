using System.Globalization;
using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Warehouse;

public static class WarehouseTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    /// <summary>The calendar date in Poland: 31 December 23:30 UTC is already 1 January there.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    /// <summary>
    /// A stored moment (UTC) as a person in Poland reads it. Never ToLocalTime(): that is the server's
    /// zone, which in a Docker container is UTC. A value read without a kind counts as UTC, as stored.
    /// </summary>
    /// <summary>The wall-clock time in Poland now, e.g. for a file name.</summary>
    public static DateTime LocalNow(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone).DateTime;

    public static string Display(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
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
