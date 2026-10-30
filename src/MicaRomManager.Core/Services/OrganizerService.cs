using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;

namespace MicaRomManager.Core.Services;

/// <summary>按标题/区域/语言/特注重命名库内文件：先出计划，确认后再移动。</summary>
public sealed class OrganizerService
{
    private const int MaxTitleLength = 150;

    private readonly GameRepository _games;
    private readonly PathGuard _guard;

    public OrganizerService(GameRepository games, PathGuard guard)
    {
        _games = games;
        _guard = guard;
    }

    public RenamePlan Plan(IReadOnlyList<Game> games)
    {
        var items = new List<RenamePlanItem>();
        var warnings = new List<string>();
        var skipped = new List<string>();
        var reserved = new HashSet<string>(StringComparer.Ordinal);

        foreach (var game in games)
        {
            var discs = game.Discs;
            for (var index = 0; index < discs.Count; index++)
            {
                var source = discs[index];
                if (!File.Exists(source))
                {
                    skipped.Add($"{game.Title}：文件不存在");
                    continue;
                }

                if (!_guard.IsInsideRoots(source))
                {
                    warnings.Add($"{game.Title}：文件不在库目录内，跳过");
                    continue;
                }

                var directory = Path.GetDirectoryName(source);
                if (string.IsNullOrWhiteSpace(directory)) continue;

                var fileName = BuildFileName(game, index, source);
                if (fileName.Length == 0)
                {
                    skipped.Add($"{game.Title}：标题为空");
                    continue;
                }

                var target = Path.Combine(directory, fileName);
                if (string.Equals(target, source, StringComparison.Ordinal))
                {
                    skipped.Add($"{game.Title}：名称已符合规则");
                    continue;
                }

                target = ResolveUniqueName(target, source, reserved);
                reserved.Add(target);
                items.Add(new RenamePlanItem(game.Id, index, source, target));
            }
        }

        return new RenamePlan(items, warnings, skipped);
    }

    public OrganizeResult Apply(RenamePlan plan)
    {
        var messages = new List<string>();
        var renamed = 0;
        var failed = 0;
        var byGame = plan.Items.GroupBy(item => item.GameId);

        foreach (var group in byGame)
        {
            var game = _games.Get(group.Key);
            if (game is null) continue;

            var paths = game.Discs.ToList();
            var changed = false;

            foreach (var item in group)
            {
                if (!File.Exists(item.OldPath))
                {
                    failed++;
                    messages.Add($"{Path.GetFileName(item.OldPath)}：源文件已不存在");
                    continue;
                }

                if (File.Exists(item.NewPath))
                {
                    failed++;
                    messages.Add($"{Path.GetFileName(item.NewPath)}：目标已存在");
                    continue;
                }

                if (!_guard.IsInsideRoots(item.NewPath) || !_guard.IsInsideRoots(item.OldPath))
                {
                    failed++;
                    messages.Add($"{Path.GetFileName(item.OldPath)}：路径越界");
                    continue;
                }

                try
                {
                    File.Move(item.OldPath, item.NewPath);
                    if (item.DiscIndex < paths.Count) paths[item.DiscIndex] = item.NewPath;
                    renamed++;
                    changed = true;
                }
                catch (Exception ex)
                {
                    failed++;
                    messages.Add($"{Path.GetFileName(item.OldPath)}：{ex.Message}");
                }
            }

            if (!changed) continue;

            game.RomPath = paths[0];
            game.DiscPaths = string.Join("\n", paths.Skip(1));
            _games.Update(game);
        }

        return new OrganizeResult(renamed, failed, messages);
    }

    /// <summary>标题 [Disc-n] (区域)(语言)(特注).扩展名</summary>
    public static string BuildFileName(Game game, int discIndex, string sourcePath)
    {
        if (game is null || string.IsNullOrWhiteSpace(sourcePath)) return "";

        var title = SanitizeTitle(game.Title);
        if (title.Length == 0) return "";

        var builder = new StringBuilder(title);
        if (game.Discs.Count > 1) builder.Append(" Disc-").Append(discIndex + 1);

        var suffix = "";
        if (JoinValues(game.Region) is { Length: > 0 } region) suffix += $"({region})";
        if (JoinValues(game.Language) is { Length: > 0 } language) suffix += $"({language})";
        if (JoinValues(game.SpecialNote) is { Length: > 0 } note) suffix += $"({note})";
        if (suffix.Length > 0) builder.Append(' ').Append(suffix);

        return builder + Path.GetExtension(sourcePath);
    }

    public static bool IsUnderDirectory(string path, string directory) =>
        PathGuard.IsUnder(path, directory);

    private static string SanitizeTitle(string? raw)
    {
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) return "";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = text.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = Regex.Replace(new string(chars), @"\s+", " ").Trim().TrimEnd('.');
        if (cleaned.Length > MaxTitleLength) cleaned = cleaned[..MaxTitleLength].TrimEnd();
        return cleaned;
    }

    private static string JoinValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var invalid = Path.GetInvalidFileNameChars();
        var parts = raw.Split(new[] { ',', '，', '、', ';', '；' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => new string(part.Select(c => invalid.Contains(c) ? '_' : c).ToArray()))
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(",", parts);
    }

    private static string ResolveUniqueName(string target, string source, HashSet<string> reserved)
    {
        if (!File.Exists(target) && !reserved.Contains(target)) return target;

        var directory = Path.GetDirectoryName(target) ?? "";
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (string.Equals(candidate, source, StringComparison.Ordinal)) return candidate;
            if (!File.Exists(candidate) && !reserved.Contains(candidate)) return candidate;
        }

        return target;
    }
}
