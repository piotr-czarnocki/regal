using Regal.Web.Features.Warehouse;

namespace Regal.UnitTests;

public class BatchAllocatorTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private static BatchAvailability Batch(int id, decimal quantity, DateOnly? expiry = null) =>
        new(id, $"P{id}", expiry, quantity);

    private static IEnumerable<(int BatchId, decimal Quantity)> Taken(AllocationPlan plan) =>
        plan.Allocations.Select(a => (a.Batch.BatchId, a.Quantity));

    private static AllocationPlan Automatic(decimal quantity, bool allowExpired, params BatchAvailability[] batches) =>
        BatchAllocator.Allocate(quantity, chosenBatchId: null, batches, allowExpired, Today);

    [Fact]
    public void Automatic_issue_takes_the_shortest_expiry_first()
    {
        var plan = Automatic(1, false, Batch(1, 5, Today.AddDays(30)), Batch(2, 5, Today.AddDays(5)), Batch(3, 5, Today.AddDays(10)));

        Assert.Equal([(2, 1m)], Taken(plan));
    }

    [Fact]
    public void Batches_without_a_date_go_last()
    {
        var plan = Automatic(7, false, Batch(1, 5), Batch(2, 5, Today.AddDays(100)));

        Assert.Equal([(2, 5m), (1, 2m)], Taken(plan));
    }

    [Fact]
    public void On_equal_dates_the_oldest_batch_goes_first()
    {
        var date = Today.AddDays(3);

        var plan = Automatic(1, false, Batch(9, 5, date), Batch(4, 5, date));

        Assert.Equal([(4, 1m)], Taken(plan));
    }

    [Fact]
    public void Among_batches_without_a_date_the_oldest_goes_first()
    {
        var plan = Automatic(1, false, Batch(9, 5), Batch(4, 5));

        Assert.Equal([(4, 1m)], Taken(plan));
    }

    [Fact]
    public void Quantity_is_split_over_as_many_batches_as_needed_and_no_more()
    {
        var plan = Automatic(7.5m, false, Batch(1, 3, Today.AddDays(1)), Batch(2, 4, Today.AddDays(2)), Batch(3, 4, Today.AddDays(3)), Batch(4, 9, Today.AddDays(4)));

        Assert.Equal([(1, 3m), (2, 4m), (3, 0.5m)], Taken(plan));
        Assert.Null(plan.Error);
    }

    [Fact]
    public void Issue_for_wz_skips_expired_batches()
    {
        var plan = Automatic(2, false, Batch(1, 5, Today.AddDays(-1)), Batch(2, 5, Today.AddDays(9)));

        Assert.Equal([(2, 2m)], Taken(plan));
    }

    [Fact]
    public void Issue_for_rw_takes_expired_batches_first()
    {
        var plan = Automatic(2, true, Batch(1, 5, Today.AddDays(9)), Batch(2, 5, Today.AddDays(-1)));

        Assert.Equal([(2, 2m)], Taken(plan));
    }

    [Fact]
    public void A_batch_can_be_issued_on_its_expiry_date()
    {
        Assert.False(BatchAllocator.IsExpired(Today, Today));
        Assert.True(BatchAllocator.IsExpired(Today.AddDays(-1), Today));
        Assert.False(BatchAllocator.IsExpired(null, Today));
    }

    [Fact]
    public void Shortage_gives_an_error_and_no_allocations()
    {
        var plan = Automatic(11, false, Batch(1, 5), Batch(2, 5));

        Assert.Empty(plan.Allocations);
        Assert.StartsWith("brak towaru", plan.Error);
    }

    [Fact]
    public void Shortage_caused_by_expired_goods_says_so()
    {
        var plan = Automatic(6, false, Batch(1, 5), Batch(2, 5, Today.AddDays(-1)));

        Assert.Contains("Przeterminowane", plan.Error);
    }

    [Fact]
    public void Exact_stock_is_enough()
    {
        Assert.Null(Automatic(10, false, Batch(1, 5), Batch(2, 5)).Error);
    }

    [Fact]
    public void Chosen_batch_is_used_even_when_another_expires_sooner()
    {
        var plan = BatchAllocator.Allocate(2, 2, [Batch(1, 5, Today.AddDays(1)), Batch(2, 5, Today.AddDays(9))], false, Today);

        Assert.Equal([(2, 2m)], Taken(plan));
    }

    [Fact]
    public void Chosen_expired_batch_is_refused_for_wz_and_allowed_for_rw()
    {
        BatchAvailability[] batches = [Batch(1, 5, Today.AddDays(-1))];

        Assert.Contains("przeterminowana", BatchAllocator.Allocate(1, 1, batches, allowExpired: false, Today).Error);
        Assert.Null(BatchAllocator.Allocate(1, 1, batches, allowExpired: true, Today).Error);
    }

    [Fact]
    public void Chosen_batch_of_another_product_is_refused()
    {
        Assert.NotNull(BatchAllocator.Allocate(1, 99, [Batch(1, 5)], false, Today).Error);
    }

    [Fact]
    public void Chosen_batch_does_not_spill_over_into_others()
    {
        var plan = BatchAllocator.Allocate(6, 1, [Batch(1, 5), Batch(2, 5)], false, Today);

        Assert.Empty(plan.Allocations);
        Assert.Contains("brak towaru w partii P1", plan.Error);
    }
}
