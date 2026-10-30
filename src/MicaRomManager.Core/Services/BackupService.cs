using System.IO;
using Microsoft.Data.Sqlite;
using MicaRomManager.Core.Data;

namespace MicaRomManager.Core.Services;

public sealed record BackupFile(string Name, string FullPath, long Size, DateTime CreatedAt);

/// <summary>数据库备份/恢复：VACUUM INTO 生成一致快照，恢复前自动备份当前库。</summary>
public sealed class BackupService
{
    private readonly Db _db;
    private readonly string _backupDir;

    public BackupService(Db db)
    {
        _db = db;
        _backupDir = Path.Combine(db.DataDir, "backups");
        Directory.CreateDirectory(_backupDir);
    }

    public BackupFile Create(string? label = null)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var name = string.IsNullOrWhiteSpace(label) ? $"mica-{stamp}.db" : $"mica-{stamp}-{Sanitize(label)}.db";
        var target = Path.Combine(_backupDir, name);

        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO @target;";
            command.Parameters.AddWithValue("@target", target);
            command.ExecuteNonQuery();
        });

        return new BackupFile(name, target, new FileInfo(target).Length, DateTime.Now);
    }

    public IReadOnlyList<BackupFile> List()
    {
        var files = new List<BackupFile>();
        foreach (var path in Directory.EnumerateFiles(_backupDir, "*.db"))
        {
            var info = new FileInfo(path);
            files.Add(new BackupFile(info.Name, path, info.Length, info.CreationTime));
        }

        return files.OrderByDescending(file => file.CreatedAt).ToList();
    }

    public (bool Ok, string Message) Restore(Stream backup, string originalName)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"mica-restore-{Guid.NewGuid():N}.db");
        try
        {
            using (var output = File.Create(temp))
            {
                backup.CopyTo(output);
            }

            if (!IsSqlite(temp)) return (false, "上传的文件不是有效的 SQLite 数据库。");

            var safety = Create("before-restore");
            SqliteConnection.ClearAllPools();
            File.Copy(temp, _db.DbPath, true);
            return (true, $"已恢复，恢复前快照：{safety.Name}。请重启容器使所有连接使用新库。");
        }
        catch (Exception ex)
        {
            return (false, $"恢复失败：{ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

    public bool Delete(string name)
    {
        var safe = Path.GetFileName(name);
        if (!string.Equals(safe, name, StringComparison.Ordinal)) return false;
        var path = Path.Combine(_backupDir, safe);
        if (!File.Exists(path)) return false;

        try
        {
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSqlite(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[16];
            if (stream.Read(header, 0, header.Length) < header.Length) return false;
            return System.Text.Encoding.ASCII.GetString(header) == "SQLite format 3\0";
        }
        catch
        {
            return false;
        }
    }

    private static string Sanitize(string text)
    {
        var chars = text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var result = new string(chars);
        return result.Length <= 40 ? result : result[..40];
    }

    /// <summary>恢复后释放连接池，避免旧文件句柄。</summary>
    public static void ClearPools() => SqliteConnection.ClearAllPools();
}
