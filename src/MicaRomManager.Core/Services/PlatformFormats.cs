using System.IO;

namespace MicaRomManager.Core.Services;

/// <summary>平台 ROM 格式白名单与内置默认值。</summary>
public static class PlatformFormats
{
    public static IReadOnlyDictionary<string, string[]> Defaults { get; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = new[] { "nes", "unf" },
            ["SNES"] = new[] { "sfc", "smc" },
            ["N64"] = new[] { "n64", "z64", "v64" },
            ["Game Boy"] = new[] { "gb" },
            ["GBC"] = new[] { "gbc" },
            ["GBA"] = new[] { "gba" },
            ["NDS"] = new[] { "nds" },
            ["3DS"] = new[] { "3ds", "cia" },
            ["Switch"] = new[] { "nsp", "xci" },
            ["GameCube"] = new[] { "gcm", "iso", "rvz" },
            ["Wii"] = new[] { "iso", "wbfs", "rvz" },
            ["Mega Drive"] = new[] { "md", "gen", "smd", "bin" },
            ["Master System"] = new[] { "sms" },
            ["Game Gear"] = new[] { "gg" },
            ["Saturn"] = new[] { "iso", "cue", "bin", "chd" },
            ["Dreamcast"] = new[] { "iso", "cue", "bin", "chd", "gdi" },
            ["PlayStation"] = new[] { "iso", "cue", "bin", "chd", "pbp" },
            ["PS2"] = new[] { "iso", "bin", "chd" },
            ["PS3"] = new[] { "iso", "pkg" },
            ["PSP"] = new[] { "iso", "cso", "pbp", "chd" },
            ["PC Engine"] = new[] { "pce" },
            ["Virtual Boy"] = new[] { "vb" },
        };

    public static string GetDefault(string platform) =>
        Defaults.TryGetValue(platform, out var values) ? string.Join(" ", values) : "";

    /// <summary>解析为带点的小写扩展名列表；支持空格、逗号、顿号、分号分隔。</summary>
    public static IReadOnlyList<string> Parse(string? formats)
    {
        if (string.IsNullOrWhiteSpace(formats)) return Array.Empty<string>();

        var separators = new[] { ' ', '\t', ',', '，', '、', ';', '；' };
        return formats.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.TrimStart('.').ToLowerInvariant())
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(token => "." + token)
            .ToList();
    }

    /// <summary>统一存为空格分隔、小写、无点、去重的形式。</summary>
    public static string Normalize(string? formats) =>
        string.Join(" ", Parse(formats).Select(extension => extension.TrimStart('.')));

    public static bool Supports(string? formats, string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return Parse(formats).Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>扫描时确定平台：唯一匹配优先；冲突时回退内置映射或标记为需手动指定。</summary>
    public static (string Platform, bool NeedsSpecify) Resolve(
        string filePath,
        ExtensionInfo builtin,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? platformFormats)
    {
        if (platformFormats is null || platformFormats.Count == 0)
            return (builtin.Platform ?? ExtensionMap.UnspecifiedPlatform, builtin.NeedsSpecify);

        if (ExtensionMap.IsArchive(filePath))
            return (ExtensionMap.UnspecifiedPlatform, true);

        var extension = Path.GetExtension(filePath);
        var matches = platformFormats
            .Where(pair => pair.Value.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList();

        if (matches.Count == 1) return (matches[0], false);

        if (matches.Count > 1)
        {
            if (builtin.Platform is not null &&
                matches.Contains(builtin.Platform, StringComparer.OrdinalIgnoreCase))
                return (builtin.Platform, false);

            return (ExtensionMap.UnspecifiedPlatform, true);
        }

        return (builtin.Platform ?? ExtensionMap.UnspecifiedPlatform, builtin.NeedsSpecify);
    }
}
