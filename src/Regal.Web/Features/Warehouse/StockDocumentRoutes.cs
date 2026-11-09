using System.Globalization;

namespace Regal.Web.Features.Warehouse;

public static class StockDocumentRoutes
{
    public static string Details(int id) => string.Create(CultureInfo.InvariantCulture, $"/stock-documents/{id}");

    public static string ProductHistory(int productId) => string.Create(CultureInfo.InvariantCulture, $"/stock/{productId}/history");
}
