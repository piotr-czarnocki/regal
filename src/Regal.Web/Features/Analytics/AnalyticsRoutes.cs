namespace Regal.Web.Features.Analytics;

public static class AnalyticsRoutes
{
    public const string Products = "/analytics/products";

    public const string Contractors = "/analytics/customers";

    public static string Product(int id) => $"/analytics/products/{id}";

    public static string Contractor(int id) => $"/analytics/contractors/{id}";

    /// <summary>"+12,5%", "−3,0%" or "—" when there is nothing to compare with.</summary>
    public static string Change(decimal? percent) =>
        percent is { } value ? $"{(value > 0 ? "+" : null)}{value:0.0}%" : "—";

    public static string Percent(decimal? percent) => percent is { } value ? $"{value:0.0}%" : "—";
}
