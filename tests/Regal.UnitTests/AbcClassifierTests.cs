using Regal.Web.Features.Analytics;

namespace Regal.UnitTests;

public class AbcClassifierTests
{
    private static IReadOnlyList<AbcRow> Classify(params decimal[] sales) =>
        AbcClassifier.Classify(sales.Select((value, i) => new AbcItem(i + 1, $"P{i + 1}", value)));

    [Fact]
    public void Classic_split_gives_a_b_and_c()
    {
        var rows = Classify(80, 15, 5);

        Assert.Equal([AbcClass.A, AbcClass.B, AbcClass.C], rows.Select(r => r.Class));
        Assert.Equal([80m, 15m, 5m], rows.Select(r => r.Share));
        Assert.Equal([80m, 95m, 100m], rows.Select(r => r.CumulativeShare));
    }

    [Fact]
    public void Product_that_crosses_the_limit_stays_in_the_class_it_started_in()
    {
        // 70% before the second product, so it is A although it ends at 90%.
        var rows = Classify(70, 20, 6, 4);

        Assert.Equal([AbcClass.A, AbcClass.A, AbcClass.B, AbcClass.C], rows.Select(r => r.Class));
    }

    [Fact]
    public void Single_product_is_a()
    {
        var row = Assert.Single(Classify(123.45m));

        Assert.Equal(AbcClass.A, row.Class);
        Assert.Equal(100m, row.Share);
    }

    [Fact]
    public void Order_is_by_sales_and_then_by_id()
    {
        var rows = AbcClassifier.Classify([new AbcItem(5, "E", 10), new AbcItem(2, "B", 30), new AbcItem(3, "C", 10)]);

        Assert.Equal([2, 3, 5], rows.Select(r => r.ProductId));
    }

    [Fact]
    public void Products_without_sales_value_are_c()
    {
        var rows = Classify(100, 0);

        Assert.Equal(AbcClass.C, rows[1].Class);
        Assert.Equal(0m, rows[1].Share);
    }

    [Fact]
    public void No_sales_at_all_gives_c_with_zero_shares()
    {
        Assert.All(Classify(0, 0), row => Assert.Equal((AbcClass.C, 0m), (row.Class, row.Share)));
        Assert.Empty(Classify());
    }

    [Fact]
    public void Many_equal_products_split_by_position()
    {
        var rows = Classify(Enumerable.Repeat(1m, 20).ToArray());

        Assert.Equal(16, rows.Count(r => r.Class == AbcClass.A)); // before each: 0%, 5%, … 75%
        Assert.Equal(3, rows.Count(r => r.Class == AbcClass.B));  // 80%, 85%, 90%
        Assert.Equal(1, rows.Count(r => r.Class == AbcClass.C));  // 95%
    }
}
