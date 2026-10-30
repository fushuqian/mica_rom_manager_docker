using MicaRomManager.Core.Models;

namespace MicaRomManager.Core.Data;

public sealed class PlatformRepository
{
    private readonly Db _db;
    private readonly GameRepository _games;

    public PlatformRepository(Db db, GameRepository games)
    {
        _db = db;
        _games = games;
    }

    public IReadOnlyList<Platform> ListAll()
    {
        var counts = _games.CountByPlatform();
        var result = new List<Platform>();
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, folder, formats, play_core, sort_order, updated_at
            FROM platforms ORDER BY sort_order, name COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var platform = new Platform
            {
                Name = reader.GetString(0),
                Folder = reader.GetString(1),
                Formats = reader.GetString(2),
                PlayCore = reader.GetString(3),
                SortOrder = reader.GetInt32(4),
                UpdatedAt = DateTime.TryParse(reader.GetString(5), out var date) ? date : DateTime.Now,
            };
            platform.GameCount = counts.TryGetValue(platform.Name, out var count) ? count : 0;
            result.Add(platform);
        }

        return result;
    }

    public Platform? Get(string name)
    {
        foreach (var platform in ListAll())
            if (string.Equals(platform.Name, name, StringComparison.OrdinalIgnoreCase)) return platform;
        return null;
    }

    public void Ensure(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO platforms(name, folder, formats, sort_order, updated_at)
                VALUES (@name, @name, '', 999, @now);
                """;
            command.Parameters.AddWithValue("@name", name.Trim());
            command.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
            command.ExecuteNonQuery();

            using var lookup = connection.CreateCommand();
            lookup.CommandText = "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES ('platform', @name);";
            lookup.Parameters.AddWithValue("@name", name.Trim());
            lookup.ExecuteNonQuery();
        });
    }

    public void Update(Platform platform)
    {
        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE platforms SET folder=@folder, formats=@formats, play_core=@core,
                    sort_order=@order, updated_at=@now
                WHERE name=@name;
                """;
            command.Parameters.AddWithValue("@folder", platform.Folder);
            command.Parameters.AddWithValue("@formats", platform.Formats);
            command.Parameters.AddWithValue("@core", platform.PlayCore);
            command.Parameters.AddWithValue("@order", platform.SortOrder);
            command.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
            command.Parameters.AddWithValue("@name", platform.Name);
            command.ExecuteNonQuery();
        });
    }

    /// <summary>删除平台；仍有游戏引用时拒绝。</summary>
    public (bool Ok, string Message) Delete(string name)
    {
        var platform = Get(name);
        if (platform is null) return (false, "平台不存在。");
        if (platform.GameCount > 0) return (false, $"平台「{name}」下还有 {platform.GameCount} 款游戏，请先移走。");

        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM platforms WHERE name=@name;";
            command.Parameters.AddWithValue("@name", name);
            command.ExecuteNonQuery();
        });
        return (true, "已删除。");
    }

    /// <summary>平台改名：级联 games.platform 与 lookup 词典。</summary>
    public void Rename(string oldName, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName) || oldName == newName) return;

        _db.Write(connection =>
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                using (var check = connection.CreateCommand())
                {
                    check.Transaction = transaction;
                    check.CommandText = "SELECT 1 FROM platforms WHERE name=@name;";
                    check.Parameters.AddWithValue("@name", newName);
                    if (check.ExecuteScalar() is not null)
                        throw new InvalidOperationException($"平台「{newName}」已存在。");
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE platforms SET name=@new, folder=CASE WHEN folder=@old THEN @new ELSE folder END WHERE name=@old;";
                    command.Parameters.AddWithValue("@new", newName);
                    command.Parameters.AddWithValue("@old", oldName);
                    command.ExecuteNonQuery();
                }

                using (var games = connection.CreateCommand())
                {
                    games.Transaction = transaction;
                    games.CommandText = "UPDATE games SET platform=@new WHERE platform=@old;";
                    games.Parameters.AddWithValue("@new", newName);
                    games.Parameters.AddWithValue("@old", oldName);
                    games.ExecuteNonQuery();
                }

                using (var lookups = connection.CreateCommand())
                {
                    lookups.Transaction = transaction;
                    lookups.CommandText = "UPDATE OR IGNORE lookup_items SET value=@new WHERE kind='platform' AND value=@old;";
                    lookups.Parameters.AddWithValue("@new", newName);
                    lookups.Parameters.AddWithValue("@old", oldName);
                    lookups.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        });
    }

    /// <summary>平台名 → 库内子目录（为空则同名）。</summary>
    public string FolderFor(string platform)
    {
        var item = Get(platform);
        return string.IsNullOrWhiteSpace(item?.Folder) ? platform : item!.Folder;
    }
}
