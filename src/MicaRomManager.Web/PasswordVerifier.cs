using System.Security.Cryptography;
using System.Text;

namespace MicaRomManager.Web;

/// <summary>单密码校验：只在内存中保存 SHA-256，比较使用固定时间算法。</summary>
public sealed class PasswordVerifier
{
    private readonly byte[] _hash;

    public PasswordVerifier(string password)
    {
        _hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
    }

    public bool Verify(string? candidate)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate ?? ""));
        return CryptographicOperations.FixedTimeEquals(_hash, hash);
    }
}
