namespace Application.Core;

/// <summary>
/// Carries a page of results plus the total row count. When <see cref="Page"/>/
/// <see cref="PageSize"/> are null the result is unpaged (Items holds every row)
/// — callers expose the page metadata via response headers so the JSON body can
/// stay a plain array (backward-compatible).
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; init; } = new();
    public int Total { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

/// <summary>Shared page/pageSize normalisation for list handlers.</summary>
public static class Pagination
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>
    /// Returns null when neither page nor pageSize was supplied (caller should
    /// return the full list, unchanged behaviour); otherwise a clamped 1-based
    /// page and a page size in [1, <see cref="MaxPageSize"/>].
    /// </summary>
    public static (int Page, int Size)? Resolve(int? page, int? pageSize)
    {
        if (page is null && pageSize is null) return null;
        var p = page is > 0 ? page.Value : 1;
        var s = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        return (p, s);
    }
}
