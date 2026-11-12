using Regal.Web.Features.Analytics;

namespace Regal.UnitTests;

public sealed class InsightRulesTests
{
    private static DateOnly D(int day) => new(2026, 10, day);

    [Fact]
    public void Gap_needs_two_purchase_days() =>
        Assert.Null(InsightRules.AverageGapDays([D(1), D(1)]));

    [Fact]
    public void Gap_is_the_span_divided_by_the_gaps_between_distinct_days() =>
        // 1, 4, 4, 11: three distinct days, two gaps, ten days → 5.
        Assert.Equal(5m, InsightRules.AverageGapDays([D(4), D(1), D(11), D(4)]));

    [Fact]
    public void No_paid_invoice_means_no_habit() =>
        Assert.Equal((null, null), InsightRules.PaymentHabits([]));

    [Fact]
    public void Payment_habits_count_days_to_pay_and_payments_by_the_due_date()
    {
        var habits = InsightRules.PaymentHabits(
        [
            (D(1), 14, D(15)), // on the due date: on time, 14 days
            (D(1), 14, D(16)), // a day late, 15 days
            (D(10), 0, D(10)), // cash, 0 days
        ]);

        Assert.Equal((9.7m, 66.7m), habits);
    }
}
