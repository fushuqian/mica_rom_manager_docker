using System.IO;
using Microsoft.Data.Sqlite;

namespace MicaRomManager.Core.Data;

/// <summary>SQLite 连接工厂 + 迁移 + 写闸门。所有写操作串行，读操作各自开连接。</summary>
public sealed class Db : IDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public string DbPath { get; }
    public string DataDir { get; }

    public Db(string dataDir)
    {
        DataDir = Path.GetFullPath(dataDir);
        Directory.CreateDirectory(DataDir);
        DbPath = Path.Combine(DataDir, "mica.db");
        Migrate();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>串行执行写操作；回调内应使用传入的连接。</summary>
    public T Write<T>(Func<SqliteConnection, T> action)
    {
        _writeGate.Wait();
        try
        {
            using var connection = Open();
            return action(connection);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Write(Action<SqliteConnection> action) => Write<object?>(connection =>
    {
        action(connection);
        return null;
    });

    private void Migrate()
    {
        using var connection = Open();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }

        var version = ReadVersion(connection);
        foreach (var migration in Schema.Migrations.Where(m => m.Version > version).OrderBy(m => m.Version))
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = migrationBuilder(migration.Version);
                    command.ExecuteNonQuery();
                }

                Db.SeedDefaults(connection, transaction);

                using (var stamp = connection.CreateCommand())
                {
                    stamp.Transaction = transaction;
                    stamp.CommandText = $"PRAGMA user_version = {migration.Version};";
                    stamp.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // 首次启动或新增平台时补齐默认值（幂等）
        SeedDefaults(connection, null);
    }

    private static string migrationBuilder(int version) => version switch
    {
        1 => Schema.V1,
        _ => throw new InvalidOperationException($"未知数据库版本 {version}"),
    };

    private static int ReadVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>写入默认平台与词典；幂等，可重复调用。</summary>
    private static void SeedDefaults(SqliteConnection connection, SqliteTransaction? transaction)
    {
        foreach (var platform in Services.PlatformFormats.Defaults)
        {
            using var cmd = connection.CreateCommand();
            if (transaction is not null) cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT OR IGNORE INTO platforms(name, folder, formats, sort_order)
                VALUES (@name, @name, @formats, @order);
                """;
            cmd.Parameters.AddWithValue("@name", platform.Key);
            cmd.Parameters.AddWithValue("@formats", string.Join(" ", platform.Value));
            cmd.Parameters.AddWithValue("@order", IndexOf(platform.Key));
            cmd.ExecuteNonQuery();

            using var lookup = connection.CreateCommand();
            if (transaction is not null) lookup.Transaction = transaction;
            lookup.CommandText = "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES ('platform', @name);";
            lookup.Parameters.AddWithValue("@name", platform.Key);
            lookup.ExecuteNonQuery();
        }

        foreach (var platform in new[] { "街机", "其他", "未指定" })
        {
            using var cmd = connection.CreateCommand();
            if (transaction is not null) cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO platforms(name, folder, formats, sort_order) VALUES (@name, @name, '', @order);";
            cmd.Parameters.AddWithValue("@name", platform);
            cmd.Parameters.AddWithValue("@order", IndexOf(platform));
            cmd.ExecuteNonQuery();
        }

        foreach (var (kind, values) in Schema.DefaultLookups)
        {
            foreach (var value in values)
            {
                using var cmd = connection.CreateCommand();
                if (transaction is not null) cmd.Transaction = transaction;
                cmd.CommandText = "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES (@kind, @value);";
                cmd.Parameters.AddWithValue("@kind", kind);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private static int IndexOf(string name)
    {
        var keys = Services.PlatformFormats.Defaults.Keys.ToList();
        var index = keys.FindIndex(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : keys.Count + (name == "街机" ? 0 : name == "其他" ? 1 : 2);
    }

    public void Dispose() => _writeGate.Dispose();
}
