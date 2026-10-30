using System.IO;
using MicaRomManager.Core.Services;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class PathGuardTests : IDisposable
{
    private readonly TempWorkspace _ws = new();
    private readonly string _root;

    public PathGuardTests() => _root = _ws.CreateLibrary("lib");

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void InsideRoot_IsAllowed()
    {
        Assert.True(_ws.Guard.IsInsideRoots(Path.Combine(_root, "sub", "a.nes")));
    }

    [Fact]
    public void OutsideRoot_IsRejected()
    {
        Assert.False(_ws.Guard.IsInsideRoots(Path.Combine(_ws.Root, "other", "a.nes")));
    }

    [Fact]
    public void SiblingPrefix_IsNotConfusedWithRoot()
    {
        var sibling = _root + "-evil";
        Assert.False(_ws.Guard.IsInsideRoots(Path.Combine(sibling, "a.nes")));
    }

    [Fact]
    public void DataDirectory_IsAllowedOnlyWhenRequested()
    {
        var cover = Path.Combine(_ws.DataDir, "covers", "x.jpg");
        Assert.False(_ws.Guard.IsInsideRoots(cover));
        Assert.True(_ws.Guard.IsInsideData(cover));
        Assert.Equal(Path.GetFullPath(cover), _ws.Guard.ResolveInside(cover));
    }

    [Fact]
    public void ResolveInLibrary_RejectsTraversal()
    {
        Assert.Null(_ws.Guard.ResolveInLibrary(1, "../etc/passwd"));
        Assert.NotNull(_ws.Guard.ResolveInLibrary(1, "sub/a.nes"));
    }
}
