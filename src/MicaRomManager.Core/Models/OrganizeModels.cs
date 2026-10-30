namespace MicaRomManager.Core.Models;

public sealed record RenamePlanItem(long GameId, int DiscIndex, string OldPath, string NewPath);

public sealed record RenamePlan(IReadOnlyList<RenamePlanItem> Items, IReadOnlyList<string> Warnings, IReadOnlyList<string> Skipped)
{
    public int Count => Items.Count;
}

public sealed record OrganizeResult(int Renamed, int Failed, IReadOnlyList<string> Messages);
