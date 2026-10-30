using System.IO;
using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class OrganizerServiceTests : IDisposable
{
    private readonly TempWorkspace _ws = new();
    private readonly string _root;

    public OrganizerServiceTests() => _root = _ws.CreateLibrary("lib");

    public void Dispose() => _ws.Dispose();

    private OrganizerService Organizer() => new(_ws.Games, _ws.Guard);

    [Fact]
    public void BuildFileName_IncludesOnlyPresentFields()
    {
        var game = new Game { Title = "新鬼武者", Region = "欧版", Language = "英语", SpecialNote = "汉化版" };
        var name = OrganizerService.BuildFileName(game, 0, "/x/a.chd");
        Assert.Equal("新鬼武者 (欧版)(英语)(汉化版).chd", name);
    }

    [Fact]
    public void BuildFileName_AddsDiscNumberOnlyForMultiDisc()
    {
        var single = new Game { Title = "A", RomPath = "/x/a.chd" };
        Assert.Equal("A.chd", OrganizerService.BuildFileName(single, 0, "/x/a.chd"));

        var multi = new Game { Title = "B", RomPath = "/x/b1.chd", DiscPaths = "/x/b2.chd" };
        Assert.Equal("B Disc-2.chd", OrganizerService.BuildFileName(multi, 1, "/x/b2.chd"));
    }

    [Fact]
    public void PlanAndApply_RenamesFileAndUpdatesDatabase()
    {
        var source = Path.Combine(_root, "old_name.nes");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3 });

        var game = new Game
        {
            Title = "超级马里奥",
            Platform = "NES",
            Region = "日版",
            RomPath = source,
            Status = GameStatus.NotPlayed,
        };
        var id = _ws.Games.Insert(game);
        var stored = _ws.Games.Get(id)!;

        var plan = Organizer().Plan(new[] { stored });
        Assert.Single(plan.Items);
        Assert.Equal("超级马里奥 (日版).nes", Path.GetFileName(plan.Items[0].NewPath));

        var result = Organizer().Apply(plan);
        Assert.Equal(1, result.Renamed);

        var loaded = _ws.Games.Get(id)!;
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(loaded.RomPath));
        Assert.EndsWith("超级马里奥 (日版).nes", loaded.RomPath);
    }

    [Fact]
    public void Plan_SkipsFilesOutsideLibrary()
    {
        var outside = Path.Combine(_ws.Root, "outside.nes");
        File.WriteAllBytes(outside, new byte[] { 1 });
        var game = new Game { Title = "X", Platform = "NES", RomPath = outside };
        var id = _ws.Games.Insert(game);

        var plan = Organizer().Plan(new[] { _ws.Games.Get(id)! });
        Assert.Empty(plan.Items);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void Plan_AvoidsTargetCollision()
    {
        var first = Path.Combine(_root, "a.nes");
        var second = Path.Combine(_root, "b.nes");
        File.WriteAllBytes(first, new byte[] { 1 });
        File.WriteAllBytes(second, new byte[] { 2 });
        File.WriteAllBytes(Path.Combine(_root, "同名 (2).nes"), new byte[] { 3 });

        var one = _ws.Games.Insert(new Game { Title = "同名", RomPath = first });
        var two = _ws.Games.Insert(new Game { Title = "同名", RomPath = second });

        var plan = Organizer().Plan(new[] { _ws.Games.Get(one)!, _ws.Games.Get(two)! });
        Assert.Equal(2, plan.Items.Count);
        Assert.NotEqual(plan.Items[0].NewPath, plan.Items[1].NewPath);
    }
}
