using System.Text;
using Microsoft.AspNetCore.DataProtection;
using MicaRomManager.Core.Ai;

namespace MicaRomManager.Web;

/// <summary>用 ASP.NET Data Protection 加密设置中的 AI Key；密钥环持久化在 data/keys。</summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("MicaRomManager.AiKey.v1");

    public string Protect(string plaintext) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(_protector.Protect(plaintext)));

    public string Unprotect(string ciphertext) =>
        _protector.Unprotect(Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext)));
}
