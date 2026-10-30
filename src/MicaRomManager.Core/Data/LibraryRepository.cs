using System.IO;
using MicaRomManager.Core.Models;

namespace MicaRomManager.Core.Data;

public sealed class LibraryRepository
{
    private readonly Db _db;

    public LibraryRepository(Db db) => _db = db;

    public IReadOnlyList<Library> ListAll()
    {
        var result = new List<Library>();
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.id, l.name, l.root_path, l.created_at,
                   (SELECT COUNT(*) FROM games g WHERE g.library_id = l.id) AS game_count
            FROM libraries l
            ORDER BY l.name COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var root = reader.GetString(2);
            result.Add(new Library
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                RootPath = root,
                CreatedAt = DateTime.TryParse(reader.GetString(3), out var date) ? date : DateTime.Now,
                GameCount = reader.GetInt32(4),
                Writable = IsWritable(root),
            });
        }

        return result;
    }

    public Library? Get(long id) => ListAll().FirstOrDefault(library => library.Id == id);

    public (bool Ok, string Message) Add(string name, string rootPath)
    {
        var normalized = Normalize(rootPath);
        if (normalized.Length == 0) return (false, "路径不能为空。");
        if (!Path.IsPathRooted(normalized)) return (false, "必须使用容器内绝对路径，例如 /roms/nes。");
        if (!Directory.Exists(normalized)) return (false, $"目录不存在：{normalized}（请检查挂载与大小写）。");

        name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(normalized.TrimEnd('/')) : name.Trim();

        try
        {
            _db.Write(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO libraries(name, root_path, created_at) VALUES (@name, @root, @now);
                    """;
                command.Parameters.AddWithValue("@name", name);
                command.Parameters.AddWithValue("@root", normalized);
                command.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
                command.ExecuteNonQuery();
            });
            return (true, $"已添加库「{name}」。");
        }
        catch (Exception ex)
        {
            return (false, $"添加失败：{ex.Message}");
        }
    }

    public (bool Ok, string Message) Update(long id, string name, string rootPath)
    {
        var normalized = Normalize(rootPath);
        if (normalized.Length == 0 || !Directory.Exists(normalized))
            return (false, "目录不存在或路径为空。");

        try
        {
            _db.Write(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE libraries SET name=@name, root_path=@root WHERE id=@id;";
                command.Parameters.AddWithValue("@name", name.Trim());
                command.Parameters.AddWithValue("@root", normalized);
                command.Parameters.AddWithValue("@id", id);
                command.ExecuteNonQuery();
            });
            return (true, "已保存。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public (bool Ok, string Message) Delete(long id, bool deleteGames)
    {
        try
        {
            _db.Write(connection =>
            {
                using var transaction = connection.BeginTransaction();
                using (var games = connection.CreateCommand())
                {
                    games.Transaction = transaction;
                    games.CommandText = deleteGames
                        ? "DELETE FROM games WHERE library_id=@id;"
                        : "UPDATE games SET library_id=NULL WHERE library_id=@id;";
                    games.Parameters.AddWithValue("@id", id);
                    games.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM libraries WHERE id=@id;";
                    command.Parameters.AddWithValue("@id", id);
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            });
            return (true, deleteGames ? "库与记录已删除。" : "库已删除，游戏记录保留。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static string Normalize(string path)
    {
        var trimmed = (path ?? "").Trim();
        if (trimmed.Length == 0) return "";
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch
        {
            return "";
        }
    }

    /// <summary>通过试写探测目录是否可写（随后删除探测文件）。</summary>
    public static bool IsWritable(string root)
    {
        if (!Directory.Exists(root)) return false;
        var probe = Path.Combine(root, $".mica-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
