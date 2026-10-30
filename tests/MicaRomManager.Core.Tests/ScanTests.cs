using System.Diagnostics;
using System.IO;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class ScanTests : IDisposable
{
    private readonly TempWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private ScanCoordinator CreateCoordinator() =>
        new(_ws.Db, _ws.Libraries, _ws.Platforms, _ws.Games, _ws.Settings);

    private static void WriteRom(string path, byte seed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[512];
        data[0] = seed;
        File.WriteAllBytes(path, data);
    }

    [Fact]
    public async Task Scan_RegistersGames_DetectsPlatforms_AndDedupes()
    {
        var root = _ws.CreateLibrary("测试库");
        WriteRom(Path.Combine(root, "nes", "Contra.nes"), 1);
        WriteRom(Path.Combine(root, "nes", "Mario.nes"), 2);
        WriteRom(Path.Combine(root, "snes", "Metroid.sfc"), 3);
        WriteRom(Path.Combine(root, "nes", "readme.txt"), 4);

        var coordinator = CreateCoordinator();
        var start = coordinator.Start(new ScanOptions { LibraryId = 1 });
        Assert.True(start.Ok, start.Message);

        await WaitUntilAsync(() => !coordinator.Progress.IsRunning, TimeSpan.FromSeconds(30));

        Assert.Equal(ScanStateKind.Completed, coordinator.Progress.Kind);
        var games = _ws.Games.ListAll();
        Assert.Equal(3, games.Count);
        Assert.Equal("NES", games.Single(game => game.Title == "Contra").Platform);
        Assert.Equal("SNES", games.Single(game => game.Title == "Metroid").Platform);
        Assert.All(games, game => Assert.False(string.IsNullOrWhiteSpace(game.RomHash)));

        var second = CreateCoordinator();
        second.Start(new ScanOptions { LibraryId = 1 });
        await WaitUntilAsync(() => !second.Progress.IsRunning, TimeSpan.FromSeconds(30));
        Assert.Equal(0, second.Progress.Added);
        Assert.Equal(3, _ws.Games.CountAll());
    }

    [Fact]
    public async Task Scan_MarksMissingFiles()
    {
        var root = _ws.CreateLibrary("missing");
        var target = Path.Combine(root, "a.nes");
        WriteRom(target, 7);

        var coordinator = CreateCoordinator();
        coordinator.Start(new ScanOptions { LibraryId = 1 });
        await WaitUntilAsync(() => !coordinator.Progress.IsRunning, TimeSpan.FromSeconds(30));
        Assert.Single(_ws.Games.ListAll());

        File.Delete(target);

        var second = CreateCoordinator();
        second.Start(new ScanOptions { LibraryId = 1 });
        await WaitUntilAsync(() => !second.Progress.IsRunning, TimeSpan.FromSeconds(30));

        Assert.Equal(1, second.Progress.MissingMarked);
        Assert.True(_ws.Games.ListAll().Single().Missing);
    }

    [Fact]
    public async Task Rescan_DoesNotMarkExistingFilesAsMissing()
    {
        var root = _ws.CreateLibrary("rescan");
        WriteRom(Path.Combine(root, "a.nes"), 9);

        var first = CreateCoordinator();
        first.Start(new ScanOptions { LibraryId = 1 });
        await WaitUntilAsync(() => !first.Progress.IsRunning, TimeSpan.FromSeconds(30));

        var second = CreateCoordinator();
        second.Start(new ScanOptions { LibraryId = 1 });
        await WaitUntilAsync(() => !second.Progress.IsRunning, TimeSpan.FromSeconds(30));

        Assert.Equal(0, second.Progress.MissingMarked);
        Assert.False(_ws.Games.ListAll().Single().Missing);
    }

    [Fact]
    public async Task Scan_RejectsSubFolderOutsideRoot()
    {
        _ws.CreateLibrary("lib");
        var coordinator = CreateCoordinator();
        var result = coordinator.Start(new ScanOptions { LibraryId = 1, SubFolder = "../etc" });
        Assert.False(result.Ok);
        await Task.CompletedTask;
    }

    [Fact]
    public void Scan_RejectsMissingLibrary()
    {
        var coordinator = CreateCoordinator();
        var result = coordinator.Start(new ScanOptions { LibraryId = 99 });
        Assert.False(result.Ok);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout) throw new TimeoutException("等待扫描完成超时");
            await Task.Delay(50);
        }
    }
}
