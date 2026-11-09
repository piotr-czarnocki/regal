namespace Regal.Web.Features.Common;

/// <summary>Outcome of a write. Error is a message in Polish, ready to show to the user.</summary>
public sealed record SaveResult(int? Id, string? Error)
{
    public bool IsSuccess => Error is null;

    public static SaveResult Ok(int id) => new(id, null);

    public static SaveResult Fail(string error) => new(null, error);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public static class Paging
{
    private const int MaxPageSize = 100;

    public static int Skip(int page, int pageSize) => Math.Max(page, 0) * Take(pageSize);

    public static int Take(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);
}
