using System.IO;

namespace MicaRomManager.Core.Services;

/// <summary>封面文件存储：统一放到数据目录 covers/ 下，数据库只存文件名。</summary>
public sealed class CoverStore
{
    public static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    public const long MaxBytes = 10L * 1024 * 1024;

    public string CoversDir { get; }

    public CoverStore(string dataDir)
    {
        CoversDir = Path.Combine(dataDir, "covers");
        Directory.CreateDirectory(CoversDir);
    }

    public string FullPath(string fileName) => Path.Combine(CoversDir, fileName);

    public bool Exists(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && File.Exists(FullPath(fileName));

    /// <summary>导入上传的封面；返回存储文件名。</summary>
    public string Import(Stream source, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension)) extension = ".png";

        var fileName = Guid.NewGuid().ToString("N") + extension;
        var target = FullPath(fileName);

        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(output);
        }

        return fileName;
    }

    public void Delete(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;
        var path = FullPath(fileName);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 删除失败不影响主流程
        }
    }

    /// <summary>清理没有被任何游戏引用的封面文件。</summary>
    public int CleanupOrphans(IEnumerable<string> referenced)
    {
        var keep = new HashSet<string>(referenced.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(CoversDir))
        {
            if (keep.Contains(Path.GetFileName(file))) continue;
            try
            {
                File.Delete(file);
                removed++;
            }
            catch
            {
                // 忽略单个文件失败
            }
        }

        return removed;
    }
}
