using System.IO;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using MicaRomManager.Core.Ai;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Services;
using MicaRomManager.Web;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Environment.GetEnvironmentVariable("MICA_DATA_DIR")
              ?? builder.Configuration["Mica:DataDir"];
if (string.IsNullOrWhiteSpace(dataDir))
    dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
dataDir = Path.GetFullPath(dataDir);
foreach (var sub in new[] { "", "covers", "keys", "backups", "logs", "trash" })
    Directory.CreateDirectory(Path.Combine(dataDir, sub));

var password = Environment.GetEnvironmentVariable("MICA_PASSWORD")
               ?? builder.Configuration["Mica:Password"];
if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
{
    throw new InvalidOperationException(
        "必须通过环境变量 MICA_PASSWORD 设置至少 8 位的访问密码（可在 .env 中配置）。");
}

var libraryRoots = (Environment.GetEnvironmentVariable("MICA_LIBRARY_ROOTS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(LibraryRepository.Normalize)
    .Where(root => root.Length > 0)
    .ToList();

builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(dataDir, "logs")));

builder.Services.AddSingleton(new Db(dataDir));
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<GameRepository>();
builder.Services.AddSingleton<PlatformRepository>();
builder.Services.AddSingleton<LookupRepository>();
builder.Services.AddSingleton<LibraryRepository>();
builder.Services.AddSingleton<PathGuard>();
builder.Services.AddSingleton(_ => new CoverStore(dataDir));
builder.Services.AddSingleton<ScanCoordinator>();
builder.Services.AddSingleton<OrganizerService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton(new PasswordVerifier(password));
builder.Services.AddSingleton(new LibraryRootPolicy(libraryRoots));

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")))
    .SetApplicationName("MicaRomManager");
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

builder.Services.AddHttpClient("ai")
    .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
    });

builder.Services.AddSingleton<IAiChatClient, AiChatClient>();
builder.Services.AddSingleton<AiSearchService>();
builder.Services.AddSingleton<AiPromptCatalog>();
builder.Services.AddSingleton<AiMetadataProvider>();
builder.Services.AddSingleton<ScrapeService>();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "mica_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "mica_csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.HeaderName = "X-CSRF-TOKEN";
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 5;
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownIPNetworks = { },
    KnownProxies = { },
});

app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAppEndpoints();
app.MapRazorComponents<MicaRomManager.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program
{
}

public sealed class LibraryRootPolicy
{
    public IReadOnlyList<string> AllowedRoots { get; }

    public LibraryRootPolicy(IReadOnlyList<string> roots) => AllowedRoots = roots;

    /// <summary>限制可添加的库目录（可选）：设置后只允许其子目录。</summary>
    public (bool Ok, string Message) Check(string path)
    {
        if (AllowedRoots.Count == 0) return (true, "");
        return AllowedRoots.Any(root => PathGuard.IsUnder(path, root))
            ? (true, "")
            : (false, $"路径必须在 {string.Join("、", AllowedRoots)} 之下。");
    }
}
