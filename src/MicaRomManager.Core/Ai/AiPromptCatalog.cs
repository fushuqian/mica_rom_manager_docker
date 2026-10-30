using System.Text.RegularExpressions;
using MicaRomManager.Core.Data;

namespace MicaRomManager.Core.Ai;

public sealed record AiPromptDefinition(
    string Id,
    string Name,
    string Description,
    string[] Variables,
    string DefaultSystem,
    string DefaultUser);

/// <summary>读取/保存各 AI 功能的自定义提示词；未自定义或留空时回落内置版本。</summary>
public sealed class AiPromptCatalog
{
    public const string MetadataId = "metadata";
    private const string KeyPrefix = "ai.prompt.";
    private static readonly Regex Placeholder = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    private readonly SettingsRepository _settings;

    public AiPromptCatalog(SettingsRepository settings) => _settings = settings;

    public static IReadOnlyList<AiPromptDefinition> Definitions { get; } = new[]
    {
        new AiPromptDefinition(
            MetadataId, "元数据刮削",
            "根据文件名、平台与联网搜索结果补全游戏元数据，要求只输出 JSON。",
            new[]
            {
                "FileName 文件名", "Platform 平台", "Title 现有标题", "ReleaseYear 现有年份",
                "Regions 区域词典", "Languages 语言词典", "Genres 类型词典", "SearchBlock 联网搜索结果",
            },
            AiPrompts.MetadataSystem, AiPrompts.MetadataUserTemplate),
    };

    public static AiPromptDefinition Find(string id) =>
        Definitions.FirstOrDefault(definition => definition.Id == id)
        ?? throw new ArgumentException($"未知提示词：{id}", nameof(id));

    public string SystemFor(string id)
    {
        var definition = Find(id);
        var custom = _settings.Get(Key(id, "system"));
        return string.IsNullOrWhiteSpace(custom) ? definition.DefaultSystem : custom;
    }

    public string UserFor(string id)
    {
        var definition = Find(id);
        var custom = _settings.Get(Key(id, "user"));
        return string.IsNullOrWhiteSpace(custom) ? definition.DefaultUser : custom;
    }

    public bool IsCustomized(string id) =>
        !string.IsNullOrWhiteSpace(_settings.Get(Key(id, "system"))) ||
        !string.IsNullOrWhiteSpace(_settings.Get(Key(id, "user")));

    public void Save(string id, string? system, string? user)
    {
        _settings.Set(Key(id, "system"), string.IsNullOrWhiteSpace(system) ? null : system);
        _settings.Set(Key(id, "user"), string.IsNullOrWhiteSpace(user) ? null : user);
    }

    public void Reset(string id)
    {
        _settings.Set(Key(id, "system"), null);
        _settings.Set(Key(id, "user"), null);
    }

    public static string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        if (template.Length == 0) return template;

        return Placeholder.Replace(template,
            match => variables.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    private static string Key(string id, string part) => $"{KeyPrefix}{id}.{part}";
}
