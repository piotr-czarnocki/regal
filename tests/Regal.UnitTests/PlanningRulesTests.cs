using Regal.Web.Features.Analytics;

namespace Regal.UnitTests;

public class PlanningRulesTests
{
    [Fact]
    public void The_previous_period_has_the_same_number_of_days_just_before()
    {
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), PlanningRules.PreviousPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)));
        Assert.Equal((new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 9)), PlanningRules.PreviousPeriod(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10)));
    }

    [Fact]
    public void A_year_earlier_turns_29_february_into_28_february() =>
        Assert.Equal((new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 1)), PlanningRules.YearEarlier(new DateOnly(2028, 2, 29), new DateOnly(2028, 3, 1)));

    [Theory]
    [InlineData(150, 100, 50.0)]
    [InlineData(50, 100, -50.0)]
    [InlineData(10, -20, 150.0)]
    [InlineData(1, 3, -66.7)]
    public void Change_is_in_percent_of_the_earlier_value(double current, double earlier, double expected) =>
        Assert.Equal((decimal)expected, PlanningRules.ChangePercent((decimal)current, (decimal)earlier));

    [Fact]
    public void Nothing_to_compare_with_gives_no_change() => Assert.Null(PlanningRules.ChangePercent(10, 0));

    [Theory]
    [InlineData(28, 28, 1)]
    [InlineData(-5, 28, 0)]
    [InlineData(10, 0, 0)]
    public void Daily_average_divides_by_the_history(double sold, int days, double expected) =>
        Assert.Equal((decimal)expected, PlanningRules.DailyAverage((decimal)sold, days));

    [Fact]
    public void Cover_is_in_days_and_empty_when_nothing_sells()
    {
        Assert.Equal(2.5m, PlanningRules.DaysOfCover(5, 2));
        Assert.Null(PlanningRules.DaysOfCover(5, 0));
        Assert.Equal(0m, PlanningRules.DaysOfCover(-3, 2));
    }

    [Theory]
    [InlineData(1.5, 14, 10, "szt", 11)]
    [InlineData(1.5, 14, 30, "szt", 0)]
    [InlineData(0.333, 3, 0, "kg", 0.999)]
    [InlineData(0.3333, 3, 0, "kg", 1.0)]
    [InlineData(1, 7, -2, "szt", 7)]
    public void The_order_covers_the_horizon_rounded_up(double daily, int horizon, double available, string unit, double expected) =>
        Assert.Equal((decimal)expected, PlanningRules.SuggestedOrder((decimal)daily, horizon, (decimal)available, unit));

    [Theory]
    [InlineData(0, 14)]
    [InlineData(28, 0)]
    [InlineData(366, 14)]
    public void Days_out_of_range_are_refused(int history, int horizon) => Assert.NotNull(PlanningRules.ValidateDays(history, horizon));
}
