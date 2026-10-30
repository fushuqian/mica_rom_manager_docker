using System.Globalization;
using Microsoft.Data.Sqlite;
using MicaRomManager.Core.Models;

namespace MicaRomManager.Core.Data;

public sealed class GameRepository
{
    public const string Columns =
        "id,library_id,title,platform,genre,region,language,special_note,release_year,publisher,developer," +
        "favorite,rating,status,rom_path,disc_paths,file_size,rom_hash,cover_file,notes,tags," +
        "play_count,play_seconds,last_played_at,missing,created_at,updated_at";

    private readonly Db _db;

    public GameRepository(Db db) => _db = db;

    public PagedResult<Game> Query(GameFilter filter, GameSort sort, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var (where, parameters) = BuildWhere(filter);
        using var connection = _db.Open();

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM games{where};";
        AddParameters(countCommand, parameters);
        var total = Convert.ToInt32(countCommand.ExecuteScalar());

        using var listCommand = connection.CreateCommand();
        listCommand.CommandText =
            $"SELECT {Columns} FROM games{where} ORDER BY {sort.Resolve()} {(sort.Descending ? "DESC" : "ASC")}, id LIMIT @take OFFSET @skip;";
        AddParameters(listCommand, parameters);
        listCommand.Parameters.AddWithValue("@take", pageSize);
        listCommand.Parameters.AddWithValue("@skip", (page - 1) * pageSize);

        var games = new List<Game>();
        using var reader = listCommand.ExecuteReader();
        while (reader.Read()) games.Add(Map(reader));

        return new PagedResult<Game>(games, total);
    }

