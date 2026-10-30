using System.IO;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-mig-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void FreshDatabase_HasVersionOne_AndSeedsPlatforms()
    {
        var db = new Db(_dir);
        using var connection = db.Open();
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(1, Convert.ToInt32(version.ExecuteScalar()));

        using var platforms = connection.CreateCommand();
        platforms.CommandText = "SELECT COUNT(*) FROM platforms;";
        Assert.True(Convert.ToInt32(platforms.ExecuteScalar()) >= 25);

        using var settings = connection.CreateCommand();
        settings.CommandText = "SELECT COUNT(*) FROM lookup_items;";
        Assert.True(Convert.ToInt32(settings.ExecuteScalar()) > 10);
    }

    [Fact]
    public void Reopen_IsIdempotent_AndKeepsData()
    {
        var path = Path.Combine(_dir, "mica.db");
        var first = new Db(_dir);
        var games = new GameRepository(first);
        games.Insert(TempWorkspace.NewGame(Path.Combine(_dir, "a.nes"), "A"));
        first.Dispose();

        var second = new Db(_dir);
        Assert.True(File.Exists(path));
        Assert.Single(new GameRepository(second).ListAll());

        using var connection = second.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
    }

    [Fact]
    public void JournalMode_IsWal()
    {
        var db = new Db(_dir);
        using var connection = db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", Convert.ToString(command.ExecuteScalar())?.ToLowerInvariant());

        using var foreign = connection.CreateCommand();
        foreign.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1, Convert.ToInt32(foreign.ExecuteScalar()));
    }
}
