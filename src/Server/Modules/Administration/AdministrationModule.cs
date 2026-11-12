using Mir2.Management;

namespace Server.ConsoleHost;

public static class AdministrationModule
{
    public static void MapAdministration(this RouteGroupBuilder management)
    {
        var group = management.MapGroup("/admin");
        group.MapGet("/gm-commands", (GameRuntime runtime, CancellationToken token) => runtime.Invoke(() =>
            Mir2.Commands.GameCommands.All.Concat(Server.MirEnvir.Envir.Main.CustomCommands
                .Where(name => Mir2.Commands.GameCommands.All.All(command => !command.Original.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Select(name => new Mir2.Commands.GameCommandInfo(name, name, name, "", "@" + name, "所选版本自定义脚本命令；参数与权限由默认 NPC 脚本定义。", true))).ToArray(), token));
        group.MapGet("/monsters/{id:int}/appearance", (GameRuntime runtime, int id, CancellationToken token) => runtime.Invoke(() =>
            WorldFigureCatalog.Appearance(Server.MirEnvir.Envir.Main.GetMonsterInfo(id) ?? throw new FileNotFoundException("未找到怪物。")), token));
        group.MapGet("/catalog/{kind}", (GameRuntime runtime, string kind, int page, string? search, CancellationToken token) =>
            runtime.Invoke(() => WorldFigureCatalog.Read(kind, page, search), token));
        group.MapGet("/accounts", (GameRuntime runtime, PlayerAdministration players, string? search, bool? online, CancellationToken token) => runtime.Invoke(() => players.Accounts(search, online ?? false), token));
        group.MapGet("/accounts/{id:int}", (GameRuntime runtime, PlayerAdministration players, int id, CancellationToken token) => runtime.Invoke(() => players.ReadAccount(id), token));
        group.MapPost("/accounts", (GameRuntime runtime, PlayerAdministration players, AccountCreate value, CancellationToken token) => runtime.Invoke(() => players.CreateAccount(value), token));
        group.MapPut("/accounts", (GameRuntime runtime, PlayerAdministration players, AccountEdit value, CancellationToken token) => Apply(runtime, () => players.SaveAccount(value), token));
        group.MapPost("/accounts/{id:int}/password", (GameRuntime runtime, PlayerAdministration players, int id, PasswordReset value, CancellationToken token) => Apply(runtime, () => players.ResetPassword(id, value, false), token));
        group.MapPost("/accounts/{id:int}/storage-password", (GameRuntime runtime, PlayerAdministration players, int id, PasswordReset value, CancellationToken token) => Apply(runtime, () => players.ResetPassword(id, value, true), token));
        group.MapPost("/accounts/{id:int}/kick", (GameRuntime runtime, PlayerAdministration players, int id, AdminCommand value, CancellationToken token) => Apply(runtime, () => players.Kick(id, value), token));
        group.MapPost("/accounts/{id:int}/delete", (GameRuntime runtime, PlayerAdministration players, int id, AdminCommand value, CancellationToken token) => Apply(runtime, () => players.DeleteAccount(id, value), token));
        group.MapGet("/characters", (GameRuntime runtime, PlayerAdministration players, string? search, bool? online, CancellationToken token) => runtime.Invoke(() => players.Characters(search, online ?? false), token));
        group.MapGet("/characters/{id:int}", (GameRuntime runtime, PlayerAdministration players, int id, CancellationToken token) => runtime.Invoke(() => players.ReadCharacter(id), token));
        group.MapPost("/characters", (GameRuntime runtime, PlayerAdministration players, CharacterCreate value, CancellationToken token) => runtime.Invoke(() => players.CreateCharacter(value), token));
        group.MapPut("/characters", (GameRuntime runtime, PlayerAdministration players, CharacterEdit value, CancellationToken token) => Apply(runtime, () => players.SaveCharacter(value), token));
        group.MapPost("/characters/{id:int}/travel", (GameRuntime runtime, PlayerAdministration players, int id, CharacterTravel value, CancellationToken token) => Apply(runtime, () => players.Travel(id, value), token));
        group.MapPost("/characters/{id:int}/items", (GameRuntime runtime, PlayerAdministration players, int id, ItemGrant value, CancellationToken token) => Apply(runtime, () => players.Grant(id, value), token));
        group.MapPost("/characters/{id:int}/delete", (GameRuntime runtime, PlayerAdministration players, int id, AdminCommand value, CancellationToken token) => Apply(runtime, () => players.DeleteCharacter(id, true, value), token));
        group.MapPost("/characters/{id:int}/restore", (GameRuntime runtime, PlayerAdministration players, int id, AdminCommand value, CancellationToken token) => Apply(runtime, () => players.DeleteCharacter(id, false, value), token));
        group.MapGet("/guilds", (GameRuntime runtime, GuildAdministration guilds, string? search, CancellationToken token) => runtime.Invoke(() => guilds.List(search), token));
        group.MapGet("/guilds/{id:int}", (GameRuntime runtime, GuildAdministration guilds, int id, CancellationToken token) => runtime.Invoke(() => guilds.Read(id), token));
        group.MapPost("/guilds", (GameRuntime runtime, GuildAdministration guilds, GuildCreate value, CancellationToken token) => runtime.Invoke(() => guilds.Create(value), token));
        group.MapPut("/guilds", (GameRuntime runtime, GuildAdministration guilds, GuildEdit value, CancellationToken token) => Apply(runtime, () => guilds.Save(value), token));
        group.MapPost("/guilds/{id:int}/members", (GameRuntime runtime, GuildAdministration guilds, int id, GuildMemberEdit value, CancellationToken token) => Apply(runtime, () => guilds.Member(id, value), token));
        group.MapPost("/guilds/{id:int}/ranks", (GameRuntime runtime, GuildAdministration guilds, int id, GuildRankEdit value, CancellationToken token) => Apply(runtime, () => guilds.Rank(id, value), token));
        group.MapPost("/guilds/{id:int}/disband", (GameRuntime runtime, GuildAdministration guilds, int id, AdminCommand value, CancellationToken token) => Apply(runtime, () => guilds.Disband(id, value), token));
        group.MapGet("/maps", (GameRuntime runtime, WorldPopulation population, CancellationToken token) => runtime.Invoke(population.Maps, token));
        group.MapGet("/maps/{id:int}", async (GameRuntime runtime, WorldPopulation population, MapThumbnails thumbnails, int id, CancellationToken token) =>
        {
            var source = await runtime.Invoke(() => population.Terrain(id), token);
            var image = await thumbnails.Get(source.FileName, source.MiniMap, source.AssetVersion, token);
            return await runtime.Invoke(() => population.Overview(source, image), token);
        });
        group.MapGet("/maps/{id:int}/image", async (GameRuntime runtime, WorldPopulation population, MapThumbnails thumbnails, HttpContext context, int id, string version, CancellationToken token) =>
        {
            var source = await runtime.Invoke(() => population.Terrain(id), token);
            if (source.ImageVersion != version) return Results.StatusCode(410);
            var image = await thumbnails.Get(source.FileName, source.MiniMap, source.AssetVersion, token);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(image.Png, "image/png");
        });
        group.MapGet("/maps/{id:int}/thumbnail", async (GameRuntime runtime, WorldPopulation population, MapThumbnails thumbnails, HttpContext context, int id, CancellationToken token) =>
        {
            var source = await runtime.Invoke(() => population.Terrain(id), token);
            var image = await thumbnails.Get(source.FileName, source.MiniMap, source.AssetVersion, token);
            context.Response.Headers.CacheControl = "no-store";
            return new { version = source.ImageVersion, revision = image.Revision };
        });
        group.MapGet("/maps/{id:int}/tile", async (GameRuntime runtime, WorldPopulation population, MapThumbnails thumbnails, int id, string version, int x, int y, CancellationToken token) =>
        {
            var source = await runtime.Invoke(() => population.Terrain(id), token);
            if (source.ImageVersion != version) return Results.StatusCode(410);
            return Results.Bytes(await thumbnails.Native(source.FileName + ".map", source.AssetVersion, x, y, token), "image/png");
        });
        group.MapPost("/spawns/batch", (GameRuntime runtime, WorldPopulation population, SpawnBatch value, CancellationToken token) => Apply(runtime, () => population.Batch(value), token));
        group.MapGet("/spawns", (GameRuntime runtime, WorldPopulation population, int? mapId, string? search, CancellationToken token) => runtime.Invoke(() => population.List(mapId, search), token));
        group.MapPost("/spawns", (GameRuntime runtime, WorldPopulation population, SpawnMutation value, CancellationToken token) => Apply(runtime, () => population.Create(value), token));
        group.MapPut("/spawns", (GameRuntime runtime, WorldPopulation population, SpawnMutation value, CancellationToken token) => Apply(runtime, () => population.Save(value), token));
        group.MapPost("/spawns/{mapId:int}/{id:int}/delete", (GameRuntime runtime, WorldPopulation population, int mapId, int id, AdminCommand value, CancellationToken token) => Apply(runtime, () => population.Delete(mapId, id, value), token));
        group.MapPost("/spawns/{mapId:int}/{id:int}/refill", (GameRuntime runtime, WorldPopulation population, int mapId, int id, AdminCommand value, CancellationToken token) => runtime.Invoke(() => population.Refill(mapId, id, value), token));
        group.MapPost("/npcs", (GameRuntime runtime, WorldPopulation population, NpcMutation value, CancellationToken token) => Apply(runtime, () => population.CreateNpc(value), token));
        group.MapPut("/npcs", async (GameRuntime runtime, WorldPopulation population, NpcMutation value, CancellationToken token) =>
        {
            var restart = await runtime.Invoke(() => population.SaveNpc(value), token);
            if (restart) await runtime.Restart(CancellationToken.None);
            return new ActionResult(restart ? "已保存，更换 NPC 脚本后引擎已重启。" : "已备份并保存，NPC 已在游戏内即时更新。");
        });
        group.MapPost("/npcs/{id:int}/delete", (GameRuntime runtime, WorldPopulation population, int id, AdminCommand value, CancellationToken token) => Apply(runtime, () => population.DeleteNpc(id, value), token));
    }
    private static async Task<ActionResult> Apply(GameRuntime runtime, Action action, CancellationToken token, bool restart = false)
    {
        await runtime.Invoke(() => { action(); return true; }, token);
        if (restart) await runtime.Restart(CancellationToken.None);
        return new(restart ? "已备份并保存，引擎已重启；请重新连接游戏。" : "操作已完成。");
    }
}
