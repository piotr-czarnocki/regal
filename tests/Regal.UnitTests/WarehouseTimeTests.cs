using Microsoft.Extensions.Time.Testing;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Warehouse;

namespace Regal.UnitTests;

public class WarehouseTimeTests
{
    private static DateOnly DateAt(int year, int month, int day, int hour, int minute) =>
        WarehouseTime.DateOf(new FakeTimeProvider(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)).GetUtcNow());

    [Fact]
    public void Half_past_eleven_utc_on_new_years_eve_is_already_next_year_in_warsaw()
    {
        var date = DateAt(2026, 12, 31, 23, 30);

        Assert.Equal(new DateOnly(2027, 1, 1), date);
        Assert.Equal("PZ/1/2027", DocumentNumber.Format(StockDocumentType.PZ, 1, date.Year));
    }

    [Fact]
    public void One_hour_earlier_in_winter_it_is_still_the_old_year() =>
        Assert.Equal(new DateOnly(2026, 12, 31), DateAt(2026, 12, 31, 22, 59));

    [Fact]
    public void Summer_time_is_two_hours_ahead_of_utc() =>
        Assert.Equal(new DateOnly(2026, 7, 1), DateAt(2026, 6, 30, 22, 30));

    [Theory]
    [InlineData(StockDocumentType.PZ, 1, 2026, "PZ/1/2026")]
    [InlineData(StockDocumentType.RW, 1234, 2027, "RW/1234/2027")]
    public void Number_has_the_type_the_sequence_and_the_year(StockDocumentType type, int number, int year, string expected) =>
        Assert.Equal(expected, DocumentNumber.Format(type, number, year));
}
