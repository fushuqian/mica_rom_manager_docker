using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;
using Xunit;

namespace MicaRomManager.Web.Tests;

public sealed class WebTests : IClassFixture<MicaWebFactory>
{
    private readonly MicaWebFactory _factory;

    public WebTests(MicaWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_IsAnonymous()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_IsAnonymous()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Root_RedirectsToLogin_WhenAnonymous()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsError()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var page = await client.GetStringAsync("/login");
        var token = WebTestHelpers.TokenPattern.Match(page).Groups[1].Value;

        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = "wrong-password",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=1", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_WithoutAntiforgery_IsRejected()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["password"] = MicaWebFactory.Password,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("expired", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_ThenAccessPages()
    {
        var client = await _factory.GetAuthClientAsync();

        var root = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Contains("游戏库", await root.Content.ReadAsStringAsync());

        foreach (var path in new[] { "/settings", "/import", "/scrape" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Api_RequiresAuthentication()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/scan/state");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Scan_EndToEnd_RegistersAndLists()
    {
        var root = Path.Combine(Path.GetTempPath(), "mica-web-roms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nes"));
        Directory.CreateDirectory(Path.Combine(root, "snes"));
        await File.WriteAllBytesAsync(Path.Combine(root, "nes", "Contra.nes"), new byte[] { 1, 2, 3 });
        await File.WriteAllBytesAsync(Path.Combine(root, "snes", "Metroid.sfc"), new byte[] { 4, 5, 6 });

        try
        {
            var libraries = _factory.Services.GetRequiredService<LibraryRepository>();
            var games = _factory.Services.GetRequiredService<GameRepository>();
            var scan = _factory.Services.GetRequiredService<ScanCoordinator>();

            var (ok, message) = libraries.Add("e2e", root);
            Assert.True(ok, message);
            var library = libraries.ListAll().Single(item => item.RootPath == root);

            var start = scan.Start(new ScanOptions { LibraryId = library.Id });
            Assert.True(start.Ok, start.Message);
            await WaitAsync(() => !scan.Progress.IsRunning, TimeSpan.FromSeconds(30));

            Assert.Equal(ScanStateKind.Completed, scan.Progress.Kind);
            var added = games.Query(new GameFilter { LibraryId = library.Id }, new GameSort(), 1, 50);
            Assert.Equal(2, added.Total);

            var client = await _factory.GetAuthClientAsync();
            var page = await client.GetStringAsync("/");
            Assert.Contains("Contra", page);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

    [Fact]
    public async Task RomEndpoint_ServesRange()
    {
        var root = Path.Combine(Path.GetTempPath(), "mica-web-rom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "range.nes");
        await File.WriteAllBytesAsync(file, Enumerable.Range(0, 1024).Select(value => (byte)value).ToArray());

        try
        {
            var libraries = _factory.Services.GetRequiredService<LibraryRepository>();
            var games = _factory.Services.GetRequiredService<GameRepository>();
            libraries.Add("range", root);
            var library = libraries.ListAll().Single(item => item.RootPath == root);

            var game = new Game { Title = "Range", Platform = "NES", RomPath = file, LibraryId = library.Id };
            var id = games.Insert(game);

            var client = await _factory.GetAuthClientAsync();
            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/games/{id}/rom");
            request.Headers.Range = new RangeHeaderValue(0, 9);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(10, (await response.Content.ReadAsByteArrayAsync()).Length);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public async Task RomEndpoint_RejectsPathOutsideLibrary()
    {
        var games = _factory.Services.GetRequiredService<GameRepository>();
        var id = games.Insert(new Game { Title = "Outside", Platform = "NES", RomPath = "/etc/passwd" });

        var client = await _factory.GetAuthClientAsync();
        var response = await client.GetAsync($"/api/games/{id}/rom");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CoverEndpoint_ServesImage()
    {
        var games = _factory.Services.GetRequiredService<GameRepository>();
        var covers = _factory.Services.GetRequiredService<CoverStore>();
        var fileName = "test-cover.png";
        await File.WriteAllBytesAsync(covers.FullPath(fileName),
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

        var id = games.Insert(new Game { Title = "Cover", Platform = "NES", RomPath = "/library/cover.nes", CoverFile = fileName });

        var client = await _factory.GetAuthClientAsync();
        var response = await client.GetAsync($"/api/games/{id}/cover");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PlayConfig_ReturnsJson()
    {
        var client = await _factory.GetAuthClientAsync();
        var response = await client.GetAsync("/api/play/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("platforms", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CsvExport_HasBom()
    {
        var games = _factory.Services.GetRequiredService<GameRepository>();
        games.Insert(new Game { Title = "CSV,Test", Platform = "NES", RomPath = "/library/csv.nes" });

        var client = await _factory.GetAuthClientAsync();
        var bytes = await client.GetByteArrayAsync("/api/export/csv");
        Assert.Equal(0xEF, bytes[0]);
    }

    private static async Task WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout) throw new TimeoutException("等待扫描超时");
            await Task.Delay(50);
        }
    }
}
