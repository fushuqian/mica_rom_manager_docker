using System.IO;
using MicaRomManager.Core.Services;
using Xunit;

namespace MicaRomManager.Core.Tests;

public sealed class RomHasherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-hash-" + Guid.NewGuid().ToString("N"));

    public RomHasherTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void SameContent_ProducesSameHash()
    {
        var a = Path.Combine(_dir, "a.nes");
        var b = Path.Combine(_dir, "b.nes");
        File.WriteAllBytes(a, new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(b, new byte[] { 1, 2, 3, 4 });

        Assert.Equal(RomHasher.Compute(a), RomHasher.Compute(b));
        Assert.False(RomHasher.IsSampled(RomHasher.Compute(a)));
    }

    [Fact]
    public void DifferentContent_ProducesDifferentHash()
    {
        var a = Path.Combine(_dir, "a.nes");
        var b = Path.Combine(_dir, "b.nes");
        File.WriteAllBytes(a, new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(b, new byte[] { 1, 2, 3, 5 });

        Assert.NotEqual(RomHasher.Compute(a), RomHasher.Compute(b));
    }

    [Fact]
    public void LargeFile_UsesSampledHash_AndIsStable()
    {
        var path = Path.Combine(_dir, "big.iso");
        using (var stream = new FileStream(path, FileMode.CreateNew))
        {
            stream.SetLength(RomHasher.FullHashLimit + 32L * 1024 * 1024);
            stream.Position = 0;
            stream.Write(new byte[] { 7, 7, 7, 7 });
            stream.Position = stream.Length / 2;
            stream.Write(new byte[] { 8, 8, 8, 8 });
            stream.Position = stream.Length - 4;
            stream.Write(new byte[] { 9, 9, 9, 9 });
        }

        var first = RomHasher.Compute(path);
        var second = RomHasher.Compute(path);
        Assert.True(RomHasher.IsSampled(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public void MissingFile_ReturnsEmpty()
    {
        Assert.Equal("", RomHasher.Compute(Path.Combine(_dir, "nope.nes")));
    }
}
