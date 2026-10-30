namespace MicaRomManager.Core.Models;

/// <summary>刮削候选结果：来自文件名解析和/或 AI，经用户预览确认后写库。</summary>
public sealed class ScrapeResult
{
    public long GameId { get; set; }
    public string Title { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Region { get; set; } = "";
    public string Language { get; set; } = "";
    public int? ReleaseYear { get; set; }
    public string Publisher { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Source { get; set; } = "";
    public string Error { get; set; } = "";
    /// <summary>与当前值相比是否有可应用的变化。</summary>
    public bool Apply { get; set; }
}

public sealed record ScrapeProgress(int Completed, int Total, string Message);

/// <summary>预览行：候选值 + 用户勾选的字段。</summary>
public sealed class ScrapePreviewRow
{
    public required Game Game { get; init; }
    public required ScrapeResult Result { get; init; }
    public bool Selected { get; set; } = true;
    /// <summary>勾选要应用的字段名（title/genre/region/language/release_year/publisher/developer/notes）。</summary>
    public HashSet<string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ScrapePreviewRow Create(Game game, ScrapeResult result)
    {
        var row = new ScrapePreviewRow { Game = game, Result = result };
        if (!string.IsNullOrWhiteSpace(result.Title) && result.Title != game.Title) row.Fields.Add("title");
        if (!string.IsNullOrWhiteSpace(result.Genre) && result.Genre != game.Genre) row.Fields.Add("genre");
        if (!string.IsNullOrWhiteSpace(result.Region) && result.Region != game.Region) row.Fields.Add("region");
        if (!string.IsNullOrWhiteSpace(result.Language) && result.Language != game.Language) row.Fields.Add("language");
        if (result.ReleaseYear is not null && result.ReleaseYear != game.ReleaseYear) row.Fields.Add("release_year");
        if (!string.IsNullOrWhiteSpace(result.Publisher) && result.Publisher != game.Publisher) row.Fields.Add("publisher");
        if (!string.IsNullOrWhiteSpace(result.Developer) && result.Developer != game.Developer) row.Fields.Add("developer");
        if (!string.IsNullOrWhiteSpace(result.Summary) && string.IsNullOrWhiteSpace(game.Notes)) row.Fields.Add("notes");
        if (row.Fields.Count == 0) row.Selected = false;
        return row;
    }

    public GamePatch ToPatch()
    {
        return new GamePatch
        {
            Title = Has("title") ? Result.Title : null,
            Genre = Has("genre") ? Result.Genre : null,
            Region = Has("region") ? Result.Region : null,
            Language = Has("language") ? Result.Language : null,
            ReleaseYear = Has("release_year") ? Result.ReleaseYear : null,
            Publisher = Has("publisher") ? Result.Publisher : null,
            Developer = Has("developer") ? Result.Developer : null,
            Notes = Has("notes") ? Result.Summary : null,
        };
    }

    private bool Has(string field) => Selected && Fields.Contains(field);
}
