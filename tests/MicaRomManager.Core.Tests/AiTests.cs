using MicaRomManager.Core.Ai;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class AiTests : IDisposable
{
    private readonly TempWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void AiJson_ExtractsFencedJson()
    {
        var parsed = AiJson.Parse<Payload>("好的，结果如下：\n```json\n{\"title\":\"银河战士\",\"release_year\":1994}\n```");
        Assert.NotNull(parsed);
        Assert.Equal("银河战士", parsed!.Title);
        Assert.Equal(1994, parsed.ReleaseYear);
    }

    [Fact]
    public void AiJson_InvalidContent_ReturnsNull()
    {
        Assert.Null(AiJson.Parse<Payload>("not json at all"));
        Assert.Contains("not json", AiJson.Snippet("not json at all"));
    }

    [Fact]
    public void Prompt_UnknownVariable_IsPreserved()
    {
        var rendered = AiPromptCatalog.Render("A={{Title}} B={{Nope}}", new Dictionary<string, string>
        {
            ["Title"] = "银河战士",
        });
        Assert.Equal("A=银河战士 B={{Nope}}", rendered);
    }

    [Fact]
    public void PromptCatalog_OverrideAndReset()
    {
        var catalog = new AiPromptCatalog(_ws.Settings);
        Assert.False(catalog.IsCustomized(AiPromptCatalog.MetadataId));
        Assert.Equal(AiPrompts.MetadataSystem, catalog.SystemFor(AiPromptCatalog.MetadataId));

        catalog.Save(AiPromptCatalog.MetadataId, "自定义系统提示", "自定义 {{FileName}}");
        Assert.True(catalog.IsCustomized(AiPromptCatalog.MetadataId));
        Assert.Equal("自定义系统提示", catalog.SystemFor(AiPromptCatalog.MetadataId));

        catalog.Reset(AiPromptCatalog.MetadataId);
        Assert.False(catalog.IsCustomized(AiPromptCatalog.MetadataId));
        Assert.Equal(AiPrompts.MetadataUserTemplate, catalog.UserFor(AiPromptCatalog.MetadataId));
    }

    [Fact]
    public void Merge_OnlyFillsEmptyFields_WhenNotOverwriting()
    {
        var game = new Game { Title = "Contra", Platform = "NES", Region = "美版", RomPath = "/x/contra.nes" };
        var local = new ScrapeResult { GameId = 1, Title = "魂斗罗", Region = "日版", ReleaseYear = 1988 };
        var ai = new ScrapeResult { GameId = 1, Genre = "动作", Publisher = "Konami", Summary = "简介" };

        var merged = ScrapeService.Merge(game, local, ai, overwrite: false);
        Assert.Equal("Contra", merged.Title);
        Assert.Equal("美版", merged.Region);
        Assert.Equal("动作", merged.Genre);
        Assert.Equal("Konami", merged.Publisher);
        Assert.Equal("简介", merged.Summary);
        Assert.True(merged.Apply);
    }

    [Fact]
    public void Merge_Overwrite_PrefersAiThenLocal()
    {
        var game = new Game { Title = "Contra", RomPath = "/x/contra.nes" };
        var local = new ScrapeResult { Title = "魂斗罗", Region = "日版" };
        var ai = new ScrapeResult { Title = "魂斗罗（AI）", Genre = "动作" };

        var merged = ScrapeService.Merge(game, local, ai, overwrite: true);
        Assert.Equal("魂斗罗（AI）", merged.Title);
        Assert.Equal("动作", merged.Genre);
        Assert.Equal("日版", merged.Region);
    }

    [Fact]
    public void Search_ParsesResultsAndBuildsContext()
    {
        const string json = """
            {"results":[{"title":"Super Metroid","url":"https://example.com","snippet":"片段",
            "summary":"1994 年发售的 SNES 游戏","site_name":"Example","date":"2024-01-01"}]}
            """;

        var results = AiSearchService.ParseResults(json);
        Assert.Single(results);
        Assert.Equal("Example", results[0].SiteName);

        var context = AiSearchService.BuildContext(results);
        Assert.Contains("Example", context);
        Assert.DoesNotContain("https://example.com", context);
    }

    [Fact]
    public void SearchOptions_DeepSeekEndpointDetection()
    {
        Assert.True(AiSearchOptions.IsDeepSeekEndpoint("https://api.deepseek.com/v1"));
        Assert.False(AiSearchOptions.IsDeepSeekEndpoint("https://api.openai.com/v1"));
        Assert.False(AiSearchOptions.IsDeepSeekEndpoint("https://evil-deepseek.com/v1"));
    }

    [Fact]
    public void AiOptions_EncryptedKeyRoundTrip()
    {
        var protector = new PassthroughSecretProtector();
        AiOptions.SaveApiKey(_ws.Settings, protector, "sk-test-123456");

        var stored = _ws.Settings.Get(AiOptions.ApiKeyKey)!;
        Assert.StartsWith("enc:", stored);

        var options = AiOptions.Load(_ws.Settings, protector);
        Assert.Equal("sk-test-123456", options.ApiKey);
        Assert.Contains("••••", AiOptions.MaskedKey(_ws.Settings, protector));
    }

    [Fact]
    public void ScrapePreviewRow_SelectsChangedFields()
    {
        var game = new Game { Title = "Contra", Genre = "动作", RomPath = "/x/a.nes" };
        var result = new ScrapeResult
        {
            Title = "魂斗罗",
            Genre = "动作",
            ReleaseYear = 1988,
            Summary = "经典射击游戏",
        };

        var row = ScrapePreviewRow.Create(game, result);
        Assert.Contains("title", row.Fields);
        Assert.DoesNotContain("genre", row.Fields);
        Assert.Contains("notes", row.Fields);

        var patch = row.ToPatch();
        Assert.Equal("魂斗罗", patch.Title);
        Assert.Null(patch.Genre);
        Assert.Equal(1988, patch.ReleaseYear);
    }

    private sealed class Payload
    {
        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string? Title { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("release_year")]
        public int? ReleaseYear { get; set; }
    }
}
