using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;

namespace MicaRomManager.Core.Services;

/// <summary>单例扫描协调器：同一时刻只允许一个扫描任务，进度供 UI 轮询。</summary>
public sealed class ScanCoordinator
{
    public const int MaxFiles = 200_000;
    public const int BatchSize = 200;
    private const int HashConcurrency = 2;

    private static readonly string[] SkippedDirectories =
    {
        ".trash", "$RECYCLE.BIN", "#recycle", "@eaDir", ".git", ".mica",
        "System Volume Information", "lost+found",
    };

    private readonly Db _db;
    private readonly LibraryRepository _libraries;
    private readonly PlatformRepository _platforms;
    private readonly GameRepository _games;
    private readonly SettingsRepository _settings;
    private readonly object _gate = new();

    private ScanProgress _progress = new();
    private CancellationTokenSource? _cancellation;
    private Task? _running;

    public ScanCoordinator(Db db, LibraryRepository libraries, PlatformRepository platforms,
        GameRepository games, SettingsRepository settings)
    {
        _db = db;
        _libraries = libraries;
        _platforms = platforms;
        _games = games;
        _settings = settings;
    }

    public ScanProgress Progress
    {
        get
        {
            lock (_gate) return _progress;
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _progress.IsRunning;
        }
    }

    public (bool Ok, string Message) Start(ScanOptions options)
    {
        if (IsRunning) return (false, "已有扫描任务在进行，请先等待或取消。");

        var library = _libraries.Get(options.LibraryId);
        if (library is null) return (false, "库不存在。");
        if (!Directory.Exists(library.RootPath)) return (false, $"库目录不存在：{library.RootPath}");

        var root = library.RootPath;
        if (!string.IsNullOrWhiteSpace(options.SubFolder))
        {
            var sub = Path.GetFullPath(Path.Combine(root, options.SubFolder));
            var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (!sub.StartsWith(prefix, StringComparison.Ordinal) && !string.Equals(
                    Path.TrimEndingDirectorySeparator(sub), Path.TrimEndingDirectorySeparator(root), StringComparison.Ordinal))
                return (false, "子目录必须位于库根之内。");
            if (!Directory.Exists(sub)) return (false, $"子目录不存在：{options.SubFolder}");
            root = sub;
        }

        lock (_gate)
        {
            _cancellation = new CancellationTokenSource();
            _progress = new ScanProgress
            {
                Kind = ScanStateKind.Running,
                Message = "正在枚举文件…",
                StartedAt = DateTime.Now,
            };
            _running = Task.Run(() => RunAsync(library, root, options, _cancellation.Token));
        }

        return (true, "扫描已开始。");
    }

    public void Cancel()
    {
        lock (_gate) _cancellation?.Cancel();
    }

    private async Task RunAsync(Library library, string scanRoot, ScanOptions options, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = 0;
        var duplicate = 0;
        var skipped = 0;
        var added = 0;
        var missingMarked = 0;

        try
        {
            var accepted = BuildExtensionSet(options.Extensions);
            var files = EnumerateFiles(scanRoot, options.Recursive, accepted, token).ToList();

            Update(progress => progress with
            {
                Total = files.Count,
                Message = files.Count == 0 ? "没有找到可识别的 ROM 文件" : "正在计算指纹并入库…",
            });

            var existingPaths = _games.AllPaths();
            var existingHashes = _games.AllHashes();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenHashes = new HashSet<string>(existingHashes, StringComparer.OrdinalIgnoreCase);
            var seenPaths = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);

            var platforms = _platforms.ListAll();
            var platformFormats = platforms
                .Where(platform => !string.IsNullOrWhiteSpace(platform.Formats))
                .ToDictionary(platform => platform.Name,
                    platform => (IReadOnlyCollection<string>)PlatformFormats.Parse(platform.Formats),
                    StringComparer.OrdinalIgnoreCase);

            var processed = 0;
            foreach (var batch in Batch(files, BatchSize))
            {
                token.ThrowIfCancellationRequested();

                var candidates = new ConcurrentBag<Game>();
                await Parallel.ForEachAsync(batch,
                    new ParallelOptions { MaxDegreeOfParallelism = HashConcurrency, CancellationToken = token },
                    (file, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var full = Path.GetFullPath(file);
                            lock (_gate)
                            {
                                if (seenPaths.Contains(full))
                                {
                                    // 已登记的文件也要计入 found，否则重扫会把它们全部标成缺失
                                    found.Add(full);
                                    skipped++;
                                    return ValueTask.CompletedTask;
                                }
                            }

                            var (platform, _) = ResolvePlatform(library.RootPath, full, platforms, platformFormats);
                            var info = ExtensionMap.Detect(full);
                            if (platform.Length == 0) platform = info.Platform ?? ExtensionMap.UnspecifiedPlatform;

                            var hash = RomHasher.Compute(full);
                            if (hash.Length > 0)
                            {
                                lock (_gate)
                                {
                                    if (seenHashes.Contains(hash))
                                    {
                                        duplicate++;
                                        return ValueTask.CompletedTask;
                                    }

                                    seenHashes.Add(hash);
                                }
                            }

                            var game = new Game
                            {
                                LibraryId = library.Id,
                                Title = Path.GetFileNameWithoutExtension(full),
                                Platform = platform,
                                RomPath = full,
                                FileSize = SafeLength(full),
                                RomHash = hash,
                                Status = GameStatus.NotPlayed,
                                CreatedAt = DateTime.Now,
                                UpdatedAt = DateTime.Now,
                            };

                            candidates.Add(game);
                            lock (_gate)
                            {
                                seenPaths.Add(full);
                                found.Add(full);
                            }
                        }
                        catch (Exception)
                        {
                            Interlocked.Increment(ref errors);
                        }

                        return ValueTask.CompletedTask;
                    });

                var toInsert = candidates.OrderBy(game => game.RomPath, StringComparer.Ordinal).ToList();
                if (toInsert.Count > 0) added += _games.InsertMany(toInsert);

                processed += batch.Count;
                Update(progress => progress with
                {
                    Done = processed,
                    Added = added,
                    Duplicate = duplicate,
                    Skipped = skipped,
                    Errors = errors,
                    Current = toInsert.Count > 0 ? Path.GetFileName(toInsert[^1].RomPath) : progress.Current,
                });
            }