    public IReadOnlyList<Game> ListAll()
    {
        var games = new List<Game>();
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM games ORDER BY title COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) games.Add(Map(reader));
        return games;
    }

    public Game? Get(long id)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM games WHERE id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Game> GetMany(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return Array.Empty<Game>();

        var result = new List<Game>();
        using var connection = _db.Open();
        foreach (var chunk in Chunk(ids, 400))
        {
            using var command = connection.CreateCommand();
            var names = new List<string>();
            var index = 0;
            foreach (var id in chunk)
            {
                names.Add($"@id{index}");
                command.Parameters.AddWithValue($"@id{index}", id);
                index++;
            }

            command.CommandText = $"SELECT {Columns} FROM games WHERE id IN ({string.Join(",", names)});";
            using var reader = command.ExecuteReader();
            while (reader.Read()) result.Add(Map(reader));
        }

        return result;
    }

    public long Insert(Game game)
    {
        if (game.CreatedAt == default) game.CreatedAt = DateTime.Now;
        game.UpdatedAt = DateTime.Now;

        return _db.Write(connection =>
        {
            using var command = BuildInsert(connection, game);
            game.Id = Convert.ToInt64(command.ExecuteScalar());
            return game.Id;
        });
    }

    /// <summary>批量插入（扫描用），单事务；返回成功条数。</summary>
    public int InsertMany(IReadOnlyList<Game> games)
    {
        if (games.Count == 0) return 0;

        return _db.Write(connection =>
        {
            using var transaction = connection.BeginTransaction();
            var inserted = 0;
            foreach (var game in games)
            {
                game.UpdatedAt = DateTime.Now;
                if (game.CreatedAt == default) game.CreatedAt = DateTime.Now;
                try
                {
                    using var command = BuildInsert(connection, game);
                    command.Transaction = transaction;
                    command.ExecuteNonQuery();
                    inserted++;
                }
                catch (SqliteException)
                {
                    // 路径唯一冲突等：跳过该条，不影响整批
                }
            }

            transaction.Commit();
            return inserted;
        });
    }

    public void Update(Game game)
    {
        game.UpdatedAt = DateTime.Now;
        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE games SET
                    library_id=@library_id, title=@title, platform=@platform, genre=@genre, region=@region,
                    language=@language, special_note=@special_note, release_year=@release_year,
                    publisher=@publisher, developer=@developer, favorite=@favorite, rating=@rating,
                    status=@status, rom_path=@rom_path, disc_paths=@disc_paths, file_size=@file_size,
                    rom_hash=@rom_hash, cover_file=@cover_file, notes=@notes, tags=@tags,
                    play_count=@play_count, play_seconds=@play_seconds, last_played_at=@last_played_at,
                    missing=@missing, updated_at=@updated_at
                WHERE id=@id;
                """;
            AddGameParameters(command, game);
            command.Parameters.AddWithValue("@id", game.Id);
            command.ExecuteNonQuery();
        });
    }

    /// <summary>按字段补丁批量更新；返回受影响条数。</summary>
    public int UpdateFields(IReadOnlyCollection<long> ids, GamePatch patch)
    {
        if (ids.Count == 0 || !patch.HasAny) return 0;

        var sets = new List<string>();
        var values = new List<(string Name, object? Value)>();

        void Add(string column, object? value)
        {
            var name = "@p" + values.Count;
            sets.Add($"{column}={name}");
            values.Add((name, value));
        }

        if (patch.Title is not null) Add("title", patch.Title);
        if (patch.Platform is not null) Add("platform", patch.Platform);
        if (patch.Genre is not null) Add("genre", patch.Genre);
        if (patch.Region is not null) Add("region", patch.Region);
        if (patch.Language is not null) Add("language", patch.Language);
        if (patch.SpecialNote is not null) Add("special_note", patch.SpecialNote);
        if (patch.ReleaseYear is not null) Add("release_year", patch.ReleaseYear);
        if (patch.Publisher is not null) Add("publisher", patch.Publisher);
        if (patch.Developer is not null) Add("developer", patch.Developer);
        if (patch.Favorite is not null) Add("favorite", patch.Favorite.Value ? 1 : 0);
        if (patch.Rating is not null) Add("rating", Math.Clamp(patch.Rating.Value, 0, 5));
        if (patch.Status is not null) Add("status", patch.Status);
        if (patch.Tags is not null) Add("tags", patch.Tags);
        if (patch.Notes is not null) Add("notes", patch.Notes);

        sets.Add("updated_at=@updated_at");

        return _db.Write(connection =>
        {
            using var transaction = connection.BeginTransaction();
            var updated = 0;
            foreach (var chunk in Chunk(ids, 400))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var idNames = new List<string>();
                var index = 0;
                foreach (var id in chunk)
                {
                    var name = "@id" + index;
                    idNames.Add(name);
                    command.Parameters.AddWithValue(name, id);
                    index++;
                }

                command.CommandText =
                    $"UPDATE games SET {string.Join(",", sets)} WHERE id IN ({string.Join(",", idNames)});";
                foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                command.Parameters.AddWithValue("@updated_at", DateTime.Now.ToString("O"));
                updated += command.ExecuteNonQuery();
            }

            transaction.Commit();
            return updated;
        });
    }

    public void Delete(long id)
    {
        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM games WHERE id=@id;";
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        });
    }

    public int DeleteMany(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0) return 0;

        return _db.Write(connection =>
        {
            using var transaction = connection.BeginTransaction();
            var deleted = 0;
            foreach (var chunk in Chunk(ids, 400))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var names = new List<string>();
                var index = 0;
                foreach (var id in chunk)
                {
                    names.Add($"@id{index}");
                    command.Parameters.AddWithValue($"@id{index}", id);
                    index++;
                }

                command.CommandText = $"DELETE FROM games WHERE id IN ({string.Join(",", names)});";
                deleted += command.ExecuteNonQuery();
            }

            transaction.Commit();
            return deleted;
        });
    }

    public int SetMissing(IReadOnlyCollection<long> ids, bool missing)
    {
        if (ids.Count == 0) return 0;

        return _db.Write(connection =>
        {
            using var transaction = connection.BeginTransaction();
            var updated = 0;
            foreach (var chunk in Chunk(ids, 400))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                var names = new List<string>();
                var index = 0;
                foreach (var id in chunk)
                {
                    names.Add($"@id{index}");
                    command.Parameters.AddWithValue($"@id{index}", id);
                    index++;
                }

                command.CommandText = $"UPDATE games SET missing=@missing WHERE id IN ({string.Join(",", names)});";
                command.Parameters.AddWithValue("@missing", missing ? 1 : 0);
                updated += command.ExecuteNonQuery();
            }

            transaction.Commit();
            return updated;
        });
    }

    public void SetHash(long id, string hash)
    {
        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE games SET rom_hash=@hash, updated_at=@now WHERE id=@id;";
            command.Parameters.AddWithValue("@hash", hash);
            command.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        });
    }

    public void AddPlayStats(long id, long seconds)
    {
        seconds = Math.Max(0, seconds);
        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE games SET
                    play_count = play_count + 1,
                    play_seconds = play_seconds + @seconds,
                    last_played_at = @now,
                    status = CASE WHEN status = '未玩' THEN '在玩' ELSE status END,
                    updated_at = @now
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("@seconds", seconds);
            command.Parameters.AddWithValue("@now", DateTime.Now.ToString("O"));
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        });
    }

    public HashSet<string> AllPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rom_path FROM games;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) paths.Add(reader.GetString(0));
        return paths;
    }

    public HashSet<string> AllHashes()
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rom_hash FROM games WHERE rom_hash <> '';";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (!reader.IsDBNull(0)) hashes.Add(reader.GetString(0));
        return hashes;
    }

    public IReadOnlyList<(long Id, string Path, bool Missing)> PathsByLibrary(long libraryId)
    {
        var result = new List<(long, string, bool)>();
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, rom_path, missing FROM games WHERE library_id=@id;";
        command.Parameters.AddWithValue("@id", libraryId);
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2) != 0));
        return result;
    }

    public int CountAll()
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM games;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IReadOnlyList<string> Distinct(string column)
    {
        var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["platform"] = "platform",
            ["region"] = "region",
            ["language"] = "language",
            ["genre"] = "genre",
            ["developer"] = "developer",
            ["publisher"] = "publisher",
            ["status"] = "status",
        };
        if (!allowed.TryGetValue(column, out var sqlColumn)) return Array.Empty<string>();

        var values = new List<string>();
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT DISTINCT {sqlColumn} FROM games WHERE {sqlColumn} <> '' ORDER BY {sqlColumn} COLLATE NOCASE;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values;
    }

    public IReadOnlyList<string> DistinctTags()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tags FROM games WHERE tags <> '';";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            foreach (var tag in reader.GetString(0).Split(
                         new[] { ',', '，', ';', '；' },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                tags.Add(tag);
        }

        return tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>平台侧栏计数（含 0 条的平台由平台表补齐）。</summary>
    public IReadOnlyDictionary<string, int> CountByPlatform()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT platform, COUNT(*) FROM games GROUP BY platform;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    public LibraryStatistics Statistics()
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*),
                COALESCE(SUM(favorite), 0),
                COALESCE(SUM(missing), 0),
                COALESCE(SUM(CASE WHEN play_count > 0 THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(play_seconds), 0)
            FROM games;
            """;
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new LibraryStatistics(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt64(4))
            : new LibraryStatistics(0, 0, 0, 0, 0);
    }

    private static (string Where, List<(string Name, object Value)> Parameters) BuildWhere(GameFilter filter)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object)>();

        void Add(string sql, string name, object value)
        {
            conditions.Add(sql);
            parameters.Add((name, value));
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            Add("(title LIKE @keyword OR rom_path LIKE @keyword)", "@keyword", "%" + filter.Keyword.Trim() + "%");
        }

        if (!string.IsNullOrWhiteSpace(filter.Platform))
            Add("platform = @platform", "@platform", filter.Platform.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Status))
            Add("status = @status", "@status", filter.Status.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Region))
            Add("region = @region", "@region", filter.Region.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Language))
            Add("language = @language", "@language", filter.Language.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Genre))
            Add("genre = @genre", "@genre", filter.Genre.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Developer))
            Add("developer = @developer", "@developer", filter.Developer.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Tag))
            Add("instr(',' || replace(tags, '，', ',') || ',', @tag) > 0", "@tag", "," + filter.Tag.Trim() + ",");
        if (filter.OnlyFavorites) conditions.Add("favorite = 1");
        if (filter.OnlyMissing) conditions.Add("missing = 1");
        if (filter.YearMin is not null) Add("release_year >= @yearMin", "@yearMin", filter.YearMin.Value);
        if (filter.YearMax is not null) Add("release_year <= @yearMax", "@yearMax", filter.YearMax.Value);
        if (filter.LibraryId is not null) Add("library_id = @libraryId", "@libraryId", filter.LibraryId.Value);

        var where = conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions);
        return (where, parameters);
    }

    private static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
    }

    private static SqliteCommand BuildInsert(SqliteConnection connection, Game game)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO games
                (library_id,title,platform,genre,region,language,special_note,release_year,publisher,developer,
                 favorite,rating,status,rom_path,disc_paths,file_size,rom_hash,cover_file,notes,tags,
                 play_count,play_seconds,last_played_at,missing,created_at,updated_at)
            VALUES
                (@library_id,@title,@platform,@genre,@region,@language,@special_note,@release_year,@publisher,@developer,
                 @favorite,@rating,@status,@rom_path,@disc_paths,@file_size,@rom_hash,@cover_file,@notes,@tags,
                 @play_count,@play_seconds,@last_played_at,@missing,@created_at,@updated_at);
            SELECT last_insert_rowid();
            """;
        AddGameParameters(command, game);
        return command;
    }

    private static void AddGameParameters(SqliteCommand command, Game game)
    {
        command.Parameters.AddWithValue("@library_id", (object?)game.LibraryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@title", game.Title);
        command.Parameters.AddWithValue("@platform", game.Platform);
        command.Parameters.AddWithValue("@genre", game.Genre);
        command.Parameters.AddWithValue("@region", game.Region);
        command.Parameters.AddWithValue("@language", game.Language);
        command.Parameters.AddWithValue("@special_note", game.SpecialNote);
        command.Parameters.AddWithValue("@release_year", (object?)game.ReleaseYear ?? DBNull.Value);
        command.Parameters.AddWithValue("@publisher", game.Publisher);
        command.Parameters.AddWithValue("@developer", game.Developer);
        command.Parameters.AddWithValue("@favorite", game.Favorite ? 1 : 0);
        command.Parameters.AddWithValue("@rating", Math.Clamp(game.Rating, 0, 5));
        command.Parameters.AddWithValue("@status", game.Status);
        command.Parameters.AddWithValue("@rom_path", game.RomPath);
        command.Parameters.AddWithValue("@disc_paths", game.DiscPaths);
        command.Parameters.AddWithValue("@file_size", game.FileSize);
        command.Parameters.AddWithValue("@rom_hash", game.RomHash);
        command.Parameters.AddWithValue("@cover_file", game.CoverFile);
        command.Parameters.AddWithValue("@notes", game.Notes);
        command.Parameters.AddWithValue("@tags", game.Tags);
        command.Parameters.AddWithValue("@play_count", game.PlayCount);
        command.Parameters.AddWithValue("@play_seconds", game.PlaySeconds);
        command.Parameters.AddWithValue("@last_played_at", (object?)game.LastPlayedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("@missing", game.Missing ? 1 : 0);
        command.Parameters.AddWithValue("@created_at", game.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@updated_at", game.UpdatedAt.ToString("O"));
    }

    private static Game Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        LibraryId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
        Title = reader.GetString(2),
        Platform = reader.GetString(3),
        Genre = reader.GetString(4),
        Region = reader.GetString(5),
        Language = reader.GetString(6),
        SpecialNote = reader.GetString(7),
        ReleaseYear = reader.IsDBNull(8) ? null : reader.GetInt32(8),
        Publisher = reader.GetString(9),
        Developer = reader.GetString(10),
        Favorite = reader.GetInt64(11) != 0,
        Rating = reader.GetInt32(12),
        Status = reader.GetString(13),
        RomPath = reader.GetString(14),
        DiscPaths = reader.GetString(15),
        FileSize = reader.GetInt64(16),
        RomHash = reader.GetString(17),
        CoverFile = reader.GetString(18),
        Notes = reader.GetString(19),
        Tags = reader.GetString(20),
        PlayCount = reader.GetInt32(21),
        PlaySeconds = reader.GetInt64(22),
        LastPlayedAt = reader.IsDBNull(23) ? null : ParseDate(reader.GetString(23)),
        Missing = reader.GetInt64(24) != 0,
        CreatedAt = ParseDate(reader.GetString(25)),
        UpdatedAt = ParseDate(reader.GetString(26)),
    };

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : DateTime.Now;

    public static IEnumerable<List<T>> Chunk<T>(IReadOnlyCollection<T> source, int size)
    {
        var buffer = new List<T>(size);
        foreach (var item in source)
        {
            buffer.Add(item);
            if (buffer.Count < size) continue;
            yield return buffer;
            buffer = new List<T>(size);
        }

        if (buffer.Count > 0) yield return buffer;
    }
}

public sealed record LibraryStatistics(int Total, int Favorites, int Missing, int Played, long TotalPlaySeconds);
