using Regal.Web.Features.Warehouse;

namespace Regal.UnitTests;

public class AverageCostTests
{
    [Fact]
    public void First_receipt_at_zero_stock_sets_the_average_to_its_cost() =>
        Assert.Equal(12.5m, AverageCost.AfterReceipt(stock: 0, average: 0, received: 4, unitCost: 12.5m));

    [Fact]
    public void Receipt_at_zero_stock_ignores_the_old_average() =>
        Assert.Equal(7m, AverageCost.AfterReceipt(stock: 0, average: 100m, received: 2, unitCost: 7m));

    [Fact]
    public void Receipt_weights_the_average_by_quantity() =>
        Assert.Equal(6.5m, AverageCost.AfterReceipt(stock: 30, average: 6m, received: 10, unitCost: 8m));

    [Fact]
    public void Result_has_four_decimal_places() =>
        Assert.Equal(1.3333m, AverageCost.AfterReceipt(stock: 2, average: 1m, received: 1, unitCost: 2m));

    [Fact]
    public void Half_is_rounded_away_from_zero() =>
        Assert.Equal(1.0003m, AverageCost.AfterReceipt(stock: 1, average: 1.0002m, received: 1, unitCost: 1.0003m));

    [Fact]
    public void Issue_correction_at_the_issue_cost_keeps_an_unchanged_average() =>
        Assert.Equal(6m, AverageCost.AfterReceipt(stock: 5, average: 6m, received: 5, unitCost: 6m));

    [Fact]
    public void Receipt_correction_takes_the_receipt_out_of_the_average() =>
        Assert.Equal(5m, AverageCost.AfterReceiptReversal(stock: 20, average: 6m, reversedQuantity: 10, reversedValue: 70m));

    [Fact]
    public void Receipt_correction_leaving_nothing_keeps_the_average() =>
        Assert.Equal(6m, AverageCost.AfterReceiptReversal(stock: 10, average: 6m, reversedQuantity: 10, reversedValue: 70m));

    [Fact]
    public void Receipt_correction_with_a_negative_result_is_clipped_to_zero() =>
        Assert.Equal(0m, AverageCost.AfterReceiptReversal(stock: 10, average: 1m, reversedQuantity: 5, reversedValue: 50m));
}
