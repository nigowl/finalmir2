using System.Drawing;
using Mir2.Hosting;
using Mir2.Management;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public sealed record MapTerrainSource(int Id, string Title, string FileName, int Width, int Height, int MiniMap, string AssetVersion, string ImageVersion);

public sealed class WorldPopulation(ProjectPaths paths, AdminChanges changes, WorldDefinitions definitions, AssetCatalog assets)
{
    private static Envir World => Envir.Main;
    private static MapInfo Map(int id) => World.MapInfoList.SingleOrDefault(m => m.Index == id) ?? throw new FileNotFoundException("未找到地图。");
    private static RespawnInfo Spawn(int mapId, int id) => Map(mapId).Respawns.SingleOrDefault(s => s.RespawnIndex == id) ?? throw new FileNotFoundException("未找到刷怪点。");
    private static string Label(int mapId, int id) => Map(mapId).Title + "/" + id;
    public MapChoice[] Maps()
    {
        var npcs = World.NPCInfoList.GroupBy(n => n.MapIndex).ToDictionary(g => g.Key, g => g.Count());
        return World.MapList.OrderBy(m => m.Info.Title).Select(m => new MapChoice(m.Info.Index, m.Info.FileName, m.Info.Title, m.Width, m.Height,
            m.MonsterCount, m.Info.Respawns.Sum(s => (long)s.Count * World.SpawnMultiplier), m.Info.Respawns.Count, npcs.GetValueOrDefault(m.Info.Index))).ToArray();
    }
    public MapTerrainSource Terrain(int id)
    {
        var map = World.GetMap(id) ?? throw new FileNotFoundException("地图尚未加载。"); var mini = map.Info.BigMap > 0 ? map.Info.BigMap : map.Info.MiniMap;
        return new(id, map.Info.Title, map.Info.FileName, map.Width, map.Height, mini, assets.Version, AdminChanges.Version(new { id, map.Info.FileName, mini, assets.Version })[..20]);
    }
    public string SpawnVersion(int id) => AdminChanges.Version(Map(id).Respawns.Select(s => Read(Map(id), s).Version).ToArray());
    public MapOverview Overview(MapTerrainSource source, MapThumbnail image)
    {
        var current = Terrain(source.Id);
        if (source != current || source.Width != image.Width || source.Height != image.Height) throw new InvalidOperationException("地图已变化，请刷新后再编辑。");
        return new(source.Id, source.Title, source.FileName, image.Width, image.Height, source.ImageVersion, image.Source, image.Walkable, List(source.Id, null),
            World.NPCInfoList.Where(n => n.MapIndex == source.Id).Select(n => definitions.ReadNpc(n.Index)).ToArray(), SpawnVersion(source.Id));
    }
    private static SpawnDefinition Read(MapInfo map, RespawnInfo spawn)
    {
        var result = new SpawnDefinition(spawn.RespawnIndex, map.Index, map.Title, spawn.MonsterIndex, World.GetMonsterInfo(spawn.MonsterIndex)?.Name ?? "未知怪物",
            spawn.Location.X, spawn.Location.Y, spawn.Count, spawn.Spread, spawn.Delay, spawn.RandomDelay, spawn.Direction, spawn.RoutePath, spawn.SaveRespawnTime, spawn.RespawnTicks, 0, 0, "");
        var live = World.GetMap(map.Index)?.Respawns.FirstOrDefault(s => ReferenceEquals(s.Info, spawn));
        return result with { Version = AdminChanges.Version(result), Alive = live?.Count ?? 0, SecondsUntilSpawn = live == null ? 0 : Math.Max(0, (live.RespawnTime - World.Time) / 1000) };
    }
    public SpawnDefinition[] List(int? mapId, string? search) => World.MapInfoList.Where(m => mapId == null || m.Index == mapId)
        .SelectMany(m => m.Respawns.Select(s => Read(m, s))).Where(s => string.IsNullOrWhiteSpace(search) || s.Id.ToString() == search || s.MapName.Contains(search, StringComparison.OrdinalIgnoreCase) || s.MonsterName.Contains(search, StringComparison.OrdinalIgnoreCase))
        .OrderBy(s => s.MapId).ThenBy(s => s.Id).ToArray();
    private SpawnDefinition Validate(SpawnDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var map = World.GetMap(value.MapId) ?? throw new InvalidDataException("地图尚未加载。");
        if (World.GetMonsterInfo(value.MonsterId) == null) throw new InvalidDataException("未找到怪物定义。");
        if (value.Count is < 0 or > 1000 || value.Spread is < 0 or > 500 || value.DelayMinutes is < 1 or > 65535 || value.RandomDelayMinutes < 0 || value.RandomDelayMinutes > value.DelayMinutes || value.Direction is < 0 or > 8 || value.Ticks is < 0 or > 65535)
            throw new InvalidDataException("刷怪数量（0至1,000）、范围（0至500）、延迟、方向或时间设置无效。");
        if (value.X < 0 || value.Y < 0 || value.X >= map.Width || value.Y >= map.Height) throw new InvalidDataException("刷怪中心必须位于地图内。");
        var route = value.Route ?? throw new InvalidDataException("巡逻路线不能为空。");
        if (route.Length > 160 || route.Any(char.IsControl)) throw new InvalidDataException("巡逻路线的路径无效。");
        route = route.Trim().Replace('\\', '/');
        if (route != "" && !File.Exists(ProjectPaths.SafePath(Path.Combine(paths.World, "Envir", "Routes"), route + ".txt"))) throw new InvalidDataException("指定巡逻路线前，请先创建路线脚本。");
        if ((long)value.Count * World.SpawnMultiplier > 10000) throw new InvalidDataException("此处的刷怪倍率会使怪物超过10,000只。");
        return value with { Route = route };
    }
    private static void Apply(RespawnInfo target, SpawnDefinition value)
    {
        target.MonsterIndex = value.MonsterId; target.Location = new Point(value.X, value.Y); target.Count = (ushort)value.Count; target.Spread = (ushort)value.Spread;
        target.Delay = (ushort)value.DelayMinutes; target.RandomDelay = (ushort)value.RandomDelayMinutes; target.Direction = (byte)value.Direction; target.RoutePath = value.Route;
        target.SaveRespawnTime = value.SaveTime; target.RespawnTicks = (ushort)value.Ticks;
    }
    public void Create(SpawnMutation request)
    {
        var value = Validate(request.Value); var map = Map(value.MapId); AdminChanges.Confirm(request.Confirmation, map.Title);
        var spawn = new RespawnInfo { RespawnIndex = AdminChanges.Next(World.RespawnIndex, World.MapInfoList.SelectMany(m => m.Respawns).Select(s => s.RespawnIndex)) }; Apply(spawn, value);
        var prepared = PrepareSpawn(value);
        var counter = World.RespawnIndex;
        changes.Commit(() => { map.Respawns.Add(spawn); World.RespawnIndex = spawn.RespawnIndex; }, () => { map.Respawns.Remove(spawn); World.RespawnIndex = counter; });
        RefreshSpawn(map.Index, spawn, prepared);
    }
    public void Save(SpawnMutation request)
    {
        var value = Validate(request.Value); var map = Map(value.MapId); var target = Spawn(value.MapId, value.Id); var before = Read(map, target);
        AdminChanges.Match(before.Version, value.Version); AdminChanges.Confirm(request.Confirmation, Label(value.MapId, value.Id));
        var prepared = PrepareSpawn(value);
        changes.Commit(() => Apply(target, value), () => Apply(target, before));
        RefreshSpawn(map.Index, target, prepared);
    }
    public void Delete(int mapId, int id, AdminCommand command)
    {
        var map = Map(mapId); var spawn = Spawn(mapId, id); AdminChanges.Match(Read(map, spawn).Version, command.Version); AdminChanges.Confirm(command.Confirmation, Label(mapId, id));
        var position = map.Respawns.IndexOf(spawn);
        changes.Commit(() => map.Respawns.Remove(spawn), () => map.Respawns.Insert(position, spawn));
        var loaded = World.GetMap(mapId);
        if (loaded != null) foreach (var live in loaded.Respawns.Where(s => ReferenceEquals(s.Info, spawn)).ToArray()) { loaded.Respawns.Remove(live); World.SavedSpawns.Remove(live); }
    }
    public void Batch(SpawnBatch request)
    {
        var map = Map(request.MapId); AdminChanges.Match(SpawnVersion(request.MapId), request.Version); AdminChanges.Confirm(request.Confirmation, map.Title);
        if (request.Values == null || request.Values.Length is < 1 or > 100 || request.Values.Any(v => v == null || v.MapId != request.MapId || v.Id < 0)) throw new InvalidDataException("请在所选地图上提供1至100个布点。");
        var values = request.Values.Select(Validate).ToArray();
        if (values.Where(v => v.Id > 0).GroupBy(v => v.Id).Any(g => g.Count() > 1) || values.Sum(v => (long)v.Count * World.SpawnMultiplier) > 10000) throw new InvalidDataException("刷怪编号重复，或整批怪物数量超过10,000只。");
        var counter = World.RespawnIndex; var next = AdminChanges.Next(counter, World.MapInfoList.SelectMany(m => m.Respawns).Select(s => s.RespawnIndex));
        if (next > int.MaxValue - values.Length) throw new InvalidOperationException("刷怪编号已用尽。");
        var changeset = values.Select(v => (Value: v, Target: v.Id == 0 ? new RespawnInfo { RespawnIndex = next++ } : Spawn(request.MapId, v.Id), Prepared: PrepareSpawn(v))).ToArray();
        foreach (var entry in changeset.Where(c => c.Value.Id > 0)) AdminChanges.Match(Read(map, entry.Target).Version, entry.Value.Version);
        var before = changeset.Where(c => c.Value.Id > 0).Select(c => (c.Target, Value: Read(map, c.Target))).ToArray();
        changes.Commit(() => { foreach (var c in changeset) { Apply(c.Target, c.Value); if (c.Value.Id == 0) map.Respawns.Add(c.Target); } World.RespawnIndex = Math.Max(counter, next - 1); },
            () => { foreach (var c in changeset.Where(c => c.Value.Id == 0)) map.Respawns.Remove(c.Target); foreach (var c in before) Apply(c.Target, c.Value); World.RespawnIndex = counter; });
        foreach (var entry in changeset) RefreshSpawn(map.Index, entry.Target, entry.Prepared);
    }
    private static MapRespawn PrepareSpawn(SpawnDefinition value)
    {
        var map = World.GetMap(value.MapId)!; var definition = new RespawnInfo(); Apply(definition, value);
        return new MapRespawn(definition) { Map = map, WalkableCells = map.GetRespawnCells(definition) };
    }
    private static void RefreshSpawn(int mapId, RespawnInfo definition, MapRespawn prepared)
    {
        var map = World.GetMap(mapId) ?? throw new InvalidOperationException("地图尚未加载。");
        var live = map.Respawns.SingleOrDefault(s => ReferenceEquals(s.Info, definition));
        if (live == null) { live = prepared; live.Info = definition; map.Respawns.Add(live); }
        else { live.Monster = prepared.Monster; live.Route = prepared.Route; live.WalkableCells = prepared.WalkableCells; live.ErrorCount = 0; }
        live.RespawnTime = World.Time; live.NextSpawnTick = World.RespawnTick.CurrentTickcounter;
        World.SavedSpawns.Remove(live); if (definition.SaveRespawnTime && definition.RespawnTicks != 0) World.SavedSpawns.Add(live);
    }
    public ActionResult Refill(int mapId, int id, AdminCommand command)
    {
        var spawn = Spawn(mapId, id); AdminChanges.Match(Read(Map(mapId), spawn).Version, command.Version); AdminChanges.Confirm(command.Confirmation, Label(mapId, id));
        var live = World.GetMap(mapId)?.Respawns.SingleOrDefault(s => ReferenceEquals(s.Info, spawn)) ?? throw new InvalidOperationException("刷怪点尚未启用。");
        var needed = Math.Max(0L, (long)spawn.Count * World.SpawnMultiplier - live.Count); var before = live.Count;

        for (var i = 0L; i < Math.Min(500, needed); i++) if (!live.Spawn()) break;
        return new($"已补充 {live.Count - before} 只；每次最多补充 500 只，不超过当前刷怪上限。");
    }
    public void CreateNpc(NpcMutation request)
    {
        var value = definitions.ValidateNpc(request.Value); AdminChanges.Confirm(request.Confirmation, value.Name);
        var npc = new NPCInfo { Index = AdminChanges.Next(World.NPCIndex, World.NPCInfoList.Select(n => n.Index)) }; WorldDefinitions.ApplyNpc(npc, value);
        var counter = World.NPCIndex;
        changes.Commit(() => { World.NPCInfoList.Add(npc); World.NPCIndex = npc.Index; }, () => { World.NPCInfoList.Remove(npc); World.NPCIndex = counter; });
        var map = World.GetMap(npc.MapIndex)!; map.Info.NPCs.Add(npc);
        var live = new NPCObject(npc) { CurrentMap = map }; map.AddObject(live); live.RefreshDefinitionVisibility(); NotifyNpc(npc);
    }
    public bool SaveNpc(NpcMutation request)
    {
        var value = definitions.ValidateNpc(request.Value); var before = definitions.ReadNpc(value.Id); AdminChanges.Match(before.Version, value.Version); AdminChanges.Confirm(request.Confirmation, before.Name);
        var npc = WorldDefinitions.Npc(value.Id);
        var live = World.NPCs.Where(n => ReferenceEquals(n.Info, npc)).ToArray();
        if (npc.Conquest != 0 && value.MapId != before.MapId) throw new InvalidOperationException("攻城人物不能移到另一张地图。");
        changes.Commit(() => WorldDefinitions.ApplyNpc(npc, value), () => WorldDefinitions.ApplyNpc(npc, before));
        // shortcut: script path changes restart to preserve quest/merchant state; replace only after safe script rebinding is implemented.
        if (!before.FileName.Equals(value.FileName, StringComparison.OrdinalIgnoreCase)) return true;
        var oldMap = World.GetMap(before.MapId); var map = World.GetMap(value.MapId)!;
        foreach (var actor in live)
        {
            CloseNpc(actor);
            actor.CurrentMap?.Broadcast(new ServerPackets.ObjectRemove { ObjectID = actor.ObjectID }, new Point(before.X, before.Y));
            if (actor.CurrentMap != null) { actor.CurrentMap.GetCell(new Point(before.X, before.Y)).Remove(actor); actor.CurrentMap.NPCs.Remove(actor); }
            actor.CurrentMap = map; map.AddObject(actor); actor.RefreshDefinitionVisibility();
        }
        oldMap?.Info.NPCs.Remove(npc); if (!map.Info.NPCs.Contains(npc)) map.Info.NPCs.Add(npc);
        if (live.Length == 0) { var actor = new NPCObject(npc) { CurrentMap = map }; map.AddObject(actor); actor.RefreshDefinitionVisibility(); }
        NotifyNpc(npc); return false;
    }
    public void DeleteNpc(int id, AdminCommand command)
    {
        var npc = WorldDefinitions.Npc(id); AdminChanges.Match(definitions.ReadNpc(id).Version, command.Version); AdminChanges.Confirm(command.Confirmation, npc.Name);
        var live = World.NPCs.Where(n => ReferenceEquals(n.Info, npc)).ToArray();
        if (npc.Conquest != 0 || npc.CollectQuestIndexes.Count != 0 || npc.FinishQuestIndexes.Count != 0 || live.Any(n => n.Quests.Count != 0 || n.UsedGoods.Count != 0 || n.BuyBack.Values.Any(items => items.Count != 0)))
            throw new InvalidOperationException("删除此人物前，请先处理任务、攻城引用和商人保管的商品。");
        var position = World.NPCInfoList.IndexOf(npc);
        changes.Commit(() => World.NPCInfoList.Remove(npc), () => World.NPCInfoList.Insert(position, npc));
        foreach (var actor in live) { CloseNpc(actor); actor.Despawn(); actor.CurrentMap?.RemoveObject(actor); actor.CurrentMap?.Info.NPCs.Remove(npc); World.NPCs.Remove(actor); World.Scripts.Remove(actor.ScriptID); }
        foreach (var player in World.Players) { var info = npc.ClientInformation; info.ShowOnBigMap = false; info.CanTeleportTo = false; player.Enqueue(new ServerPackets.NewNPCInfo { Info = info }); }
    }
    private static void CloseNpc(NPCObject actor)
    {
        foreach (var player in World.Players.Where(p => p.NPCObjectID == actor.ObjectID))
        {
            player.ActionList.RemoveAll(a => a.Type == DelayedType.NPC); player.NPCPage = null; player.NPCObjectID = 0; player.NPCScriptID = 0; player.NPCDelayed = false;
            player.Enqueue(new ServerPackets.NPCResponse { Page = [] });
        }
    }
    private static void NotifyNpc(NPCInfo info)
    { foreach (var player in World.Players) { player.Connection.SentNPCInfo.Remove(info); player.Connection.CheckNPCInfo(info); } }
}
