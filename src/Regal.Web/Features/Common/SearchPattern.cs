namespace Regal.Web.Features.Common;

public static class SearchPattern
{
    public const string Escape = "\\";

    /// <summary>Builds a "contains" ILIKE pattern with wildcards in the term taken literally; null when blank.</summary>
    public static string? Contains(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        var escaped = term.Trim()
            .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
            .Replace("%", Escape + "%", StringComparison.Ordinal)
            .Replace("_", Escape + "_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
