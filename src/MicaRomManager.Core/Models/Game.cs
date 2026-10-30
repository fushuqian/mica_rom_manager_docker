using System.IO;

namespace MicaRomManager.Core.Models;

public static class GameStatus
{
    public const string NotPlayed = "未玩";
    public const string Playing = "在玩";
    public const string Finished = "已通关";
    public const string Dropped = "已放弃";

    public static readonly string[] All = { NotPlayed, Playing, Finished, Dropped };
}

/// <summary>库内一款游戏（一场游戏可能有多张碟）。</summary>
public sealed class Game
{
    public long Id { get; set; }
    public long? LibraryId { get; set; }

    public string Title { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Region { get; set; } = "";
    public string Language { get; set; } = "";
    /// <summary>特别标注，整理文件名时排在语言之后，如「汉化版」。</summary>
    public string SpecialNote { get; set; } = "";
    public int? ReleaseYear { get; set; }
    public string Publisher { get; set; } = "";
    public string Developer { get; set; } = "";
    public bool Favorite { get; set; }
    /// <summary>0 表示未评分，1–5 星。</summary>
    public int Rating { get; set; }
    public string Status { get; set; } = GameStatus.NotPlayed;

    /// <summary>ROM 内容指纹：小文件全量 SHA256，大文件为 fast: 采样指纹。</summary>
    public string RomHash { get; set; } = "";
    /// <summary>第一张碟的绝对路径（容器内）。</summary>
    public string RomPath { get; set; } = "";
    /// <summary>附加碟片路径，换行分隔。</summary>
    public string DiscPaths { get; set; } = "";
    public long FileSize { get; set; }
    /// <summary>封面文件名（位于数据目录 covers/ 下）。</summary>
    public string CoverFile { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Tags { get; set; } = "";

    public int PlayCount { get; set; }
    public long PlaySeconds { get; set; }
    public DateTime? LastPlayedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>文件是否已不存在；由扫描/刷新维护，避免列表绑定实时查盘。</summary>
    public bool Missing { get; set; }

    public string FileName => string.IsNullOrWhiteSpace(RomPath) ? "" : Path.GetFileName(RomPath);

    public string Format => string.IsNullOrWhiteSpace(RomPath)
        ? ""
        : Path.GetExtension(RomPath).TrimStart('.').ToUpperInvariant();

    public string FileSizeDisplay => FormatSize(FileSize);

    public string FavoriteDisplay => Favorite ? "★" : "";

    public string RatingStars => new string('★', Math.Clamp(Rating, 0, 5))
                                 + new string('☆', 5 - Math.Clamp(Rating, 0, 5));

    public string PlaySecondsDisplay => PlaySeconds <= 0
        ? ""
        : $"{PlaySeconds / 3600:00}:{PlaySeconds % 3600 / 60:00}";

    public string LastPlayedDisplay => LastPlayedAt is null
        ? ""
        : LastPlayedAt.Value.ToString("yyyy-MM-dd HH:mm");

    /// <summary>全部碟片：第一碟 + 附加碟。</summary>
    public IReadOnlyList<string> Discs
    {
        get
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(RomPath)) list.Add(RomPath);
            foreach (var path in DiscPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path);
            return list;
        }
    }

    public bool IsMultiDisc => Discs.Count > 1;

    public string DiscCountText => Discs.Count.ToString();

    public Game Clone() => (Game)MemberwiseClone();

    /// <summary>把编辑副本写回原对象。</summary>
    public void ApplyFrom(Game other)
    {
        Title = other.Title;
        Platform = other.Platform;
        Genre = other.Genre;
        Region = other.Region;
        Language = other.Language;
        SpecialNote = other.SpecialNote;
        ReleaseYear = other.ReleaseYear;
        Publisher = other.Publisher;
        Developer = other.Developer;
        Favorite = other.Favorite;
        Rating = other.Rating;
        Status = other.Status;
        RomPath = other.RomPath;
        RomHash = other.RomHash;
        DiscPaths = other.DiscPaths;
        FileSize = other.FileSize;
        CoverFile = other.CoverFile;
        Notes = other.Notes;
        Tags = other.Tags;
        LibraryId = other.LibraryId;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
