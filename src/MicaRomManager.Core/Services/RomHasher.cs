using System.IO;
using System.Security.Cryptography;

namespace MicaRomManager.Core.Services;

/// <summary>
/// ROM 内容指纹：≤128MB 全量 SHA-256；更大文件用「大小 + 首/中/尾各 1MB」采样，带 fast: 前缀。
/// </summary>
public static class RomHasher
{
    public const long FullHashLimit = 128L * 1024 * 1024;
    private const int SampleSize = 1024 * 1024;

    public static string Compute(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "";

            if (info.Length <= FullHashLimit) return HashFull(path);
            return "fast:" + HashSampled(path, info.Length);
        }
        catch
        {
            return "";
        }
    }

    public static bool IsSampled(string hash) => hash.StartsWith("fast:", StringComparison.Ordinal);

    private static string HashFull(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string HashSampled(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
        using var sha = SHA256.Create();

        var buffer = new byte[SampleSize];
        var sizeBytes = BitConverter.GetBytes(length);
        sha.TransformBlock(sizeBytes, 0, sizeBytes.Length, null, 0);

        foreach (var offset in new[] { 0L, Math.Max(0, length / 2 - SampleSize / 2), Math.Max(0, length - SampleSize) })
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read > 0) sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
}
