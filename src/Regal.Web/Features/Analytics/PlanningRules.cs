namespace Regal.Web.Features.Analytics;

/// <summary>Calculations of the comparison of periods and the order forecast (SPEC 5.16); no database.</summary>
public static class PlanningRules
{
    public const int DefaultHistoryDays = 28;
    public const int DefaultHorizonDays = 14;
    public const int MaxDays = 365;

    /// <summary>The same number of days just before the period, both dates included.</summary>
    public static (DateOnly From, DateOnly To) PreviousPeriod(DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        return (from.AddDays(-days), from.AddDays(-1));
    }

    /// <summary>The same dates a year earlier; 29 February becomes 28 February.</summary>
    public static (DateOnly From, DateOnly To) YearEarlier(DateOnly from, DateOnly to) => (from.AddYears(-1), to.AddYears(-1));

    /// <summary>Change in percent, rounded to one place; empty when there is nothing to compare with.</summary>
    public static decimal? ChangePercent(decimal current, decimal earlier) =>
        earlier == 0 ? null : decimal.Round((current - earlier) * 100 / Math.Abs(earlier), 1, MidpointRounding.AwayFromZero);

    /// <summary>Average sold per day over the history, which ends today.</summary>
    public static decimal DailyAverage(decimal soldInHistory, int historyDays) =>
        historyDays <= 0 ? 0 : Math.Max(0, soldInHistory) / historyDays;

    /// <summary>How many days the available stock lasts at that pace; empty when nothing sells.</summary>
    public static decimal? DaysOfCover(decimal available, decimal dailyAverage) =>
        dailyAverage <= 0 ? null : decimal.Round(Math.Max(0, available) / dailyAverage, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// What to order so that the horizon is covered: expected sales minus what is available, never
    /// below zero. Pieces are rounded up to whole units, other units up to three decimals.
    /// </summary>
    public static decimal SuggestedOrder(decimal dailyAverage, int horizonDays, decimal available, string unit)
    {
        var needed = dailyAverage * horizonDays - Math.Max(0, available);
        if (needed <= 0)
        {
            return 0;
        }

        return string.Equals(unit.Trim(), "szt", StringComparison.OrdinalIgnoreCase)
            ? decimal.Ceiling(needed)
            : decimal.Ceiling(needed * 1000) / 1000;
    }

    /// <summary>Null when valid; otherwise the message for the person.</summary>
    public static string? ValidateDays(int historyDays, int horizonDays) =>
        historyDays is < 1 or > MaxDays || horizonDays is < 1 or > MaxDays
            ? $"Liczba dni historii i horyzontu musi wynosić od 1 do {MaxDays}."
            : null;
}
