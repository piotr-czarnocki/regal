using Regal.Web.Data.Entities;

namespace Regal.Web.Features.Warehouse;

/// <summary>How warehouse values are shown; numbers follow the current (Polish) culture.</summary>
public static class StockLabels
{
    public static string TypeName(StockDocumentType type) => type switch
    {
        StockDocumentType.PZ => "PZ – przyjęcie od dostawcy",
        StockDocumentType.PW => "PW – przychód wewnętrzny",
        StockDocumentType.WZ => "WZ – wydanie na zewnątrz",
        StockDocumentType.RW => "RW – rozchód wewnętrzny",
        _ => type.ToString(),
    };

    public static string Quantity(decimal quantity) => quantity.ToString("#,0.###");

    public static string Date(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "–";

    public static string Money(decimal value) => $"{value:N2} zł";

    public static string Cost(decimal value) => $"{value:N4} zł";
}

public static class StocktakeLabels
{
    public static string Status(StocktakeStatus status) => status switch
    {
        StocktakeStatus.Draft => "w trakcie liczenia",
        StocktakeStatus.Approved => "zatwierdzony",
        StocktakeStatus.Cancelled => "anulowany",
        _ => status.ToString(),
    };

    public static MudBlazor.Color Color(StocktakeStatus status) => status switch
    {
        StocktakeStatus.Draft => MudBlazor.Color.Info,
        StocktakeStatus.Approved => MudBlazor.Color.Success,
        _ => MudBlazor.Color.Default,
    };
}
