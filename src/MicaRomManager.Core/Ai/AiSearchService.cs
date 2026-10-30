using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MicaRomManager.Core.Data;

namespace MicaRomManager.Core.Ai;

public sealed record WebSearchResult(string Title, string Url, string Snippet, string Summary, string SiteName, string Date);

public sealed record WebSearchResponse(bool Success, List<WebSearchResult> Results, string Error);

/// <summary>调用 DeepSeek 官方 Search API；复用 AI Key，按成功请求计费。</summary>
public sealed class AiSearchService
{
    private const string Endpoint = "https://api.deepseek.com/v1/tools/search";

    private readonly SettingsRepository _settings;
    private readonly ISecretProtector _protector;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AiSearchService> _logger;

    public AiSearchService(SettingsRepository settings, ISecretProtector protector,
        IHttpClientFactory httpFactory, ILogger<AiSearchService> logger)
    {
        _settings = settings;
        _protector = protector;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public AiSearchOptions Options => AiSearchOptions.Load(_settings);

    public bool CanSearch
    {
        get
        {
            var options = Options;
            var ai = AiOptions.Load(_settings, _protector);
            return options.Enabled && ai.IsConfigured && AiSearchOptions.IsDeepSeekEndpoint(ai.BaseUrl);
        }
    }

    public async Task<WebSearchResponse> SearchAsync(string query, int? maxResults, CancellationToken token)
    {
        var options = Options;
        if (!options.Enabled) return Fail("还没有启用联网搜索，请到 设置 → AI 打开「允许联网搜索」。");

        var ai = AiOptions.Load(_settings, _protector);
        if (!ai.IsConfigured) return Fail("还没有配置 AI，无法使用 DeepSeek 搜索。");
        if (!AiSearchOptions.IsDeepSeekEndpoint(ai.BaseUrl))
            return Fail("联网搜索使用 DeepSeek 官方 Search API，请把 BaseUrl 改为 https://api.deepseek.com/v1。");

        var trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0) return Fail("搜索关键词为空。");
        if (trimmed.Length > 128) trimmed = trimmed[..128];

        var count = Math.Clamp(maxResults ?? options.MaxResults, 1, 10);
        var body = JsonSerializer.Serialize(new { query = trimmed, count, need_content = false });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var client = _httpFactory.CreateClient("ai");
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ai.ApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            _logger.LogInformation("SEARCH HTTP {Status}", (int)response.StatusCode);

            if (!response.IsSuccessStatusCode) return Fail(DescribeError(response.StatusCode, text));

            return new WebSearchResponse(true, ParseResults(text), "");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Fail("搜索已取消。");
        }
        catch (OperationCanceledException)
        {
            return Fail("搜索超时（30 秒），请检查网络或代理。");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SEARCH 异常 type={Type}", ex.GetType().Name);
            return Fail($"搜索失败：{ex.Message}");
        }
    }

    public static List<WebSearchResult> ParseResults(string json)
    {
        var results = new List<WebSearchResult>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var array) ||
                array.ValueKind != JsonValueKind.Array) return results;

            foreach (var item in array.EnumerateArray())
            {
                results.Add(new WebSearchResult(
                    GetString(item, "title"),
                    GetString(item, "url"),
                    GetString(item, "snippet"),
                    GetString(item, "summary"),
                    GetString(item, "site_name"),
                    GetString(item, "date")));
            }
        }
        catch
        {
            // 非法响应按空结果处理
        }

        return results;
    }

    /// <summary>把搜索结果压缩成提示词上下文（不输出链接，控制长度）。</summary>
    public static string BuildContext(IEnumerable<WebSearchResult> results)
    {
        var lines = new List<string>();
        foreach (var result in results)
        {
            var summary = string.IsNullOrWhiteSpace(result.Summary) ? result.Snippet : result.Summary;
            if (string.IsNullOrWhiteSpace(summary)) continue;
            if (summary.Length > 400) summary = summary[..400];
            var site = string.IsNullOrWhiteSpace(result.SiteName) ? "未知站点" : result.SiteName;
            lines.Add($"- [{site}] {summary}");
        }

        return string.Join("\n", lines);
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static WebSearchResponse Fail(string error) => new(false, new List<WebSearchResult>(), error);

    private static string DescribeError(HttpStatusCode status, string body)
    {
        var reason = status switch
        {
            HttpStatusCode.Unauthorized => "API Key 无效或已过期",
            HttpStatusCode.Forbidden => "没有搜索权限",
            (HttpStatusCode)429 => "请求过于频繁或额度不足",
            _ => $"HTTP {(int)status}",
        };
        return $"{reason}：{AiJson.Snippet(body)}";
    }
}
