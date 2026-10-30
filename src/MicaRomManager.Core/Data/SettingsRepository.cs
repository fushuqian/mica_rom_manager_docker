namespace MicaRomManager.Core.Data;

public sealed class SettingsRepository
{
    private readonly Db _db;

    public SettingsRepository(Db db) => _db = db;

    public string? Get(string key)
    {
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key=@key;";
        command.Parameters.AddWithValue("@key", key);
        return command.ExecuteScalar() as string;
    }

    public void Set(string key, string? value)
    {
        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO settings(key, value) VALUES (@key, @value)
                ON CONFLICT(key) DO UPDATE SET value=@value;
                """;
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
            command.ExecuteNonQuery();
        });
    }

    public bool GetBool(string key, bool fallback = false) =>
        bool.TryParse(Get(key), out var value) ? value : fallback;

    public int GetInt(string key, int fallback = 0) =>
        int.TryParse(Get(key), out var value) ? value : fallback;

    public void Remove(string key)
    {
        _db.Write(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM settings WHERE key=@key;";
            command.Parameters.AddWithValue("@key", key);
            command.ExecuteNonQuery();
        });
    }

    public IReadOnlyDictionary<string, string> GetWithPrefix(string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings WHERE key LIKE @prefix;";
        command.Parameters.AddWithValue("@prefix", prefix + "%");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(1)) result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }
}
