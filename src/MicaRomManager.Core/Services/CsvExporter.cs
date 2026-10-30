using System.Text;
using MicaRomManager.Core.Models;

namespace MicaRomManager.Core.Services;

/// <summary>导出 CSV（UTF-8 BOM，Excel 直接打开不乱码）。</summary>
public static class CsvExporter
{
    private static readonly (string Header, Func<Game, string> Value)[] Columns =
    {
        ("标题", game => game.Title),
        ("平台", game => game.Platform),
        ("类型", game => game.Genre),
        ("区域", game => game.Region),
        ("语言", game => game.Language),
        ("特注", game => game.SpecialNote),
        ("年份", game => game.ReleaseYear?.ToString() ?? ""),
        ("发行商", game => game.Publisher),
        ("开发商", game => game.Developer),
        ("评分", game => game.Rating.ToString()),
        ("收藏", game => game.Favorite ? "是" : "否"),
        ("状态", game => game.Status),
        ("标签", game => game.Tags),
        ("文件名", game => game.FileName),
        ("路径", game => game.RomPath),
        ("大小", game => game.FileSize.ToString()),
        ("指纹", game => game.RomHash),
        ("游玩次数", game => game.PlayCount.ToString()),
        ("游玩时长(秒)", game => game.PlaySeconds.ToString()),
        ("最近游玩", game => game.LastPlayedAt?.ToString("yyyy-MM-dd HH:mm") ?? ""),
        ("备注", game => game.Notes),
        ("文件缺失", game => game.Missing ? "是" : "否"),
    };

    public static byte[] Export(IEnumerable<Game> games)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Columns.Select(column => Quote(column.Header))));

        foreach (var game in games)
        {
            builder.AppendLine(string.Join(",", Columns.Select(column => Quote(column.Value(game)))));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    private static string Quote(string? value)
    {
        var text = (value ?? "").Replace("\r", " ").Replace("\n", " ");
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
