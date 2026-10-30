using System.IO;
using MicaRomManager.Core.Models;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class GameRepositoryTests : IDisposable
{
    private readonly TempWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void InsertAndGet_RoundTripsAllFields()
    {
        var game = new Game
        {
            Title = "银河战士",
            Platform = "SNES",
            Genre = "动作",
            Region = "日版",
            Language = "日语",
            SpecialNote = "汉化版",
            ReleaseYear = 1994,
            Publisher = "Nintendo",
            Developer = "Nintendo R&D1",
            Favorite = true,
            Rating = 5,
            Tags = "经典,动作",
            Notes = "简介",
            RomPath = Path.Combine(_ws.Root, "metroid.sfc"),
            RomHash = "abc",
            FileSize = 1024,
        };

        var id = _ws.Games.Insert(game);
        var loaded = _ws.Games.Get(id);

        Assert.NotNull(loaded);
        Assert.Equal("银河战士", loaded!.Title);
        Assert.Equal(1994, loaded.ReleaseYear);
        Assert.Equal(5, loaded.Rating);
        Assert.True(loaded.Favorite);
        Assert.Equal("经典,动作", loaded.Tags);
        Assert.Equal("汉化版", loaded.SpecialNote);
    }

    [Fact]
    public void DuplicatePath_IsRejected()
    {
        var path = Path.Combine(_ws.Root, "dup.nes");
        _ws.Games.Insert(TempWorkspace.NewGame(path, "A"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => _ws.Games.Insert(TempWorkspace.NewGame(path, "B")));
    }

    [Fact]
    public void Patch_OnlyUpdatesGivenFields()
    {
        var game = TempWorkspace.NewGame(Path.Combine(_ws.Root, "p.nes"), "原始");
        game.Genre = "射击";
        var id = _ws.Games.Insert(game);

        _ws.Games.UpdateFields(new[] { id }, new GamePatch { Rating = 4, Status = GameStatus.Playing });

        var loaded = _ws.Games.Get(id)!;
        Assert.Equal(4, loaded.Rating);
        Assert.Equal(GameStatus.Playing, loaded.Status);
        Assert.Equal("原始", loaded.Title);
        Assert.Equal("射击", loaded.Genre);
    }

    [Fact]
    public void Rating_IsClamped()
    {
        var id = _ws.Games.Insert(TempWorkspace.NewGame(Path.Combine(_ws.Root, "r.nes")));
        _ws.Games.UpdateFields(new[] { id }, new GamePatch { Rating = 99 });
        Assert.Equal(5, _ws.Games.Get(id)!.Rating);
    }

    [Fact]
    public void Filter_ByKeywordTagFavoriteMissing()
    {
        var a = TempWorkspace.NewGame(Path.Combine(_ws.Root, "a.nes"), "Contra");
        a.Tags = "经典,动作";
        a.Favorite = true;
        var b = TempWorkspace.NewGame(Path.Combine(_ws.Root, "b.nes"), "Mario");
        b.Missing = true;

        _ws.Games.Insert(a);
        _ws.Games.Insert(b);

        Assert.Single(_ws.Games.Query(new GameFilter { Keyword = "Contra" }, new GameSort(), 1, 50).Items);
        Assert.Single(_ws.Games.Query(new GameFilter { Tag = "经典" }, new GameSort(), 1, 50).Items);
        Assert.Single(_ws.Games.Query(new GameFilter { OnlyFavorites = true }, new GameSort(), 1, 50).Items);
        Assert.Single(_ws.Games.Query(new GameFilter { OnlyMissing = true }, new GameSort(), 1, 50).Items);
    }

    [Fact]
    public void Query_PaginatesAndSorts()
    {
        for (var index = 1; index <= 7; index++)
            _ws.Games.Insert(TempWorkspace.NewGame(Path.Combine(_ws.Root, $"g{index}.nes"), $"Game {index}"));

        var page = _ws.Games.Query(new GameFilter(), new GameSort("title", true), 2, 3);
        Assert.Equal(7, page.Total);
        Assert.Equal(3, page.Items.Count);
        Assert.Equal("Game 4", page.Items[0].Title);
    }

    [Fact]
    public void DeleteMany_RemovesRows()
    {
        var ids = new List<long>();
        for (var index = 0; index < 3; index++)
            ids.Add(_ws.Games.Insert(TempWorkspace.NewGame(Path.Combine(_ws.Root, $"d{index}.nes"))));

        Assert.Equal(2, _ws.Games.DeleteMany(ids.Take(2).ToList()));
        Assert.Single(_ws.Games.ListAll());
    }

    [Fact]
    public void DistinctTags_SplitBothCommaStyles()
    {
        var game = TempWorkspace.NewGame(Path.Combine(_ws.Root, "t.nes"));
        game.Tags = "经典，动作,汉化";
        _ws.Games.Insert(game);

        var tags = _ws.Games.DistinctTags();
        Assert.Contains("经典", tags);
        Assert.Contains("动作", tags);
        Assert.Contains("汉化", tags);
    }
}
