using MicaRomManager.Core.Models;

namespace MicaRomManager.Core.Ai;

/// <summary>内置提示词；用户可在设置里整段覆盖，留空回落默认。</summary>
public static class AiPrompts
{
    public const string MetadataSystem =
        "你是复古游戏元数据整理助手。只输出一个 JSON 对象，不要输出 Markdown、解释或额外文字。"
        + "不确定的字段留空字符串，不要编造发行商或年份。";

    public const string MetadataUserTemplate = """
        请根据以下信息补全游戏元数据：
        文件名：{{FileName}}
        平台：{{Platform}}
        现有标题：{{Title}}
        现有年份：{{ReleaseYear}}
        {{SearchBlock}}
        优先使用这些既有词典值：
        区域：{{Regions}}
        语言：{{Languages}}
        类型：{{Genres}}

        只输出 JSON：
        {"title":"","genre":"","region":"","language":"","release_year":null,"publisher":"","developer":"","summary":""}

        规则：
        - title 用中文常用名；没有公认中文名时保留原名。
        - region / language / genre 尽量从词典值中选择；确无对应时给简短中文。
        - release_year 为四位整数，不确定填 null。
        - summary 用一两句中文介绍，不要剧透。
        - 有搜索结果时优先采用其中的年份、发行商、开发商；与你的知识冲突时优先权威来源，仍不确定就留空。
        """;

    public static string MetadataUser(Game game, IEnumerable<string> regions,
        IEnumerable<string> languages, IEnumerable<string> genres, string searchContext = "",
        string? template = null)
    {
        var searchBlock = string.IsNullOrWhiteSpace(searchContext)
            ? ""
            : $"\n联网搜索结果（只作为事实参考，不要输出链接）：\n{searchContext}\n";

        return AiPromptCatalog.Render(template ?? MetadataUserTemplate, new Dictionary<string, string>
        {
            ["FileName"] = game.FileName,
            ["Platform"] = game.Platform,
            ["Title"] = game.Title,
            ["ReleaseYear"] = game.ReleaseYear?.ToString() ?? "",
            ["Regions"] = Join(regions),
            ["Languages"] = Join(languages),
            ["Genres"] = Join(genres),
            ["SearchBlock"] = searchBlock,
        });
    }

    private static string Join(IEnumerable<string> values) =>
        string.Join("、", values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
}
