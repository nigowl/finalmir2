using Mir2.Management;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public static class DevelopmentModule
{
    public static void MapDevelopment(this RouteGroupBuilder management)
    {
        var group = management.MapGroup("/development");
        group.MapGet("/files", (DevelopmentFiles files, string? search) => files.List(search));
        group.MapGet("/file", (DevelopmentFiles files, string path) => files.Read(path));
        group.MapPut("/file", (DevelopmentFiles files, TextEdit edit) => files.Save(edit));
        group.MapGet("/catalog/{kind}", (GameRuntime runtime, string kind, string? search, CancellationToken token) => runtime.Invoke(() =>
        {
            var world = Envir.Main;
            IEnumerable<CatalogRow> rows = kind switch
            {
                "items" => world.ItemInfoList.Select(i => new CatalogRow(i.Index, i.Name, $"{i.Type.ToLocalizedString()} · 要求{i.RequiredAmount} · {i.Price}金币 · 堆叠上限{i.StackSize}")),
                "monsters" => world.MonsterInfoList.Select(m => new CatalogRow(m.Index, m.Name, $"等级{m.Level} · 生命{m.Stats[Stat.HP]} · 经验{m.Experience}")),
                "maps" => world.MapInfoList.Select(m => new CatalogRow(m.Index, m.Title, m.FileName)),
                "npcs" => world.NPCInfoList.Select(n => new CatalogRow(n.Index, n.Name, $"地图{n.MapIndex} ({n.Location.X},{n.Location.Y}) · {n.FileName}")),
                "quests" => world.QuestInfoList.Select(q => new CatalogRow(q.Index, q.Name, $"等级{q.RequiredMinLevel}–{q.RequiredMaxLevel} · {q.FileName}")),
                _ => throw new InvalidDataException("未知的目录类型。")
            };
            return rows.Where(row => string.IsNullOrEmpty(search) || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Id.ToString() == search).ToArray();
        }, token));
        group.MapGet("/definition/{kind}/{id:int}", (GameRuntime runtime, WorldDefinitions definitions, string kind, int id, CancellationToken token) =>
            runtime.Invoke(() => definitions.Read(kind, id), token));
        group.MapPut("/definition", (GameRuntime runtime, WorldDefinitions definitions, DefinitionEdit edit, CancellationToken token) =>
            Save(runtime, () => definitions.Save(edit), token));
        group.MapPost("/definition/copy", async (GameRuntime runtime, WorldDefinitions definitions, DefinitionClone copy, CancellationToken token) =>
        {
            var created = await runtime.Invoke(() => definitions.Clone(copy), token);
            await runtime.Restart(CancellationToken.None); return created;
        });
        group.MapGet("/maps/{id:int}", (GameRuntime runtime, WorldDefinitions definitions, int id, CancellationToken token) => runtime.Invoke(() => definitions.ReadMap(id), token));
        group.MapPut("/maps", (GameRuntime runtime, WorldDefinitions definitions, MapDefinition edit, CancellationToken token) => Save(runtime, () => definitions.Save(edit), token));
        group.MapGet("/npcs/{id:int}", (GameRuntime runtime, WorldDefinitions definitions, int id, CancellationToken token) => runtime.Invoke(() => definitions.ReadNpc(id), token));
        group.MapPut("/npcs", (GameRuntime runtime, WorldDefinitions definitions, NpcDefinition edit, CancellationToken token) => Save(runtime, () => definitions.Save(edit), token));
        group.MapGet("/quests/{id:int}", (GameRuntime runtime, WorldDefinitions definitions, int id, CancellationToken token) => runtime.Invoke(() => definitions.ReadQuest(id), token));
        group.MapPut("/quests", (GameRuntime runtime, WorldDefinitions definitions, QuestDefinition edit, CancellationToken token) => Save(runtime, () => definitions.Save(edit), token));
    }
    private static async Task<ActionResult> Save(GameRuntime runtime, Action save, CancellationToken token)
    {
        await runtime.Invoke(() => { save(); return true; }, token);
        // Once committed, finish restarting even if the browser disconnects.
        await runtime.Restart(CancellationToken.None);
        return new("定义已备份并保存到 SQLite，游戏引擎已重启，请重新连接客户端。");
    }
}