            if (options.MarkMissing) missingMarked = MarkMissing(library.Id, found);

            stopwatch.Stop();
            Update(_ => new ScanProgress
            {
                Kind = ScanStateKind.Completed,
                Message = $"扫描完成：新增 {added}，重复 {duplicate}，错误 {errors}，标记失效 {missingMarked}",
                Total = files.Count,
                Done = files.Count,
                Added = added,
                Duplicate = duplicate,
                Skipped = skipped,
                Errors = errors,
                MissingMarked = missingMarked,
                StartedAt = DateTime.Now - stopwatch.Elapsed,
                FinishedAt = DateTime.Now,
            });

            _settings.Set("scan.last_library_id", library.Id.ToString());
            _settings.Set("scan.last_at", DateTime.Now.ToString("O"));
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            Update(progress => progress with
            {
                Kind = ScanStateKind.Canceled,
                Message = $"已取消（已入库 {added} 条）",
                Added = added,
                Duplicate = duplicate,
                Errors = errors,
                FinishedAt = DateTime.Now,
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Update(progress => progress with
            {
                Kind = ScanStateKind.Failed,
                Message = $"扫描失败：{ex.Message}",
                Added = added,
                Duplicate = duplicate,
                Errors = errors + 1,
                FinishedAt = DateTime.Now,
            });
        }
    }

    private int MarkMissing(long libraryId, HashSet<string> found)
    {
        var rows = _games.PathsByLibrary(libraryId);
        var missingIds = new List<long>();
        var presentIds = new List<long>();

        foreach (var (id, path, missing) in rows)
        {
            var exists = found.Contains(path);
            if (!exists && !missing) missingIds.Add(id);
            else if (exists && missing) presentIds.Add(id);
        }

        if (missingIds.Count > 0) _games.SetMissing(missingIds, true);
        if (presentIds.Count > 0) _games.SetMissing(presentIds, false);
        return missingIds.Count;
    }

    private HashSet<string> BuildExtensionSet(IReadOnlyList<string>? explicitExtensions)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (explicitExtensions is { Count: > 0 })
        {
            foreach (var extension in explicitExtensions)
                set.Add(extension.StartsWith('.') ? extension : "." + extension);
            return set;
        }

        foreach (var platform in _platforms.ListAll())
        {
            var formats = string.IsNullOrWhiteSpace(platform.Formats)
                ? PlatformFormats.GetDefault(platform.Name)
                : platform.Formats;
            foreach (var extension in PlatformFormats.Parse(formats)) set.Add(extension);
        }

        if (set.Count == 0)
            foreach (var extension in ExtensionMap.Extensions) set.Add(extension);

        foreach (var archive in new[] { ".zip", ".7z", ".rar" }) set.Add(archive);
        return set;
    }

    private static (string Platform, bool NeedsSpecify) ResolvePlatform(
        string libraryRoot,
        string filePath,
        IReadOnlyList<Platform> platforms,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> platformFormats)
    {
        var directory = Path.GetDirectoryName(filePath) ?? "";
        var relative = Path.GetRelativePath(libraryRoot, directory);
        var top = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(top))
        {
            var folderMatch = platforms.FirstOrDefault(platform =>
                string.Equals(platform.Folder, top, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(platform.Name, top, StringComparison.OrdinalIgnoreCase));
            if (folderMatch is not null) return (folderMatch.Name, false);
        }

        return PlatformFormats.Resolve(filePath, ExtensionMap.Detect(filePath), platformFormats);
    }

    private static IEnumerable<string> EnumerateFiles(
        string root, bool recursive, HashSet<string> accepted, CancellationToken token)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        var count = 0;

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = stack.Pop();

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(directory);
            }
            catch
            {
                continue;
            }

            foreach (var child in children)
            {
                token.ThrowIfCancellationRequested();

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(child);
                }
                catch
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!recursive) continue;
                    var name = Path.GetFileName(child);
                    if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith('.')) continue;
                    stack.Push(child);
                    continue;
                }

                if ((attributes & FileAttributes.Hidden) != 0) continue;

                var extension = Path.GetExtension(child);
                if (!accepted.Contains(extension)) continue;

                count++;
                if (count > MaxFiles) yield break;
                yield return child;
            }
        }
    }

    private static IEnumerable<List<string>> Batch(IReadOnlyList<string> source, int size)
    {
        for (var index = 0; index < source.Count; index += size)
        {
            var count = Math.Min(size, source.Count - index);
            yield return source.Skip(index).Take(count).ToList();
        }
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private void Update(Func<ScanProgress, ScanProgress> mutate)
    {
        lock (_gate) _progress = mutate(_progress);
    }
}
