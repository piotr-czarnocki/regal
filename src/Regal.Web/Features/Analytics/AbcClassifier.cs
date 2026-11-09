namespace Regal.Web.Features.Analytics;

public enum AbcClass
{
    A,
    B,
    C,
}

public sealed record AbcItem(int ProductId, string Name, decimal NetSales);

/// <summary>Share and CumulativeShare are percentages of the period's net sales.</summary>
public sealed record AbcRow(int ProductId, string Name, decimal NetSales, decimal Share, decimal CumulativeShare, AbcClass Class);

public static class AbcClassifier
{
    public const decimal ALimit = 80m;
    public const decimal BLimit = 95m;

    /// <summary>
    /// Sorted by sales, a product is A while the products before it make less than 80% of sales,
    /// B below 95%, C after that. So the product that crosses a limit still belongs to the class it
    /// started in, and the first product is always A.
    /// </summary>
    public static IReadOnlyList<AbcRow> Classify(IEnumerable<AbcItem> items)
    {
        var sorted = items.OrderByDescending(i => i.NetSales).ThenBy(i => i.ProductId).ToList();
        var total = sorted.Where(i => i.NetSales > 0).Sum(i => i.NetSales);
        if (total == 0)
        {
            return sorted.Select(i => new AbcRow(i.ProductId, i.Name, i.NetSales, 0, 0, AbcClass.C)).ToList();
        }

        var rows = new List<AbcRow>(sorted.Count);
        var before = 0m;
        foreach (var item in sorted)
        {
            var value = Math.Max(item.NetSales, 0);
            var share = value * 100 / total;
            var cls = value == 0 ? AbcClass.C : before < ALimit ? AbcClass.A : before < BLimit ? AbcClass.B : AbcClass.C;
            before += share;
            rows.Add(new AbcRow(item.ProductId, item.Name, item.NetSales, Round(share), Round(Math.Min(before, 100)), cls));
        }

        return rows;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
