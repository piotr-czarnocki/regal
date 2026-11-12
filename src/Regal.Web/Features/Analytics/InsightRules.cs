namespace Regal.Web.Features.Analytics;

/// <summary>Calculations of the product and contractor analysis (SPEC 5.20); no database.</summary>
public static class InsightRules
{
    /// <summary>
    /// The average number of days between purchase days, from the first to the last; empty with
    /// fewer than two days, when there is no gap to measure.
    /// </summary>
    public static decimal? AverageGapDays(IReadOnlyCollection<DateOnly> purchaseDays)
    {
        var days = purchaseDays.Distinct().OrderBy(d => d).ToList();
        return days.Count < 2
            ? null
            : decimal.Round((decimal)(days[^1].DayNumber - days[0].DayNumber) / (days.Count - 1), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// From paid invoices: the average days from issue to payment, and the share paid by the due date
    /// (issue date plus payment days). Both empty when nothing was paid.
    /// </summary>
    public static (decimal? AverageDaysToPay, decimal? PaidOnTimePercent) PaymentHabits(
        IReadOnlyCollection<(DateOnly IssueDate, int PaymentDays, DateOnly PaidDate)> paid)
    {
        if (paid.Count == 0)
        {
            return (null, null);
        }

        var average = (decimal)paid.Sum(p => Math.Max(0, p.PaidDate.DayNumber - p.IssueDate.DayNumber)) / paid.Count;
        var onTime = paid.Count(p => p.PaidDate <= p.IssueDate.AddDays(p.PaymentDays));
        return (decimal.Round(average, 1, MidpointRounding.AwayFromZero), decimal.Round(onTime * 100m / paid.Count, 1, MidpointRounding.AwayFromZero));
    }
}
