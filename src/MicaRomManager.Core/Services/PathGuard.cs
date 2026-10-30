using System.IO;

namespace MicaRomManager.Core.Services;

/// <summary>路径安全：所有文件访问必须落在已登记库根或数据目录内。</summary>
public sealed class PathGuard
{
    private readonly Data.LibraryRepository _libraries;
    private readonly string _dataDir;

    public PathGuard(Data.LibraryRepository libraries, Data.Db db)
    {
        _libraries = libraries;
        _dataDir = db.DataDir;
    }

    public IReadOnlyList<string> Roots => _libraries.ListAll().Select(library => library.RootPath).ToList();

    public bool IsInsideRoots(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var full = SafeFullPath(path);
        if (full.Length == 0) return false;

        return Roots.Any(root => IsUnder(full, root));
    }

    public bool IsInsideData(string path)
    {
        var full = SafeFullPath(path);
        return full.Length > 0 && IsUnder(full, _dataDir);
    }

    /// <summary>校验并返回绝对路径；越界时返回 null。</summary>
    public string? ResolveInside(string path, bool allowData = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var full = SafeFullPath(path);
        if (full.Length == 0) return null;
        if (IsInsideRoots(full)) return full;
        if (allowData && IsInsideData(full)) return full;
        return null;
    }

    /// <summary>把用户输入的相对路径解析到库根内，拒绝 .. 逃逸。</summary>
    public string? ResolveInLibrary(long libraryId, string relative)
    {
        var library = _libraries.Get(libraryId);
        if (library is null) return null;

        var combined = Path.GetFullPath(Path.Combine(library.RootPath, relative ?? ""));
        return IsUnder(combined, library.RootPath) ? combined : null;
    }

    public static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;

        var full = SafeFullPath(path);
        var normalizedRoot = SafeFullPath(root);
        if (full.Length == 0 || normalizedRoot.Length == 0) return false;

        var prefix = Path.TrimEndingDirectorySeparator(normalizedRoot) + Path.DirectorySeparatorChar;
        return full.Equals(Path.TrimEndingDirectorySeparator(normalizedRoot), StringComparison.Ordinal)
               || full.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return "";
        }
    }
}
