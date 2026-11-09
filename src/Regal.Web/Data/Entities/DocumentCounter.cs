namespace Regal.Web.Data.Entities;

/// <summary>The last number used for a document type in a year (Europe/Warsaw).</summary>
public sealed class DocumentCounter
{
    public StockDocumentType Type { get; set; }

    public int Year { get; set; }

    public int LastNumber { get; set; }
}
