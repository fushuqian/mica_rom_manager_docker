namespace MicaRomManager.Core.Models;

/// <summary>批量/单条修改字段；null 表示不修改。</summary>
public sealed class GamePatch
{
    public string? Title { get; init; }
    public string? Platform { get; init; }
    public string? Genre { get; init; }
    public string? Region { get; init; }
    public string? Language { get; init; }
    public string? SpecialNote { get; init; }
    public int? ReleaseYear { get; init; }
    public string? Publisher { get; init; }
    public string? Developer { get; init; }
    public bool? Favorite { get; init; }
    public int? Rating { get; init; }
    public string? Status { get; init; }
    public string? Tags { get; init; }
    public string? Notes { get; init; }

    public bool HasAny =>
        Title is not null || Platform is not null || Genre is not null || Region is not null ||
        Language is not null || SpecialNote is not null || ReleaseYear is not null ||
        Publisher is not null || Developer is not null || Favorite is not null ||
        Rating is not null || Status is not null || Tags is not null || Notes is not null;

    public void ApplyTo(Game game)
    {
        if (Title is not null) game.Title = Title;
        if (Platform is not null) game.Platform = Platform;
        if (Genre is not null) game.Genre = Genre;
        if (Region is not null) game.Region = Region;
        if (Language is not null) game.Language = Language;
        if (SpecialNote is not null) game.SpecialNote = SpecialNote;
        if (ReleaseYear is not null) game.ReleaseYear = ReleaseYear;
        if (Publisher is not null) game.Publisher = Publisher;
        if (Developer is not null) game.Developer = Developer;
        if (Favorite is not null) game.Favorite = Favorite.Value;
        if (Rating is not null) game.Rating = Math.Clamp(Rating.Value, 0, 5);
        if (Status is not null) game.Status = Status;
        if (Tags is not null) game.Tags = Tags;
        if (Notes is not null) game.Notes = Notes;
    }
}
