namespace MicaRomManager.Core.Models;

public sealed class Platform
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    /// <summary>空格分隔的扩展名白名单（不带点）。</summary>
    public string Formats { get; set; } = "";
    /// <summary>预留：浏览器试玩用的 libretro 核心名，如 snes9x。</summary>
    public string PlayCore { get; set; } = "";
    public int SortOrder { get; set; }
    public int GameCount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class Library
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>容器内绝对路径，如 /roms/nes。</summary>
    public string RootPath { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int GameCount { get; set; }
    public bool Writable { get; set; }
}
