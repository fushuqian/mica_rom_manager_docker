using System.IO;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;

namespace MicaRomManager.Web.Tests;

/// <summary>每个测试类一个独立数据目录；环境变量在宿主构建前设置。</summary>
public sealed class MicaWebFactory : WebApplicationFactory<Program>
{
    public const string Password = "test-password-123";

    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "mica-web-" + Guid.NewGuid().ToString("N"));

    private Task<HttpClient>? _authClient;

    /// <summary>复用一个已登录客户端，避免触发登录限流。</summary>
    public Task<HttpClient> GetAuthClientAsync() => _authClient ??= WebTestHelpers.LoginAsync(this);

    public MicaWebFactory()
    {
        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable("MICA_PASSWORD", Password);
        Environment.SetEnvironmentVariable("MICA_DATA_DIR", DataDir);
        Environment.SetEnvironmentVariable("MICA_LIBRARY_ROOTS", "");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        try
        {
            Directory.Delete(DataDir, true);
        }
        catch
        {
            // 清理失败不影响测试
        }
    }
}

public static class WebTestHelpers
{
    public static readonly Regex TokenPattern =
        new("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    public static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var loginPage = await client.GetAsync("/login");
        loginPage.EnsureSuccessStatusCode();
        var html = await loginPage.Content.ReadAsStringAsync();
        var match = TokenPattern.Match(html);
        if (!match.Success) throw new InvalidOperationException("登录页缺少 antiforgery token");

        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = MicaWebFactory.Password,
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = match.Groups[1].Value,
        }));

        if (response.StatusCode is not (System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.Found))
            throw new InvalidOperationException($"登录失败：{(int)response.StatusCode}");

        client.DefaultRequestHeaders.Remove("__RequestVerificationToken");
        return client;
    }
}
