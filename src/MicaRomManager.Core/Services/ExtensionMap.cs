using System.IO;

namespace MicaRomManager.Core.Services;

public sealed record ExtensionInfo(string? Platform, bool NeedsSpecify);

/// <summary>扩展名 → 平台的内置映射；.iso/.chd 等跨平台格式要求手动指定。</summary>
public static class ExtensionMap
{
    public const string UnspecifiedPlatform = "未指定";

    private static readonly Dictionary<string, ExtensionInfo> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".nes"] = new("NES", false),
        [".unf"] = new("NES", false),
        [".sfc"] = new("SNES", false),
        [".smc"] = new("SNES", false),
        [".gb"] = new("Game Boy", false),
        [".gbc"] = new("GBC", false),
        [".gba"] = new("GBA", false),
        [".nds"] = new("NDS", false),
        [".n64"] = new("N64", false),
        [".z64"] = new("N64", false),
        [".v64"] = new("N64", false),
        [".md"] = new("Mega Drive", false),
        [".gen"] = new("Mega Drive", false),
        [".smd"] = new("Mega Drive", false),
        [".sms"] = new("Master System", false),
        [".gg"] = new("Game Gear", false),
        [".pce"] = new("PC Engine", false),
        [".vb"] = new("Virtual Boy", false),
        [".gcm"] = new("GameCube", false),
        [".3ds"] = new("3DS", false),
        [".cia"] = new("3DS", false),
        [".nsp"] = new("Switch", false),
        [".xci"] = new("Switch", false),
        [".pbp"] = new("PSP", false),
        [".cso"] = new("PSP", false),
        [".iso"] = new(null, true),
        [".chd"] = new(null, true),
        [".cue"] = new(null, true),
        [".bin"] = new(null, true),
        [".wbfs"] = new(null, true),
        [".rvz"] = new(null, true),
        [".zip"] = new(null, true),
        [".7z"] = new(null, true),
        [".rar"] = new(null, true),
    };

    private static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar" };

    public static IReadOnlyCollection<string> Extensions => Map.Keys;

    public static string FileDialogFilter
    {
        get
        {
            var patterns = string.Join(";", Map.Keys.Select(extension => "*" + extension));
            return $"游戏 ROM|{patterns}|压缩包|*.zip;*.7z;*.rar|所有文件|*.*";
        }
    }

    public static ExtensionInfo Detect(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return Map.TryGetValue(extension, out var info) ? info : new ExtensionInfo(null, true);
    }

    public static bool IsKnown(string filePath) => Map.ContainsKey(Path.GetExtension(filePath));

    public static bool IsArchive(string filePath) =>
        ArchiveExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownOrArchive(string filePath) => IsKnown(filePath) || IsArchive(filePath);
}
