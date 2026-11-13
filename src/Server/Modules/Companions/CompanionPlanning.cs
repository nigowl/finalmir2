using System.Drawing;
using Mir2.Companions;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionPlanning
{
    public static bool CanDrop(IEnumerable<DropInfo> drops, ISet<int> items, int depth = 0) => depth < 16 && drops.Any(drop =>
        drop.Chance > 0 && (drop.Item != null && items.Contains(drop.Item.Index) || drop.GroupedDrop != null && CanDrop(drop.GroupedDrop, items, depth + 1)));

    public static IEnumerable<int> KillSources(MonsterInfo monster) => Envir.Main.MonsterInfoList
        .Where(candidate => candidate.Name.StartsWith(monster.Name, StringComparison.OrdinalIgnoreCase)).Select(candidate => candidate.Index);

    public static int[] QuestMonsters(QuestInfo quest) => quest.KillTasks.SelectMany(task => KillSources(task.Monster))
        .Concat(DropSources(quest.ItemTasks.Select(task => task.Item.Index).ToHashSet())).Distinct().ToArray();

    private static IEnumerable<int> DropSources(ISet<int> items) => items.Count == 0 ? [] : Envir.Main.MonsterInfoList
        .Where(monster => CanDrop(monster.Drops, items)).Select(monster => monster.Index);

    public static IEnumerable<MovementInfo> WalkingExits(MapInfo map) => map.Movements.Where(exit => !exit.NeedHole && !exit.NeedMove && exit.ConquestIndex == 0);

    public static Dictionary<int, KnownExit?> Routes(int origin, IEnumerable<MapInfo> maps, int partySize = 0)
    {
        var definitions = maps.Where(map => map.Index == origin || !map.RequiredGroup || partySize >= Math.Max(2, map.RequiredGroupSize))
            .DistinctBy(map => map.Index).ToDictionary(map => map.Index);
        var routes = new Dictionary<int, KnownExit?> { [origin] = null };
        var queue = new Queue<int>(); queue.Enqueue(origin);
        while (queue.TryDequeue(out var id) && routes.Count < 2048)
        {
            if (!definitions.TryGetValue(id, out var map)) continue;
            // 大地图图标只是显示设置，房屋出口通常没有图标，仍是正常步行通道。
            foreach (var exit in WalkingExits(map))
            {
                if (!definitions.ContainsKey(exit.MapIndex) || routes.ContainsKey(exit.MapIndex)) continue;
                routes[exit.MapIndex] = routes[id] ?? new(id, exit.Source.X, exit.Source.Y, exit.MapIndex);
                queue.Enqueue(exit.MapIndex);
            }
        }
        return routes;
    }

    public static KnownGoal? Choose(PlayerObject player, CompanionProfile profile, ISet<string>? blocked = null)
    {
        var world = Envir.Main;
        var routes = Routes(player.CurrentMap.Info.Index, world.MapInfoList, player.GroupMembers?.Count ?? 0);
        var maps = world.MapList.Where(map => routes.ContainsKey(map.Info.Index)).DistinctBy(map => map.Info.Index).ToArray();
        var npcs = world.NPCs.Where(npc => npc.Visible && npc.CurrentMap != null && routes.ContainsKey(npc.CurrentMap.Info.Index) &&
            (!npc.VisibleLog.TryGetValue(player.Info.Index, out var visible) || visible)).ToArray();
        KnownGoal NpcGoal(string kind, QuestInfo quest, NPCObject npc) => new(kind, npc.CurrentMap.Info.Index,
            npc.CurrentLocation.X, npc.CurrentLocation.Y, $"{(kind == "turn-in" ? "交付" : "接取")}任务“{quest.Name}”：{npc.Name}", quest.Index, npc.ObjectID,
            NextExit: routes[npc.CurrentMap.Info.Index], Key:$"{kind}/{quest.Index}/{npc.ObjectID}");
        if (CompanionInventory.Choose(player, npcs, routes, blocked) is { } unload) return unload;
        if (CompanionSupplies.Choose(player, profile, npcs, routes, blocked) is { } supply) return supply;
        foreach (var progress in player.CurrentQuests.Where(quest => quest.Completed))
        {
            var npc = npcs.FirstOrDefault(npc => npc.ObjectID == progress.Info.FinishNpcIndex);
            if (npc != null && blocked?.Contains(NpcGoal("turn-in",progress.Info,npc).Key)!=true) return NpcGoal("turn-in", progress.Info, npc);
        }
        if (player.Info.Equipment[(int)EquipmentSlot.Weapon] == null && CompanionRecovery.Choose(player, maps, npcs, routes, blocked) is { } recovery)
            return recovery;
        foreach (var progress in player.CurrentQuests.Where(quest => !quest.Completed))
        {
            var kills = progress.KillTaskCount.Where(task => !task.Complete).SelectMany(task => KillSources(task.Info.Monster));
            var items = progress.ItemTaskCount.Where(task => !task.Complete).Select(task => task.ItemID).ToHashSet();
            var monsters = kills.Concat(DropSources(items)).Distinct().ToHashSet();
            var goal = Hunt(player, profile, maps, routes, monsters, progress.Index,
                $"任务“{progress.Info.Name}”{(items.Count > 0 ? "：寻找掉落任务物品的怪物" : "：击杀任务目标")}",blocked);
            if (goal != null) return goal;
        }
        if (player.CurrentQuests.Count < Globals.MaxConcurrentQuests)
        {
            var available = npcs.SelectMany(npc => npc.Quests.Select(quest => (npc, quest)))
                .Where(pair => pair.npc.ObjectID == pair.quest.NpcIndex && pair.quest.FlagTasks.Count == 0 &&
                    pair.quest.CanAccept(player) && !player.CompletedQuests.Contains(pair.quest.Index) && !player.CurrentQuests.Any(quest => quest.Index == pair.quest.Index))
                .OrderBy(pair => pair.npc.CurrentMap == player.CurrentMap ? Distance(player.CurrentLocation, pair.npc.CurrentLocation) : 10000).Take(100);
            foreach (var (npc, quest) in available)
                if (blocked?.Contains(NpcGoal("accept",quest,npc).Key)!=true &&
                    (quest.KillTasks.Count == 0 && quest.ItemTasks.Count == 0 || Hunt(player, profile, maps, routes, QuestMonsters(quest).ToHashSet(), quest.Index, "预检任务目标",blocked) != null))
                { player.CheckQuestInfo(quest); return NpcGoal("accept", quest, npc); }
        }
        return Hunt(player, profile, maps, routes, null, 0, "按当前战力寻找练级怪物",blocked);
    }

    private static KnownGoal? Hunt(PlayerObject player, CompanionProfile profile, Map[] maps, Dictionary<int, KnownExit?> routes,
        HashSet<int>? wanted, int questId, string reason, ISet<string>? blocked)
    {
        var world = Envir.Main;
        var candidates = maps.Where(map => !map.Info.NoFight).SelectMany(map => map.Info.Respawns.Where(spawn => spawn.Count > 0)
            .Select(spawn => (map, spawn, monster: world.GetMonsterInfo(spawn.MonsterIndex))))
            .Where(pair => pair.monster != null && CanHunt(player.Stats, player.Level, pair.monster, profile.Traits.Risk) &&
                (wanted != null ? wanted.Contains(pair.monster.Index) : pair.monster.Experience > 0))
            .OrderBy(pair => (pair.map == player.CurrentMap ? Distance(player.CurrentLocation, pair.spawn.Location) : 10000) +
                Math.Max(0, pair.monster.Level - player.Level) * 40).Take(32);
        foreach (var (map, spawn, monster) in candidates)
        {
            var key=$"hunt/{map.Info.Index}/{spawn.Location.X}/{spawn.Location.Y}/{monster.Index}/{questId}";
            if(blocked?.Contains(key)==true)continue;
            var live = world.Objects.OfType<MonsterObject>().Where(target => target.CurrentMap == map && !target.Dead && target.Master == null && target.Info.Index == monster.Index)
                .OrderBy(target => Distance(target.CurrentLocation, map == player.CurrentMap ? player.CurrentLocation : spawn.Location)).FirstOrDefault();
            var cells = live == null ? map.GetRespawnCells(spawn) : null;
            if (live == null && cells!.Count == 0) continue;
            var point = live?.CurrentLocation ?? cells!.OrderBy(cell => Distance(cell, spawn.Location)).First();
            return new("hunt", map.Info.Index, point.X, point.Y, $"{reason}：{monster.GameName}（{monster.Level}级，地图 {map.Info.Title}）",
                questId, MonsterIds: wanted?.Order().ToArray() ?? [monster.Index], NextExit: routes[map.Info.Index],Key:key);
        }
        return null;
    }

    private static int Distance(Point a, Point b) => Functions.MaxDistance(a, b);

    public static bool CanHunt(Stats player, int level, MonsterInfo monster, int risk)
    {
        if (monster.AI is 6 or 56 or 57 or 58 or 64 or 113 || monster.IsBoss || monster.Stats[Stat.HP] <= 0) return false;
        // 原版鹿/稻草人的定义等级高于新手角色，按现有攻防和预计承伤判断，不能只比等级。
        var damage = (player[Stat.MinDC] + (double)player[Stat.MaxDC] - monster.Stats[Stat.MinAC] - monster.Stats[Stat.MaxAC]) / 2;
        if (damage <= 0 || player[Stat.HP] <= 0) return false;
        var hits = Math.Ceiling(monster.Stats[Stat.HP] / damage);
        if (hits > 30 + risk / 3) return false;
        var interval = Math.Max(700, 1400 - level * 14 - player[Stat.AttackSpeed] * 60);
        var incoming = Math.Max(0, monster.Stats[Stat.MaxDC] - player[Stat.MinAC]);
        var spells = Math.Max(0, Math.Max(monster.Stats[Stat.MaxMC], monster.Stats[Stat.MaxSC]) - player[Stat.MinMAC]);
        var loss = Math.Ceiling(hits * interval / Math.Max(500d, monster.AttackSpeed)) * Math.Max(incoming, spells);
        return loss <= player[Stat.HP] * (50 + risk / 2d) / 100;
    }
}
