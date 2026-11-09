namespace Regal.Web.Features.Warehouse;

public sealed record BatchAvailability(int BatchId, string Number, DateOnly? ExpiryDate, decimal Quantity);

public sealed record BatchAllocation(BatchAvailability Batch, decimal Quantity);

/// <summary>Error, in Polish, is set when the quantity cannot be issued; Allocations is then empty.</summary>
public sealed record AllocationPlan(IReadOnlyList<BatchAllocation> Allocations, string? Error)
{
    public static AllocationPlan Fail(string error) => new([], error);
}

/// <summary>Decides which batches an issue takes from. The caller holds the product's lock.</summary>
public static class BatchAllocator
{
    /// <summary>Goods may be issued up to and including the expiry date.</summary>
    public static bool IsExpired(DateOnly? expiryDate, DateOnly today) => expiryDate < today;

    /// <param name="chosenBatchId">The batch picked by hand; null means first-expired-first-out.</param>
    /// <param name="allowExpired">False for WZ, which never ships expired goods, also when picked by hand.</param>
    public static AllocationPlan Allocate(
        decimal quantity,
        int? chosenBatchId,
        IReadOnlyCollection<BatchAvailability> batches,
        bool allowExpired,
        DateOnly today) =>
        chosenBatchId is { } batchId
            ? AllocateChosen(quantity, batchId, batches, allowExpired, today)
            : AllocateFirstExpired(quantity, batches, allowExpired, today);

    private static AllocationPlan AllocateChosen(
        decimal quantity,
        int batchId,
        IReadOnlyCollection<BatchAvailability> batches,
        bool allowExpired,
        DateOnly today)
    {
        var batch = batches.FirstOrDefault(b => b.BatchId == batchId);
        if (batch is null)
        {
            return AllocationPlan.Fail("wybrana partia nie należy do tego produktu albo nie ma na niej towaru.");
        }

        if (!allowExpired && IsExpired(batch.ExpiryDate, today))
        {
            return AllocationPlan.Fail($"partia {batch.Number} jest przeterminowana i nie może zostać wydana dokumentem WZ.");
        }

        return batch.Quantity < quantity
            ? AllocationPlan.Fail($"brak towaru w partii {batch.Number}: dostępne {batch.Quantity:0.###}, potrzeba {quantity:0.###}.")
            : new AllocationPlan([new BatchAllocation(batch, quantity)], null);
    }

    private static AllocationPlan AllocateFirstExpired(
        decimal quantity,
        IReadOnlyCollection<BatchAvailability> batches,
        bool allowExpired,
        DateOnly today)
    {
        var usable = batches
            .Where(b => allowExpired || !IsExpired(b.ExpiryDate, today))
            .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(b => b.BatchId) // ids grow with time, so the oldest batch goes first on a tie
            .ToList();

        var available = usable.Sum(b => b.Quantity);
        if (available < quantity)
        {
            var expired = batches.Sum(b => b.Quantity) - available;
            return AllocationPlan.Fail(
                $"brak towaru: dostępne {available:0.###}, potrzeba {quantity:0.###}."
                + (expired > 0 ? $" Przeterminowane {expired:0.###} nie mogą zostać wydane." : string.Empty));
        }

        var allocations = new List<BatchAllocation>();
        var remaining = quantity;
        foreach (var batch in usable)
        {
            var taken = Math.Min(batch.Quantity, remaining);
            allocations.Add(new BatchAllocation(batch, taken));
            remaining -= taken;
            if (remaining == 0)
            {
                break;
            }
        }

        return new AllocationPlan(allocations, null);
    }
}
