// Native micro-client resource delivery, 2026-11-11. GPL-3.0-only.
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Server.ConsoleHost;

public static class ResourceService
{
    public static WebApplication Build(string root, int gamePort, int resourcePort, IPAddress address)
    {
        var client = Path.Combine(root, "Crystal Client");
        if (!Directory.Exists(Path.Combine(client, "Data"))) throw new DirectoryNotFoundException(client);
        var assets = new GameAssets(client);
        var fingerprint = new StringBuilder("native-assets-v1\n");
        foreach (var directory in new[] { "Data", "Map", "Sound" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(client, directory), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(file);
            fingerprint.Append(Path.GetRelativePath(client, file)).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString()))).ToLowerInvariant()[..20];
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory, Args = [] });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseKestrel(options => options.Listen(address, resourcePort));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            if (host != "localhost" && !IPAddress.TryParse(host, out _)) { context.Response.StatusCode = 403; return; }
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            try { await next(); }
            catch (FileNotFoundException) { context.Response.StatusCode = 404; }
            catch (InvalidDataException error)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = error.Message });
            }
        });
        app.MapGet("/health", () => new { ready = Server.MirEnvir.Envir.Main.NetworkStarted, gamePort });
        app.MapGet("/assets/manifest", () => new { version, gamePort, protocol = "Crystal TCP", frameFormat = 1 });
        app.MapGet("/assets/{requested}/sound-list", (string requested) => requested == version
            ? Results.File(Path.Combine(client, "Sound", "SoundList.lst"), "text/plain") : Results.NotFound());
        app.MapGet("/assets/{requested}/map/{**name}", (string requested, string name) =>
            requested == version ? Results.File(assets.MapPath(name), "application/octet-stream") : Results.NotFound());
        app.MapGet("/assets/{requested}/frame/{**library}", (string requested, string library, int index) =>
            requested == version ? Results.Bytes(assets.Frame(library, index), "application/octet-stream") : Results.NotFound());
        app.MapGet("/assets/{requested}/frames/{**library}", (string requested, string library) =>
            requested == version ? Results.Json(assets.Frames(library)) : Results.NotFound());
        app.MapGet("/assets/{requested}/sound/{**name}", (string requested, string name) =>
            requested == version ? Results.File(assets.SoundPath(name), "audio/wav") : Results.NotFound());
        return app;
    }
}
