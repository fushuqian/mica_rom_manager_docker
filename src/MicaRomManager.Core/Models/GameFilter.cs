namespace MicaRomManager.Core.Models;

/// <summary>列表筛选条件；null/false 表示不限制。</summary>
public sealed record GameFilter
{
    public string? Keyword { get; init; }
    public string? Platform { get; init; }
    public string? Status { get; init; }
    public string? Region { get; init; }
    public string? Language { get; init; }
    public string? Genre { get; init; }
    public string? Developer { get; init; }
    public string? Tag { get; init; }
    public bool OnlyFavorites { get; init; }
    public bool OnlyMissing { get; init; }
    public int? YearMin { get; init; }
    public int? YearMax { get; init; }
    public long? LibraryId { get; init; }

    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Keyword) || !string.IsNullOrWhiteSpace(Platform) ||
        !string.IsNullOrWhiteSpace(Status) || !string.IsNullOrWhiteSpace(Region) ||
        !string.IsNullOrWhiteSpace(Language) || !string.IsNullOrWhiteSpace(Genre) ||
        !string.IsNullOrWhiteSpace(Developer) || !string.IsNullOrWhiteSpace(Tag) ||
        OnlyFavorites || OnlyMissing || YearMin is not null || YearMax is not null || LibraryId is not null;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total)
{
    public int PageCount(int pageSize) => pageSize <= 0 ? 0 : (Total + pageSize - 1) / pageSize;
}

public sealed record GameSort(string Column = "title", bool Descending = false)
{
    /// <summary>允许排序的列，防止 SQL 注入。</summary>
    public static readonly IReadOnlyDictionary<string, string> Columns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = "title COLLATE NOCASE",
            ["platform"] = "platform COLLATE NOCASE",
            ["releaseyear"] = "release_year",
            ["rating"] = "rating",
            ["lastplayed"] = "last_played_at",
            ["filesize"] = "file_size",
            ["playseconds"] = "play_seconds",
            ["status"] = "status COLLATE NOCASE",
            ["created"] = "created_at",
            ["updated"] = "updated_at",
            ["id"] = "id",
        };

    public string Resolve() => Columns.TryGetValue(Column, out var column) ? column : Columns["title"];
}
