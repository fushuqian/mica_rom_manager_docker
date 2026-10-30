using System.IO;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using MicaRomManager.Core.Data;
using MicaRomManager.Core.Models;
using MicaRomManager.Core.Services;

namespace MicaRomManager.Web;

public static class AppEndpoints
{
    public static void MapAppEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok", time = DateTime.Now }))
            .AllowAnonymous();

        app.MapGet("/readyz", (Db db) =>
        {
            try
            {
                using var connection = db.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                command.ExecuteScalar();
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "unavailable", error = ex.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();

        app.MapPost("/auth/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");

        app.MapGet("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });

        app.MapGet("/api/games/{id:long}/cover", (long id, GameRepository games, CoverStore covers) =>
        {
            var game = games.Get(id);
            if (game is null || string.IsNullOrWhiteSpace(game.CoverFile)) return Results.NotFound();

            var fileName = Path.GetFileName(game.CoverFile);
            if (string.IsNullOrWhiteSpace(fileName)) return Results.NotFound();
            var path = covers.FullPath(fileName);
            if (!File.Exists(path)) return Results.NotFound();

            return Results.File(path, ContentTypeFor(path), enableRangeProcessing: false);
        });

        app.MapGet("/api/games/{id:long}/rom", (long id, GameRepository games, PathGuard guard) =>
        {
            var game = games.Get(id);
            if (game is null || !File.Exists(game.RomPath)) return Results.NotFound();
            if (!guard.IsInsideRoots(game.RomPath)) return Results.Forbid();

            return Results.File(game.RomPath, "application/octet-stream", enableRangeProcessing: true);
        });

        app.MapGet("/api/games/{id:long}/download", (long id, GameRepository games, PathGuard guard) =>
        {
            var game = games.Get(id);
            if (game is null || !File.Exists(game.RomPath)) return Results.NotFound();
            if (!guard.IsInsideRoots(game.RomPath)) return Results.Forbid();

            return Results.File(game.RomPath, "application/octet-stream",
                Path.GetFileName(game.RomPath), enableRangeProcessing: true);
        });

        app.MapGet("/api/export/csv", (string? ids, GameRepository games) =>
        {
            IReadOnlyList<Game> list;
            if (!string.IsNullOrWhiteSpace(ids))
            {
                var parsed = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => long.TryParse(value, out var id) ? id : 0)
                    .Where(id => id > 0)
                    .ToList();
                list = games.GetMany(parsed);
            }
            else
            {
                list = games.ListAll();
            }

            var bytes = CsvExporter.Export(list);
            return Results.File(bytes, "text/csv; charset=utf-8", "mica-games.csv");
        });

        app.MapGet("/api/play/config", (PlatformRepository platforms) =>
        {
            var items = platforms.ListAll()
                .Where(platform => !string.IsNullOrWhiteSpace(platform.PlayCore))
                .Select(platform => new { platform = platform.Name, core = platform.PlayCore })
                .ToList();
            return Results.Ok(new { ready = items.Count > 0, platforms = items });
        });

        app.MapGet("/api/scan/state", (ScanCoordinator scan) => Results.Ok(scan.Progress));

        app.MapGet("/api/backups/{name}", (string name, BackupService backups) =>
        {
            var file = backups.List().FirstOrDefault(backup => backup.Name == name);
            if (file is null) return Results.NotFound();
            return Results.File(file.FullPath, "application/octet-stream", file.Name);
        });
    }

    private static async Task<IResult> LoginAsync(HttpContext context, IAntiforgery antiforgery,
        PasswordVerifier verifier)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Redirect("/login?error=expired");
        }

        var form = await context.Request.ReadFormAsync();
        var password = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();
        var target = string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? "/"
            : returnUrl;
        if (!target.StartsWith('/') || target.StartsWith("//")) target = "/";

        if (!verifier.Verify(password)) return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(target)}");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "admin"),
        }, CookieAuthenticationDefaults.AuthenticationScheme);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
            });

        return Results.Redirect(target);
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };
}
