namespace MicaRomManager.Core.Ai;

/// <summary>设置中的敏感值加解密；Web 层用 ASP.NET Data Protection 实现。</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

/// <summary>测试/无密钥环场景的直通实现。</summary>
public sealed class PassthroughSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
}
