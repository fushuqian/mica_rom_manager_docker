using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MicaRomManager.Core.Data;

namespace MicaRomManager.Core.Ai;

public sealed record AiChatResult(bool Success, string Content, string Error, int StatusCode, TimeSpan Elapsed);

public interface IAiChatClient
{
    AiOptions Options { get; }
    Task<AiChatResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken token);
}

/// <summary>OpenAI 兼容 /chat/completions 客户端；每次调用读取最新设置。</summary>
public sealed class AiChatClient : IAiChatClient
{
    private readonly SettingsRepository _settings;
    private readonly ISecretProtector _protector;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AiChatClient> _logger;

    public AiChatClient(SettingsRepository settings, ISecretProtector protector,
        IHttpClientFactory httpFactory, ILogger<AiChatClient> logger)
    {
        _settings = settings;
        _protector = protector;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public AiOptions Options => AiOptions.Load(_settings, _protector);

    public async Task<AiChatResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken token)
    {
        var options = Options;
        if (!options.IsConfigured)
            return new AiChatResult(false, "",
                "还没有配置 AI，请先在 设置 → AI 里填写 BaseUrl、API Key 和模型。", 0, TimeSpan.Zero);

        var body = new
        {
            model = options.Model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            temperature = 0,
            max_tokens = 1024,
        };

        var url = BuildUrl(options.BaseUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpFactory.CreateClient("ai");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI HTTP {Status} host={Host} model={Model}", (int)response.StatusCode,
                    SafeHost(url), options.Model);
                return new AiChatResult(false, "", DescribeError(response.StatusCode, text),
                    (int)response.StatusCode, stopwatch.Elapsed);
            }

            var content = ExtractContent(text);
            if (string.IsNullOrWhiteSpace(content))
                return new AiChatResult(false, "", $"模型返回为空：{AiJson.Snippet(text)}",
                    (int)response.StatusCode, stopwatch.Elapsed);

            _logger.LogInformation("AI OK host={Host} model={Model} elapsed={Elapsed}ms", SafeHost(url),
                options.Model, stopwatch.ElapsedMilliseconds);
            return new AiChatResult(true, content, "", (int)response.StatusCode, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new AiChatResult(false, "", "已取消。", 0, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return new AiChatResult(false, "", $"请求超时（{options.TimeoutSeconds} 秒），请检查网络或代理。",
                0, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("AI 异常 host={Host} type={Type}", SafeHost(url), ex.GetType().Name);
            return new AiChatResult(false, "", $"连接失败：{ex.Message}", 0, stopwatch.Elapsed);
        }
    }

    private static string BuildUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        return url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? url
            : url + "/chat/completions";
    }

    private static string ExtractContent(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                    {
                        return content.GetString() ?? "";
                    }
                }
            }
        }
        catch
        {
            // 兼容个别端点直接返回文本
        }

        return "";
    }

    private static string DescribeError(HttpStatusCode status, string body)
    {
        var detail = "";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) detail = error.GetString() ?? "";
                else if (error.TryGetProperty("message", out var message) &&
                         message.ValueKind == JsonValueKind.String) detail = message.GetString() ?? "";
            }
        }
        catch
        {
            detail = AiJson.Snippet(body);
        }

        var reason = status switch
        {
            HttpStatusCode.Unauthorized => "API Key 无效或已过期",
            HttpStatusCode.Forbidden => "没有访问权限",
            (HttpStatusCode)404 => "BaseUrl 或模型名不正确",
            (HttpStatusCode)429 => "请求过于频繁或额度不足",
            HttpStatusCode.BadRequest => "请求参数不被端点接受",
            _ => $"HTTP {(int)status}",
        };

        return detail.Length > 0 ? $"{reason}：{detail}" : reason;
    }

    private static string SafeHost(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return "invalid-url";
        }
    }

}
