using System.IO;
using System.Text.RegularExpressions;
using MicaRomManager.Core.Models;

namespace MicaRomManager.Core.Services;

/// <summary>从文件名推断标题、区域、语言、年份；纯本地、零成本。</summary>
public static class FileNameScraper
{
    private static readonly Regex BracketToken = new(@"\(([^()]*)\)|\[([^\[\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex YearToken = new(@"(?<!\d)(19\d{2}|20\d{2})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex VersionToken = new(
        @"^(rev|revision|v|ver|version|beta|proto|prototype|demo|sample|unl|hack|kiosk)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DiscToken = new(
        @"^(disc|disk|cd|side)\s*[-_ ]?\d+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SpaceToken = new(@"\s+", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> RegionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usa"] = "美版", ["us"] = "美版", ["u"] = "美版", ["america"] = "美版",
        ["europe"] = "欧版", ["eur"] = "欧版", ["e"] = "欧版",
        ["japan"] = "日版", ["jp"] = "日版", ["j"] = "日版",
        ["world"] = "世界版", ["w"] = "世界版",
        ["china"] = "国行", ["cn"] = "国行",
        ["hong kong"] = "港版", ["hongkong"] = "港版", ["hk"] = "港版",
        ["taiwan"] = "台版", ["tw"] = "台版",
        ["korea"] = "韩版", ["kr"] = "韩版",
        ["asia"] = "亚洲版", ["australia"] = "澳版", ["au"] = "澳版",
        ["germany"] = "德版", ["france"] = "法版", ["spain"] = "西班牙版",
        ["italy"] = "意大利版", ["brazil"] = "巴西版", ["canada"] = "加拿大版",
        ["日版"] = "日版", ["美版"] = "美版", ["欧版"] = "欧版", ["国行"] = "国行",
        ["港版"] = "港版", ["台版"] = "台版", ["韩版"] = "韩版", ["世界版"] = "世界版",
    };

    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "英语", ["eng"] = "英语", ["english"] = "英语", ["英语"] = "英语",
        ["ja"] = "日语", ["jp"] = "日语", ["jpn"] = "日语", ["japanese"] = "日语", ["日语"] = "日语",
        ["zh"] = "简体中文", ["cn"] = "简体中文", ["chs"] = "简体中文", ["chinese"] = "简体中文",
        ["简体中文"] = "简体中文",
        ["cht"] = "繁体中文", ["traditional chinese"] = "繁体中文", ["繁体中文"] = "繁体中文",
        ["fr"] = "法语", ["fre"] = "法语", ["fra"] = "法语", ["french"] = "法语", ["法语"] = "法语",
        ["de"] = "德语", ["ger"] = "德语", ["deu"] = "德语", ["german"] = "德语", ["德语"] = "德语",
        ["es"] = "西班牙语", ["spa"] = "西班牙语", ["spanish"] = "西班牙语", ["西班牙语"] = "西班牙语",
        ["it"] = "意大利语", ["ita"] = "意大利语", ["italian"] = "意大利语", ["意大利语"] = "意大利语",
        ["ru"] = "俄语", ["rus"] = "俄语", ["russian"] = "俄语", ["俄语"] = "俄语",
        ["pt"] = "葡萄牙语", ["por"] = "葡萄牙语", ["portuguese"] = "葡萄牙语", ["葡萄牙语"] = "葡萄牙语",
        ["ko"] = "韩语", ["kor"] = "韩语", ["korean"] = "韩语", ["韩语"] = "韩语",
    };

    public static ScrapeResult Scrape(Game game)
    {
        var result = new ScrapeResult { GameId = game.Id, Source = "文件名" };
        if (string.IsNullOrWhiteSpace(game.RomPath)) return result;

        var name = Path.GetFileNameWithoutExtension(game.RomPath);
        if (string.IsNullOrWhiteSpace(name)) return result;

        var regions = new List<string>();
        var languages = new List<string>();
        var notes = new List<string>();
        string? yearText = null;

        var title = name;
        var matches = BracketToken.Matches(name);
        foreach (Match match in matches)
        {
            var token = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
            if (token.Length == 0) continue;

            var consumed = false;

            var yearMatch = YearToken.Match(token);
            if (yearMatch.Success && token.Length <= 12 && yearText == null)
            {
                yearText = yearMatch.Value;
                consumed = true;
            }

            if (DiscToken.IsMatch(token))
            {
                consumed = true;
            }

            var parts = token.Split(new[] { ',', '，', '/', '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();

            if (parts.Count > 1 && parts.All(part => LanguageMap.ContainsKey(part)))
            {
                foreach (var part in parts)
                    AddDistinct(languages, LanguageMap[part]);
                consumed = true;
            }

            if (RegionMap.TryGetValue(token, out var region))
            {
                AddDistinct(regions, region);
                consumed = true;
            }

            if (LanguageMap.TryGetValue(token, out var language))
            {
                AddDistinct(languages, language);
                consumed = true;
            }

            if (VersionToken.IsMatch(token))
            {
                notes.Add($"({token})");
                consumed = true;
            }

            if (consumed) title = title.Replace(match.Value, " ");
        }

        var cleaned = SpaceToken.Replace(title.Replace('_', ' ').Replace('.', ' '), " ")
            .Trim().Trim('-', '_', ' ');
        if (cleaned.Length > 0 && !string.Equals(cleaned, name, StringComparison.Ordinal))
            result.Title = cleaned;

        if (regions.Count > 0) result.Region = string.Join(",", regions);
        if (languages.Count > 0) result.Language = string.Join(",", languages);
        if (yearText is not null && int.TryParse(yearText, out var year)) result.ReleaseYear = year;
        if (notes.Count > 0) result.Summary = string.Join(" ", notes);

        return result;
    }

    public static string NormalizeRegion(string? value)
    {
        var text = value?.Trim() ?? "";
        return RegionMap.TryGetValue(text, out var mapped) ? mapped : text;
    }

    public static string NormalizeLanguage(string? value)
    {
        var text = value?.Trim() ?? "";
        return LanguageMap.TryGetValue(text, out var mapped) ? mapped : text;
    }

    private static void AddDistinct(List<string> target, string value)
    {
        if (!target.Contains(value, StringComparer.OrdinalIgnoreCase)) target.Add(value);
    }
}
