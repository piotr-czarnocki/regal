namespace Regal.Web.Features.Warehouse;

/// <summary>The moving average cost of a product. All inputs and results are per unit, in PLN.</summary>
public static class AverageCost
{
    private const int Decimals = 4;

    /// <summary>A receipt, or a correction that brings issued goods back at their issue cost.</summary>
    public static decimal AfterReceipt(decimal stock, decimal average, decimal received, decimal unitCost) =>
        Round(((stock * average) + (received * unitCost)) / (stock + received));

    /// <summary>
    /// A correction that takes back received goods. With nothing left the average stays as it was.
    /// A negative result can follow from earlier rounding or corrections and is clipped to zero.
    /// </summary>
    public static decimal AfterReceiptReversal(decimal stock, decimal average, decimal reversedQuantity, decimal reversedValue)
    {
        var remaining = stock - reversedQuantity;
        return remaining <= 0
            ? average
            : Math.Max(0, Round(((stock * average) - reversedValue) / remaining));
    }

    private static decimal Round(decimal value) => decimal.Round(value, Decimals, MidpointRounding.AwayFromZero);
}
