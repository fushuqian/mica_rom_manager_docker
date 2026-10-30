using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;

namespace MicaRomManager.Core.Ai;

/// <summary>先本地文件名解析，再按需调用 AI，最后按模式合并成候选结果。</summary>
public sealed class ScrapeService
{
    private readonly IAiChatClient _client;
    private readonly AiMetadataProvider _provider;

    public ScrapeService(IAiChatClient client, AiMetadataProvider provider)
    {
        _client = client;
        _provider = provider;
    }

    public async Task<List<ScrapeResult>> ScrapeAsync(
        IReadOnlyList<Game> games,
        bool useAi,
        bool useSearch,
        bool overwrite,
        IProgress<ScrapeProgress>? progress,
        CancellationToken token)
    {
        var results = new ScrapeResult?[games.Count];
        var completed = 0;
        var useAiFinal = useAi && _client.Options.IsConfigured;
        using var gate = new SemaphoreSlim(2);

        var tasks = games.Select(async (game, index) =>
        {
            await gate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                var local = FileNameScraper.Scrape(game);
                ScrapeResult? ai = null;
                if (useAiFinal)
                {
                    try
                    {
                        ai = await _provider.ScrapeAsync(game, useSearch, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ai = new ScrapeResult { GameId = game.Id, Source = "AI", Error = ex.Message };
                    }
                }

                results[index] = Merge(game, local, ai, overwrite);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ScrapeProgress(done, games.Count, game.Title));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.Where(result => result is not null).Cast<ScrapeResult>().ToList();
    }

    public static ScrapeResult Merge(Game game, ScrapeResult? local, ScrapeResult? ai, bool overwrite)
    {
        local ??= new ScrapeResult { GameId = game.Id, Source = "文件名" };
        var result = new ScrapeResult { GameId = game.Id };

        result.Title = Pick(game.Title, local.Title, ai?.Title, overwrite);
        result.Genre = Pick(game.Genre, local.Genre, ai?.Genre, overwrite);
        result.Region = Pick(game.Region, local.Region, ai?.Region, overwrite);
        result.Language = Pick(game.Language, local.Language, ai?.Language, overwrite);
        result.Publisher = Pick(game.Publisher, local.Publisher, ai?.Publisher, overwrite);
        result.Developer = Pick(game.Developer, local.Developer, ai?.Developer, overwrite);
        result.ReleaseYear = overwrite
            ? ai?.ReleaseYear ?? local.ReleaseYear ?? game.ReleaseYear
            : game.ReleaseYear ?? local.ReleaseYear ?? ai?.ReleaseYear;

        if (string.IsNullOrWhiteSpace(game.Notes))
            result.Summary = FirstNonEmpty(ai?.Summary, local.Summary);

        var sources = new List<string>();
        if (HasValues(local)) sources.Add("文件名");
        if (ai is not null && HasValues(ai)) sources.Add("AI");
        result.Source = sources.Count == 0 ? "" : string.Join(" + ", sources);

        if (ai is not null && ai.Error.Length > 0) result.Error = ai.Error;
        result.Apply = HasChanges(game, result);
        return result;
    }

    private static string Pick(string current, string local, string? ai, bool overwrite) =>
        overwrite ? FirstNonEmpty(ai, local, current) : FirstNonEmpty(current, local, ai);

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return "";
    }

    private static bool HasValues(ScrapeResult result) =>
        result.Title.Length > 0 || result.Genre.Length > 0 || result.Region.Length > 0 ||
        result.Language.Length > 0 || result.ReleaseYear is not null ||
        result.Publisher.Length > 0 || result.Developer.Length > 0 || result.Summary.Length > 0;

    private static bool HasChanges(Game game, ScrapeResult result) =>
        !Same(game.Title, result.Title) || !Same(game.Genre, result.Genre) ||
        !Same(game.Region, result.Region) || !Same(game.Language, result.Language) ||
        game.ReleaseYear != result.ReleaseYear || !Same(game.Publisher, result.Publisher) ||
        !Same(game.Developer, result.Developer) ||
        (game.Notes.Length == 0 && result.Summary.Length > 0);

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);
}
