namespace Regal.Web.Features.Products;

/// <summary>VAT rates are codes, not numbers: "zw" (exempt) and "np" (not subject to VAT) have no percentage.</summary>
public static class VatRates
{
    public static readonly IReadOnlyList<string> All = ["23", "8", "5", "0", "zw", "np"];

    public const string Default = "23";
}
