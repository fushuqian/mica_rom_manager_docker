using System.Text.Json;

namespace MicaRomManager.Core.Ai;

/// <summary>容错解析模型返回的 JSON（可能带 Markdown 围栏或前后说明文字）。</summary>
public static class AiJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T? Parse<T>(string? content)
    {
        var json = Extract(content);
        if (json.Length == 0) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return default;
        }
    }

    public static string Extract(string? content)
    {
        var text = content?.Trim() ?? "";
        if (text.Length == 0) return "";

        text = text.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```", "", StringComparison.Ordinal)
            .Trim();

        var objectStart = text.IndexOf('{');
        var objectEnd = text.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart) return text[objectStart..(objectEnd + 1)];

        var arrayStart = text.IndexOf('[');
        var arrayEnd = text.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart) return text[arrayStart..(arrayEnd + 1)];

        return "";
    }

    public static string Snippet(string? content, int maxLength = 200)
    {
        var text = (content ?? "").Trim();
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}
