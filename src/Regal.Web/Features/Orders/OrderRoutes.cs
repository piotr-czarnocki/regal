using System.Globalization;

namespace Regal.Web.Features.Orders;

public static class OrderRoutes
{
    public const string List = "/orders";

    public const string New = "/orders/new";

    public static string Details(int id) => string.Create(CultureInfo.InvariantCulture, $"/orders/{id}");

    /// <summary>The invoice form filled from the order.</summary>
    public static string Invoice(int id) => string.Create(CultureInfo.InvariantCulture, $"/invoices/new?order={id}");
}
