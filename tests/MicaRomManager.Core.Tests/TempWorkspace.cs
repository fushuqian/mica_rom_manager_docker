using System.IO;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;

namespace MicaRomManager.Core.Tests;

internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }
    public string DataDir { get; }
    public Db Db { get; }
    public SettingsRepository Settings { get; }
    public GameRepository Games { get; }
    public PlatformRepository Platforms { get; }
    public LookupRepository Lookups { get; }
    public LibraryRepository Libraries { get; }
    public PathGuard Guard { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "mica-tests-" + Guid.NewGuid().ToString("N"));
        DataDir = Path.Combine(Root, "data");
        Directory.CreateDirectory(DataDir);
        Db = new Db(DataDir);
        Settings = new SettingsRepository(Db);
        Games = new GameRepository(Db);
        Platforms = new PlatformRepository(Db, Games);
        Lookups = new LookupRepository(Db, Platforms);
        Libraries = new LibraryRepository(Db);
        Guard = new PathGuard(Libraries, Db);
    }

    public string CreateLibrary(string name, string relative = "roms")
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(path);
        var (ok, message) = Libraries.Add(name, path);
        if (!ok) throw new InvalidOperationException(message);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Db.Dispose();
            Directory.Delete(Root, true);
        }
        catch
        {
            // 清理失败不影响测试
        }
    }

    public static Game NewGame(string path, string title = "Game") => new()
    {
        Title = title,
        Platform = "NES",
        RomPath = path,
        Status = GameStatus.NotPlayed,
    };
}
