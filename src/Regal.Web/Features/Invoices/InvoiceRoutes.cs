using System.Globalization;

namespace Regal.Web.Features.Invoices;

public static class InvoiceRoutes
{
    public static string Details(int id) => string.Create(CultureInfo.InvariantCulture, $"/invoices/{id}");

    public static string Correction(int id) => string.Create(CultureInfo.InvariantCulture, $"/invoices/{id}/correction");
}
