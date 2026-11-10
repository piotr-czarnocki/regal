namespace Regal.Web.Data.Entities;

public enum StocktakeStatus
{
    /// <summary>Being counted; lines can still change.</summary>
    Draft,

    /// <summary>Differences posted; nothing changes any more.</summary>
    Approved,

    Cancelled,
}

/// <summary>
/// A count sheet (INW). While a draft its lines change freely; a database trigger refuses any
/// change once it is approved or cancelled (SPEC 5.14).
/// </summary>
public sealed class Stocktake
{
    public int Id { get; set; }

    /// <summary>Given at approval, so a cancelled sheet takes no number.</summary>
    public string? Number { get; set; }

    public StocktakeStatus Status { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? ApprovedAt { get; set; }

    public string? Notes { get; set; }

    public List<StocktakeLine> Lines { get; set; } = [];
}

public sealed class StocktakeLine
{
    public int Id { get; set; }

    public int StocktakeId { get; set; }

    public Stocktake? Stocktake { get; set; }

    public int BatchId { get; set; }

    public Batch? Batch { get; set; }

    /// <summary>Empty until counted; zero means nothing was found.</summary>
    public decimal? Counted { get; set; }

    /// <summary>The batch's stock at approval; empty on a draft.</summary>
    public decimal? Expected { get; set; }
}

/// <summary>The last INW number used in a year (Europe/Warsaw).</summary>
public sealed class StocktakeCounter
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}
