using System.Net;
using System.Security.Cryptography;
using Mir2.Hosting;
using Mir2.Management;

namespace Server.ConsoleHost;

public static class ResourceService
{
    public static WebApplication Build(ProjectPaths paths, AssetCatalog catalog, WebApplicationBuilder builder)
    {
        var key = paths.AdminKey();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Parse(Environment.GetEnvironmentVariable("MIR2_BIND_ADDRESS") ?? "127.0.0.1"), int.Parse(Environment.GetEnvironmentVariable("MIR2_RESOURCE_PORT") ?? "5080"));
            options.Limits.MaxRequestBodySize = 513L * 1024 * 1024;
        });
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(60));
        builder.Services.AddSingleton(paths); builder.Services.AddSingleton(catalog);
        builder.Services.AddSingleton<ServerLogs>(); builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerLogs>());
        builder.Services.AddSingleton<GameRuntime>(); builder.Services.AddHostedService(sp => sp.GetRequiredService<GameRuntime>());
        builder.Services.AddSingleton<DevelopmentFiles>();
        builder.Services.AddSingleton<SystemSettings>();
        builder.Services.AddSingleton<PublicShopSettings>();
        builder.Services.AddSingleton<WorldDefinitions>();
        builder.Services.AddSingleton<MonsterDrops>();
        builder.Services.AddSingleton<MonsterDefinitions>();
        builder.Services.AddSingleton((SqliteDatabaseStorage)(Server.DatabaseStorage.Current ?? throw new InvalidOperationException("需要 SQLite 存储。")));
        builder.Services.AddSingleton<AdminChanges>();
        builder.Services.AddSingleton<PlayerAdministration>();
        builder.Services.AddSingleton<GuildAdministration>();
        builder.Services.AddSingleton<WorldPopulation>();
        builder.Services.AddSingleton<MapThumbnails>();
        builder.Services.AddSingleton<CompanionStore>();
        builder.Services.AddSingleton<CompanionService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CompanionService>());
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            if (host != "localhost" && !IPAddress.TryParse(host, out _)) { context.Response.StatusCode = 403; return; }
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var management = context.Request.Path.StartsWithSegments("/management");
            if (management && !ProjectPaths.KeyEquals(context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal), key))
            { context.Response.StatusCode = 401; return; }
            if ((context.Request.Path.StartsWithSegments("/management/companions") || context.Request.Path.StartsWithSegments("/management/settings") || context.Request.Path.StartsWithSegments("/management/shop") || context.Request.Path.StartsWithSegments("/management/admin/monsters")) && context.Request.Headers["X-Mir2-Version"] != paths.VersionId)
            { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "此操作必须显式绑定当前版本。" }); return; }
            if (management && context.Request.Headers.TryGetValue("X-Mir2-Version", out var version) && version != paths.VersionId)
            { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "管理端与服务端的版本目录不匹配。" }); return; }
            var runtime = context.RequestServices.GetRequiredService<GameRuntime>();
            var mutation = management && context.Request.Method is not ("GET" or "HEAD");
            IDisposable? lease = null;
            try
            {
                if (context.Request.Path.StartsWithSegments("/assets"))
                {
                    lease = catalog.TryRead();
                    if (lease == null) { context.Response.StatusCode = 503; context.Response.Headers.RetryAfter = "3"; return; }
                }
                if (mutation) await runtime.Changes.WaitAsync(context.RequestAborted);
                try { await next(); }
                finally { if (mutation) runtime.Changes.Release(); }
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or EndOfStreamException)
            { if (!context.Response.HasStarted) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = error.Message }); } }
            catch (FileNotFoundException error)
            { if (!context.Response.HasStarted) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new { error = error.Message }); } }
            catch (InvalidOperationException error)
            { if (!context.Response.HasStarted) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = error.Message }); } }
            finally { lease?.Dispose(); }
        });
        app.MapGet("/health", (GameRuntime runtime) => new { ready = runtime.Ready, gamePort = runtime.GamePort, version = catalog.Version, gameVersion = paths.VersionName });
        app.MapGet("/assets/manifest", (GameRuntime runtime) => new { version = catalog.Version, gamePort = runtime.GamePort, protocol = "Crystal TCP", frameFormat = 1, checksumAlgorithm = "sha256", healthDisplay = true, heroRecovery = true, heroDeletion = true, characterLocation = true, publicShop = true, publicShopLayout = true, questNavigation = true, functionKeys = 12, attackModes = 8, playerMoveMilliseconds = Settings.PlayerTiming.MoveMilliseconds, playerActionSpeedPercent = Settings.PlayerTiming.ActionSpeedPercent, itemRecycling = true });
        app.MapGet("/game/item-catalog", (GameRuntime runtime, CancellationToken token) => runtime.Invoke(() => Server.MirEnvir.Envir.Main.ItemInfoList.Select(item => new ItemCatalogEntry(item.Index, item.FriendlyName, item.Type)).ToArray(), token));
        app.MapGet("/game/storage-unlocks", (GameRuntime runtime, CancellationToken token) => runtime.Invoke(StorageUnlocks.Read, token));
        var assets = app.MapGroup("/assets/{requested}");
        assets.AddEndpointFilter(async (context, next) => context.HttpContext.Request.RouteValues["requested"]?.ToString() == catalog.Version ? await next(context) : Results.StatusCode(410));
        IResult Bytes(HttpContext context, byte[] bytes, string contentType = "application/octet-stream")
        {
            context.Response.Headers["X-Content-SHA256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            return Results.Bytes(bytes, contentType);
        }
        assets.MapGet("/map-thumbnail-meta/{**name}", async (HttpContext context, MapThumbnails thumbnails, string name, CancellationToken token) =>
        {
            var image = await thumbnails.Resource(name + ".map", false, token);
            context.Response.Headers.CacheControl = "no-store";
            return new MapThumbnailInfo(image.Revision, image.Width, image.Height);
        });
        assets.MapGet("/map-thumbnail/{**name}", async (HttpContext context, MapThumbnails thumbnails, string name, string revision, CancellationToken token) =>
        {
            var image = await thumbnails.Resource(name + ".map", false, token);
            return revision == image.Revision ? Bytes(context, image.Png, "image/png") : Results.StatusCode(410);
        });
        assets.MapGet("/sound-list", (HttpContext context) => Bytes(context, File.ReadAllBytes(catalog.ReadPath("sounds", "SoundList.lst")), "text/plain"));
        assets.MapGet("/map/{**name}", (HttpContext context, string name) => Bytes(context, File.ReadAllBytes(catalog.ReadPath("maps", name + ".map"))));
        assets.MapGet("/frame/{**library}", (HttpContext context, string library, int index) =>
        { catalog.ReadPath("libraries", library + ".Lib"); return Bytes(context, catalog.Decoder.Frame(library, index)); });
        assets.MapGet("/frames/{**library}", (HttpContext context, string library) =>
        { catalog.ReadPath("libraries", library + ".Lib"); return Bytes(context, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalog.Decoder.Frames(library)), "application/json"); });
        assets.MapGet("/sound/{**name}", (HttpContext context, string name) => Bytes(context, File.ReadAllBytes(catalog.ReadPath("sounds", name))));
        var management = app.MapGroup("/management");
        management.MapOperations(); management.MapPublicShopSettings(); management.MapResources(); management.MapDevelopment(); management.MapAdministration(); management.MapCompanions();
        return app;
    }
}
