using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;

namespace MicaRomManager.Core.Ai;

/// <summary>用 OpenAI 兼容接口补全单条游戏元数据；只发送文本，不上传 ROM。</summary>
public sealed class AiMetadataProvider
{
    private readonly IAiChatClient _client;
    private readonly AiSearchService _search;
    private readonly LookupRepository _lookups;
    private readonly AiPromptCatalog _prompts;

    public AiMetadataProvider(IAiChatClient client, AiSearchService search, LookupRepository lookups, AiPromptCatalog prompts)
    {
        _client = client;
        _search = search;
        _lookups = lookups;
        _prompts = prompts;
    }

    public async Task<ScrapeResult> ScrapeAsync(Game game, bool useSearch, CancellationToken token)
    {
        var result = new ScrapeResult { GameId = game.Id, Source = "AI" };
        if (!_client.Options.IsConfigured)
        {
            result.Error = "还没有配置 AI，请先在 设置 → AI 里填写 BaseUrl、API Key 和模型。";
            return result;
        }

        var searchContext = "";
        if (useSearch)
        {
            if (!_search.CanSearch)
            {
                result.Source = "AI（搜索不可用）";
            }
            else
            {
                var search = await _search.SearchAsync(BuildSearchQuery(game), null, token);
                if (!search.Success) result.Source = "AI（搜索失败）";
                else if (search.Results.Count == 0) result.Source = "AI（搜索无结果）";
                else
                {
                    searchContext = AiSearchService.BuildContext(search.Results);
                    result.Source = "AI+搜索";
                }
            }
        }

        var prompt = AiPrompts.MetadataUser(game,
            _lookups.List(LookupRepository.Region),
            _lookups.List(LookupRepository.Language),
            _lookups.List(LookupRepository.Genre),
            searchContext,
            _prompts.UserFor(AiPromptCatalog.MetadataId));

        var response = await _client.CompleteAsync(_prompts.SystemFor(AiPromptCatalog.MetadataId), prompt, token);
        if (!response.Success)
        {
            result.Error = response.Error;
            return result;
        }

        var payload = AiJson.Parse<AiMetadataResponse>(response.Content);
        if (payload is null)
        {
            result.Error = $"返回内容不是有效 JSON：{AiJson.Snippet(response.Content)}";
            return result;
        }

        result.Title = Clean(payload.Title);
        result.Genre = Clean(payload.Genre);
        result.Region = FileNameScraper.NormalizeRegion(payload.Region);
        result.Language = FileNameScraper.NormalizeLanguage(payload.Language);
        result.Publisher = Clean(payload.Publisher);
        result.Developer = Clean(payload.Developer);
        result.Summary = Clean(payload.Summary, 300);

        if (payload.ReleaseYear is int year && year >= 1970 && year <= DateTime.Now.Year + 2)
            result.ReleaseYear = year;

        if (result.Title.Length == 0 && result.Genre.Length == 0 && result.Summary.Length == 0)
            result.Error = "模型没有返回可用内容。";

        return result;
    }

    private static string BuildSearchQuery(Game game)
    {
        var name = game.Title.Trim();
        if (name.Length == 0 || Regex.IsMatch(name, @"^\d+$"))
            name = Path.GetFileNameWithoutExtension(game.RomPath);

        var query = $"{name} {game.Platform} 游戏 发售年份 发行商 开发商".Trim();
        return query.Length <= 120 ? query : query[..120];
    }

    private static string Clean(string? value, int maxLength = 120)
    {
        var text = (value ?? "").Trim().Trim('"', '“', '”', '\'', '「', '」');
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private sealed class AiMetadataResponse
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("genre")] public string? Genre { get; set; }
        [JsonPropertyName("region")] public string? Region { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("release_year")] public int? ReleaseYear { get; set; }
        [JsonPropertyName("publisher")] public string? Publisher { get; set; }
        [JsonPropertyName("developer")] public string? Developer { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
    }
}
