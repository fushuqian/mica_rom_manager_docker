using Microsoft.Data.Sqlite;

namespace MicaRomManager.Core.Data;

/// <summary>区域/语言/类型/发行商/开发商/标签/平台 词典维护，改名时级联到游戏记录。</summary>
public sealed class LookupRepository
{
    public const string Region = "region";
    public const string Language = "language";
    public const string Genre = "genre";
    public const string Publisher = "publisher";
    public const string Developer = "developer";
    public const string Tag = "tag";
    public const string Platform = "platform";

    public static readonly string[] Kinds = { Platform, Region, Language, Genre, Publisher, Developer, Tag };

    private readonly Db _db;
    private readonly PlatformRepository _platforms;

    public LookupRepository(Db db, PlatformRepository platforms)
    {
        _db = db;
        _platforms = platforms;
    }

    public IReadOnlyList<string> List(string kind)
    {
        var values = new List<string>();
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM lookup_items WHERE kind=@kind ORDER BY value COLLATE NOCASE;";
        command.Parameters.AddWithValue("@kind", kind);
        using var reader = command.ExecuteReader();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values;
    }

    public void Add(string kind, string value)
    {
        value = value.Trim();
        if (value.Length == 0) return;

        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO lookup_items(kind, value) VALUES (@kind, @value);";
            command.Parameters.AddWithValue("@kind", kind);
            command.Parameters.AddWithValue("@value", value);
            command.ExecuteNonQuery();
        });

        if (kind == Platform) _platforms.Ensure(value);
    }

    public int CountUsage(string kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;

        using var connection = _db.Open();
        if (kind == Tag)
        {
            using var tagCommand = connection.CreateCommand();
            tagCommand.CommandText =
                "SELECT COUNT(*) FROM games WHERE instr(',' || replace(tags, '，', ',') || ',', @pattern) > 0;";
            tagCommand.Parameters.AddWithValue("@pattern", "," + value.Trim() + ",");
            return Convert.ToInt32(tagCommand.ExecuteScalar());
        }

        if (kind == Platform)
        {
            using var platformCommand = connection.CreateCommand();
            platformCommand.CommandText = "SELECT COUNT(*) FROM games WHERE platform=@value;";
            platformCommand.Parameters.AddWithValue("@value", value.Trim());
            return Convert.ToInt32(platformCommand.ExecuteScalar());
        }

        var column = ColumnFor(kind);
        if (column is null) return 0;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM games WHERE {column}=@value;";
        command.Parameters.AddWithValue("@value", value.Trim());
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public (bool Ok, string Message) Rename(string kind, string oldValue, string newValue)
    {
        oldValue = oldValue.Trim();
        newValue = newValue.Trim();
        if (newValue.Length == 0) return (false, "新名称不能为空。");
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return (true, "名称未变化。");

        if (kind == Platform)
        {
            try
            {
                _platforms.Rename(oldValue, newValue);
                _db.Write(connection =>
                {
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "UPDATE OR IGNORE lookup_items SET value=@new WHERE kind='platform' AND value=@old;";
                    command.Parameters.AddWithValue("@new", newValue);
                    command.Parameters.AddWithValue("@old", oldValue);
                    command.ExecuteNonQuery();
                });
                return (true, $"已重命名为「{newValue}」。");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        if (kind == Tag) return RenameTag(oldValue, newValue);

        var column = ColumnFor(kind);
        if (column is null) return (false, "未知词典类型。");

        try
        {
            _db.Write(connection =>
            {
                using var transaction = connection.BeginTransaction();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = $"UPDATE games SET {column}=@new WHERE {column}=@old;";
                    command.Parameters.AddWithValue("@new", newValue);
                    command.Parameters.AddWithValue("@old", oldValue);
                    command.ExecuteNonQuery();
                }

                using (var lookup = connection.CreateCommand())
                {
                    lookup.Transaction = transaction;
                    lookup.CommandText = """
                        INSERT OR IGNORE INTO lookup_items(kind, value) VALUES (@kind, @new);
                        DELETE FROM lookup_items WHERE kind=@kind AND value=@old;
                        """;
                    lookup.Parameters.AddWithValue("@kind", kind);
                    lookup.Parameters.AddWithValue("@new", newValue);
                    lookup.Parameters.AddWithValue("@old", oldValue);
                    lookup.ExecuteNonQuery();
                }

                transaction.Commit();
            });
            return (true, $"已重命名为「{newValue}」。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private (bool Ok, string Message) RenameTag(string oldValue, string newValue)
    {
        try
        {
            _db.Write(connection =>
            {
                var rows = new List<(long Id, string Tags)>();
                using (var read = connection.CreateCommand())
                {
                    read.CommandText = "SELECT id, tags FROM games WHERE tags <> '';";
                    using var reader = read.ExecuteReader();
                    while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1)));
                }

                using var transaction = connection.BeginTransaction();
                foreach (var (id, tags) in rows)
                {
                    var parts = tags.Split(new[] { ',', '，', ';', '；' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!parts.Any(part => string.Equals(part, oldValue, StringComparison.OrdinalIgnoreCase))) continue;

                    var rebuilt = parts
                        .Select(part => string.Equals(part, oldValue, StringComparison.OrdinalIgnoreCase) ? newValue : part)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE games SET tags=@tags, updated_at=@now WHERE id=@id;";
                    update.Parameters.AddWithValue("@tags", string.Join(",", rebuilt));
                    update.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
                    update.Parameters.AddWithValue("@id", id);
                    update.ExecuteNonQuery();
                }

                using (var lookup = connection.CreateCommand())
                {
                    lookup.Transaction = transaction;
                    lookup.CommandText = """
                        INSERT OR IGNORE INTO lookup_items(kind, value) VALUES ('tag', @new);
                        DELETE FROM lookup_items WHERE kind='tag' AND value=@old;
                        """;
                    lookup.Parameters.AddWithValue("@new", newValue);
                    lookup.Parameters.AddWithValue("@old", oldValue);
                    lookup.ExecuteNonQuery();
                }

                transaction.Commit();
            });
            return (true, $"已重命名为「{newValue}」。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public (bool Ok, string Message) Delete(string kind, string value, bool force = false)
    {
        if (kind == Platform)
        {
            var (platformOk, platformMessage) = _platforms.Delete(value);
            if (!platformOk) return (false, platformMessage);
        }

        var usage = CountUsage(kind, value);
        if (usage > 0 && !force) return (false, $"仍有 {usage} 款游戏在使用「{value}」，不能删除。");

        try
        {
            _db.Write(connection =>
            {
                using var transaction = connection.BeginTransaction();

                if (kind == Tag)
                {
                    var rows = new List<(long Id, string Tags)>();
                    using (var read = connection.CreateCommand())
                    {
                        read.Transaction = transaction;
                        read.CommandText = "SELECT id, tags FROM games WHERE tags <> '';";
                        using var reader = read.ExecuteReader();
                        while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1)));
                    }

                    foreach (var (id, tags) in rows)
                    {
                        var parts = tags.Split(new[] { ',', '，', ';', '；' },
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        var remaining = parts
                            .Where(part => !string.Equals(part, value, StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        if (remaining.Length == parts.Length) continue;

                        using var update = connection.CreateCommand();
                        update.Transaction = transaction;
                        update.CommandText = "UPDATE games SET tags=@tags WHERE id=@id;";
                        update.Parameters.AddWithValue("@tags", string.Join(",", remaining));
                        update.Parameters.AddWithValue("@id", id);
                        update.ExecuteNonQuery();
                    }
                }
                else
                {
                    var column = ColumnFor(kind);
                    if (column is not null && force && usage > 0)
                    {
                        using var clear = connection.CreateCommand();
                        clear.Transaction = transaction;
                        clear.CommandText = $"UPDATE games SET {column}='' WHERE {column}=@value;";
                        clear.Parameters.AddWithValue("@value", value.Trim());
                        clear.ExecuteNonQuery();
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM lookup_items WHERE kind=@kind AND value=@value;";
                    command.Parameters.AddWithValue("@kind", kind);
                    command.Parameters.AddWithValue("@value", value.Trim());
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            });

            return (true, $"已删除「{value}」。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string? ColumnFor(string kind) => kind switch
    {
        Region => "region",
        Language => "language",
        Genre => "genre",
        Publisher => "publisher",
        Developer => "developer",
        _ => null,
    };
}
