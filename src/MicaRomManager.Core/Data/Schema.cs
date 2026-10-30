namespace MicaRomManager.Core.Data;

public sealed record Migration(int Version, string Description);

/// <summary>数据库结构与默认词典；只有 v1，后续版本追加迁移脚本。</summary>
public static class Schema
{
    public static readonly IReadOnlyList<Migration> Migrations = new[]
    {
        new Migration(1, "初始结构"),
    };

    public static readonly IReadOnlyDictionary<string, string[]> DefaultLookups =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["region"] = new[]
            {
                "日版", "美版", "欧版", "国行", "港版", "台版", "韩版", "汉化", "世界版", "亚洲版",
            },
            ["language"] = new[]
            {
                "简体中文", "繁体中文", "英语", "日语", "韩语", "法语", "德语", "西班牙语",
                "意大利语", "俄语", "葡萄牙语", "多语言",
            },
            ["genre"] = new[]
            {
                "动作", "角色扮演", "冒险", "射击", "格斗", "策略", "模拟", "体育", "竞速", "益智", "音乐",
            },
            ["publisher"] = Array.Empty<string>(),
            ["developer"] = Array.Empty<string>(),
            ["tag"] = new[] { "汉化", "经典", "联机", "独占", "移植", "系列" },
            ["platform"] = Array.Empty<string>(),
        };

    public const string V1 = """
        CREATE TABLE IF NOT EXISTS libraries (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            name        TEXT NOT NULL,
            root_path   TEXT NOT NULL UNIQUE,
            created_at  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS games (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            library_id    INTEGER REFERENCES libraries(id) ON DELETE SET NULL,
            title         TEXT NOT NULL,
            platform      TEXT NOT NULL DEFAULT '',
            genre         TEXT NOT NULL DEFAULT '',
            region        TEXT NOT NULL DEFAULT '',
            language      TEXT NOT NULL DEFAULT '',
            special_note  TEXT NOT NULL DEFAULT '',
            release_year  INTEGER,
            publisher     TEXT NOT NULL DEFAULT '',
            developer     TEXT NOT NULL DEFAULT '',
            favorite      INTEGER NOT NULL DEFAULT 0,
            rating        INTEGER NOT NULL DEFAULT 0,
            status        TEXT NOT NULL DEFAULT '未玩',
            rom_path      TEXT NOT NULL UNIQUE,
            disc_paths    TEXT NOT NULL DEFAULT '',
            file_size     INTEGER NOT NULL DEFAULT 0,
            rom_hash      TEXT NOT NULL DEFAULT '',
            cover_file    TEXT NOT NULL DEFAULT '',
            notes         TEXT NOT NULL DEFAULT '',
            tags          TEXT NOT NULL DEFAULT '',
            play_count    INTEGER NOT NULL DEFAULT 0,
            play_seconds  INTEGER NOT NULL DEFAULT 0,
            last_played_at TEXT,
            missing       INTEGER NOT NULL DEFAULT 0,
            created_at    TEXT NOT NULL,
            updated_at    TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS platforms (
            name        TEXT PRIMARY KEY,
            folder      TEXT NOT NULL DEFAULT '',
            formats     TEXT NOT NULL DEFAULT '',
            play_core   TEXT NOT NULL DEFAULT '',
            sort_order  INTEGER NOT NULL DEFAULT 0,
            updated_at  TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS lookup_items (
            kind   TEXT NOT NULL,
            value  TEXT NOT NULL,
            PRIMARY KEY (kind, value)
        );

        CREATE TABLE IF NOT EXISTS settings (
            key   TEXT PRIMARY KEY,
            value TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_games_platform ON games(platform);
        CREATE INDEX IF NOT EXISTS idx_games_status ON games(status);
        CREATE INDEX IF NOT EXISTS idx_games_hash ON games(rom_hash);
        CREATE INDEX IF NOT EXISTS idx_games_library ON games(library_id);
        CREATE INDEX IF NOT EXISTS idx_games_favorite ON games(favorite);
        CREATE INDEX IF NOT EXISTS idx_games_title ON games(title COLLATE NOCASE);
        """;
}
