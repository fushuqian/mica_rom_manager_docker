namespace MicaRomManager.Core.Models;

public enum ScanStateKind
{
    Idle,
    Running,
    Completed,
    Canceled,
    Failed,
}

/// <summary>扫描进度快照，供 UI 轮询/订阅。</summary>
public sealed record ScanProgress
{
    public ScanStateKind Kind { get; init; } = ScanStateKind.Idle;
    public string Message { get; init; } = "";
    public int Total { get; init; }
    public int Done { get; init; }
    public int Added { get; init; }
    public int Duplicate { get; init; }
    public int Skipped { get; init; }
    public int Errors { get; init; }
    public int MissingMarked { get; init; }
    public string Current { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }

    public double Percent => Total <= 0 ? 0 : Math.Min(100, Done * 100.0 / Total);

    public bool IsRunning => Kind == ScanStateKind.Running;
}

public sealed record ScanOptions
{
    public long LibraryId { get; init; }
    public bool Recursive { get; init; } = true;
    /// <summary>可选扩展名（不带点）；为空时使用平台格式并集。</summary>
    public IReadOnlyList<string>? Extensions { get; init; }
    /// <summary>只扫描库根下的某个子目录。</summary>
    public string? SubFolder { get; init; }
    public bool MarkMissing { get; init; } = true;
}

public sealed record ScanOutcome(int Added, int Duplicate, int Skipped, int Errors, int MissingMarked, TimeSpan Elapsed);
