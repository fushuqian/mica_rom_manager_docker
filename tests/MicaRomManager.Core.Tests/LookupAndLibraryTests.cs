using System.IO;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class LookupAndLibraryTests : IDisposable
{
    private readonly TempWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void Lookup_RenameCascadesToGames()
    {
        var game = TempWorkspace.NewGame(Path.Combine(_ws.Root, "a.nes"));
        game.Region = "日版";
        var id = _ws.Games.Insert(game);

        var (ok, message) = _ws.Lookups.Rename(LookupRepository.Region, "日版", "日本版");
        Assert.True(ok, message);
        Assert.Equal("日本版", _ws.Games.Get(id)!.Region);
        Assert.Contains("日本版", _ws.Lookups.List(LookupRepository.Region));
        Assert.DoesNotContain("日版", _ws.Lookups.List(LookupRepository.Region));
    }

    [Fact]
    public void Lookup_RenameTag_RewritesAllRows()
    {
        var game = TempWorkspace.NewGame(Path.Combine(_ws.Root, "tag.nes"));
        game.Tags = "经典,动作";
        var id = _ws.Games.Insert(game);

        var (ok, message) = _ws.Lookups.Rename(LookupRepository.Tag, "经典", "神作");
        Assert.True(ok, message);
        Assert.Equal("神作,动作", _ws.Games.Get(id)!.Tags);
    }

    [Fact]
    public void Lookup_DeleteUsed_IsRefused_UnlessForced()
    {
        var game = TempWorkspace.NewGame(Path.Combine(_ws.Root, "used.nes"));
        game.Language = "简体中文";
        var id = _ws.Games.Insert(game);

        Assert.False(_ws.Lookups.Delete(LookupRepository.Language, "简体中文").Ok);
        Assert.True(_ws.Lookups.Delete(LookupRepository.Language, "简体中文", force: true).Ok);
        Assert.Equal("", _ws.Games.Get(id)!.Language);
    }

    [Fact]
    public void Library_DeleteKeepsGamesByDefault()
    {
        _ws.CreateLibrary("lib");
        var game = TempWorkspace.NewGame(Path.Combine(_ws.Root, "roms", "a.nes"));
        game.LibraryId = 1;
        var id = _ws.Games.Insert(game);

        var result = _ws.Libraries.Delete(1, deleteGames: false);
        Assert.True(result.Ok, result.Message);
        Assert.NotNull(_ws.Games.Get(id));
        Assert.Null(_ws.Games.Get(id)!.LibraryId);

        var second = _ws.Libraries.Add("lib2", Path.Combine(_ws.Root, "roms"));
        Assert.True(second.Ok, second.Message);
    }

    [Fact]
    public void Library_AddRejectsMissingDirectory()
    {
        var result = _ws.Libraries.Add("x", Path.Combine(_ws.Root, "nope"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        _ws.Settings.Set("a.b", "1");
        Assert.Equal("1", _ws.Settings.Get("a.b"));
        Assert.True(_ws.Settings.GetBool("flag", true));

        _ws.Settings.Set("flag", "true");
        Assert.True(_ws.Settings.GetBool("flag"));

        _ws.Settings.Remove("a.b");
        Assert.Null(_ws.Settings.Get("a.b"));
    }

    [Fact]
    public void Csv_HasBomAndQuotes()
    {
        var game = new Game { Title = "A,B", RomPath = "/x/a.nes", Notes = "he said \"hi\"" };
        var bytes = CsvExporter.Export(new[] { game });

        Assert.Equal(0xEF, bytes[0]);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"A,B\"", text);
        Assert.Contains("\"\"hi\"\"", text);
    }

    [Fact]
    public void Backup_CreatesAndListsFile()
    {
        _ws.Games.Insert(TempWorkspace.NewGame(Path.Combine(_ws.Root, "b.nes")));
        var service = new BackupService(_ws.Db);

        var backup = service.Create("test");
        Assert.True(File.Exists(backup.FullPath));
        Assert.Contains(service.List(), file => file.Name == backup.Name);
    }

    [Fact]
    public void Backup_RejectsNonSqliteFile()
    {
        var service = new BackupService(_ws.Db);
        using var stream = new MemoryStream("not a database"u8.ToArray());

        var (ok, _) = service.Restore(stream, "bad.db");
        Assert.False(ok);
    }
}
