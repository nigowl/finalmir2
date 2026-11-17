using System.Drawing;
using Mir2.Companions;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionPlanning
{
    public static bool CanDrop(IEnumerable<DropInfo> drops, ISet<int> items, int depth = 0) => depth < 16 && drops.Any(drop =>
        drop.BaseProbability > 0 && (drop.Item != null && items.Contains(drop.Item.Index) || drop.GroupedDrop != null && CanDrop(drop.GroupedDrop, items, depth + 1)));

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

    public static KnownGoal? Choose(PlayerObject player, CompanionProfile profile, ISet<string>? blocked = null, KnownGoal? guildGoal = null)
    {
        var world = Envir.Main;
        var routes = Routes(player.CurrentMap.Info.Index, world.MapInfoList, player.GroupMembers?.Count ?? 0);
        var maps = world.MapList.Where(map => routes.ContainsKey(map.Info.Index)).DistinctBy(map => map.Info.Index).ToArray();
        var npcs = world.NPCs.Where(npc => npc.Visible && npc.CurrentMap != null && routes.ContainsKey(npc.CurrentMap.Info.Index) &&
            (!npc.VisibleLog.TryGetValue(player.Info.Index, out var visible) || visible)).ToArray();
        KnownGoal NpcGoal(string kind, QuestInfo quest, NPCObject npc) => new(kind, npc.CurrentMap.Info.Index,
            npc.CurrentLocation.X, npc.CurrentLocation.Y, $"{(kind == "turn-in" ? "交付" : "接取")}任务“{quest.Name}”：{npc.Name}", quest.Index, npc.ObjectID,
            NextExit: routes[npc.CurrentMap.Info.Index], Key:$"{kind}/{quest.Index}/{npc.ObjectID}");
        if (CompanionStorage.Choose(player, npcs, routes, blocked) is { } deposit) return deposit;
        if (CompanionInventory.Choose(player, npcs, routes, blocked) is { } unload) return unload;
        if (player.NPCPage?.Key.Equals(NPCScript.StorageKey,StringComparison.OrdinalIgnoreCase)==true &&
            npcs.Any(npc=>npc.ObjectID==player.NPCObjectID&&npc.CurrentMap==player.CurrentMap&&Functions.InRange(npc.CurrentLocation,player.CurrentLocation,Globals.DataRange)) &&
            CompanionStorage.Choose(player,npcs,routes,blocked,true) is { } reserve) return reserve;
        if (CompanionSupplies.Choose(player, profile, npcs, routes, blocked) is { } supply) return supply;
        if (CompanionRepairs.Choose(player, npcs, routes, blocked) is { } repair) return repair;
        var needsFunds=CompanionSupplies.NeedsFunds(player,profile,npcs);
        foreach (var progress in player.CurrentQuests.Where(quest => quest.Completed))
        {
            var npc = npcs.FirstOrDefault(npc => npc.ObjectID == progress.Info.FinishNpcIndex);
            // 缺药又付不起药费时不能为远程交付穿越危图，先在当前安全区域筹资。
            if(needsFunds && npc?.CurrentMap!=player.CurrentMap)continue;
            if (npc != null && RewardsFit(player,progress.Info) && blocked?.Contains(NpcGoal("turn-in",progress.Info,npc).Key)!=true) return NpcGoal("turn-in", progress.Info, npc);
        }
        if (player.Info.Equipment[(int)EquipmentSlot.Weapon] == null && CompanionRecovery.Choose(player, maps, npcs, routes, blocked) is { } recovery)
            return recovery;
        if (needsFunds)
            return Hunt(player, profile, maps, routes, null, 0, "红药与药费不足，先低风险打怪筹资", blocked, funds: true) ??
                new("wait", player.CurrentMap.Info.Index, player.CurrentLocation.X, player.CurrentLocation.Y,
                    "红药与药费不足，暂无可达的低风险金币来源，等待重新规划", Key: "fund/wait");
        if (CompanionDevelopment.Choose(player, profile, npcs, routes, blocked) is { } development) return development;
        if (guildGoal != null) return guildGoal;
        var flagSources = npcs.Select(npc => (Npc: npc, Source: CompanionQuestFlags.Source(npc))).Where(pair => pair.Source != null).ToArray();
        bool HasFlagSources(QuestInfo quest) => quest.FlagTasks.All(flag =>
            flagSources.Any(pair => pair.Source!.Value == (quest.Index, flag.Number)));
        foreach (var progress in player.CurrentQuests.Where(quest => !quest.Completed && HasFlagSources(quest.Info)))
        {
            var source = flagSources.Where(pair => pair.Source!.Value.QuestId == progress.Index &&
                progress.FlagTaskSet.Any(flag => !flag.Complete && flag.Number == pair.Source.Value.Flag))
                .OrderBy(pair => pair.Npc.CurrentMap == player.CurrentMap ? Distance(player.CurrentLocation, pair.Npc.CurrentLocation) : 10000)
                .FirstOrDefault(pair => blocked?.Contains($"quest-flag/{progress.Index}/{pair.Npc.ObjectID}/{pair.Source!.Value.Flag}") != true);
            if (source.Npc != null)
                return new("service", source.Npc.CurrentMap.Info.Index, source.Npc.CurrentLocation.X, source.Npc.CurrentLocation.Y,
                    $"访问任务“{progress.Info.Name}”的标记地点：{source.Npc.Name}", progress.Index, source.Npc.ObjectID,
                    NextExit: routes[source.Npc.CurrentMap.Info.Index], Key: $"quest-flag/{progress.Index}/{source.Npc.ObjectID}/{source.Source!.Value.Flag}");
        }
        var available = player.CurrentQuests.Count < Globals.MaxConcurrentQuests
            ? npcs.SelectMany(npc => npc.Quests.Select(quest => (npc, quest)))
                .Where(pair => pair.npc.ObjectID == pair.quest.NpcIndex && HasFlagSources(pair.quest) &&
                    pair.quest.CanAccept(player) && !player.CompletedQuests.Contains(pair.quest.Index) &&
                    !player.CurrentQuests.Any(quest => quest.Index == pair.quest.Index) && blocked?.Contains(NpcGoal("accept",pair.quest,pair.npc).Key)!=true)
                .OrderBy(pair => pair.npc.CurrentMap == player.CurrentMap ? Distance(player.CurrentLocation, pair.npc.CurrentLocation) : 10000).Take(100).ToArray()
            : [];
        // 旧猎杀任务不能一直压住可完成的剧情交接；空白占位任务不据此抢占发育。
        var visit = available.OrderByDescending(pair => pair.quest.RequiredQuest > 0).FirstOrDefault(pair =>
            pair.quest.KillTasks.Count == 0 && pair.quest.ItemTasks.Count == 0 &&
            (pair.quest.ExpReward > 0 || pair.quest.GoldReward > 0 || pair.quest.FixedRewards.Count > 0 || pair.quest.SelectRewards.Count > 0 ||
                world.QuestInfoList.Any(next => next.RequiredQuest == pair.quest.Index)));
        if (visit.npc != null)
        { player.CheckQuestInfo(visit.quest); return NpcGoal("accept",visit.quest,visit.npc); }
        var hunts = new List<(KnownGoal Goal, long Remaining)>();
        foreach (var progress in player.CurrentQuests.Where(quest => !quest.Completed))
        {
            var kills = progress.KillTaskCount.Where(task => !task.Complete).SelectMany(task => KillSources(task.Info.Monster));
            var items = progress.ItemTaskCount.Where(task => !task.Complete).Select(task => task.ItemID).ToHashSet();
            var monsters = kills.Concat(DropSources(items)).Distinct().ToHashSet();
            var goal = Hunt(player, profile, maps, routes, monsters, progress.Index,
                $"任务“{progress.Info.Name}”{(items.Count > 0 ? "：寻找掉落任务物品的怪物" : "：击杀任务目标")}",blocked);
            if (goal != null) hunts.Add((goal,
                progress.KillTaskCount.Sum(task => Math.Max(0L, (long)task.Info.Count - task.Count)) +
                progress.ItemTaskCount.Sum(task => Math.Max(0L, (long)task.Info.Count - task.Count))));
        }
        // 先完成同图任务，再收尾剩余量少的任务；同分保留原顺序，避免随活怪坐标反复换任务。
        // shortcut: 剩余计数只近似工作量，跨图耗时与掉率收益出现实测瓶颈时再评估。
        var hunt = hunts.OrderBy(candidate => candidate.Goal.MapId != player.CurrentMap.Info.Index)
            .ThenBy(candidate => candidate.Remaining).Select(candidate => candidate.Goal).FirstOrDefault();
        if (hunt != null) return hunt;
        foreach (var (npc, quest) in available)
        {
                if (quest.KillTasks.Count == 0 && quest.ItemTasks.Count == 0 || Hunt(player, profile, maps, routes, QuestMonsters(quest).ToHashSet(), quest.Index, "预检任务目标",blocked) != null)
                { player.CheckQuestInfo(quest); return NpcGoal("accept", quest, npc); }
        }
        // 无当前用途的基础材料只在闲时补回，不能为凑储备抢占真实任务、造成跨图折返。
        if (CompanionStorage.Choose(player, npcs, routes, blocked, true) is { } retrieve) return retrieve;
        if (player.CurrentQuests.Any(quest=>quest.Completed&&!RewardsFit(player,quest.Info)))
            return new("wait",player.CurrentMap.Info.Index,player.CurrentLocation.X,player.CurrentLocation.Y,
                "已完成任务的物品奖励暂时放不下，保留任务并等待背包空间",Key:"quest/reward-space");
        return Hunt(player, profile, maps, routes, null, 0, "按当前战力寻找练级怪物",blocked);
    }

    private static bool RewardsFit(PlayerObject player,QuestInfo quest)
    {
        bool Fits(IEnumerable<QuestItemReward> rewards)
        {
            var items=new List<UserItem>();
            foreach(var reward in rewards)
            {
                if(reward.Item.StackSize==0)return false;
                for(var remaining=(int)reward.Count;remaining>0;remaining-=reward.Item.StackSize)
                {
                    var item=new UserItem(reward.Item){Count=(ushort)Math.Min(remaining,reward.Item.StackSize),
                        CurrentDura=reward.Item.Durability,MaxDura=reward.Item.Durability};
                    Envir.Main.UpdateItemExpiry(item);items.Add(item);
                    if(items.Count>player.Info.Inventory.Length+player.Info.QuestInventory.Length)return false;
                }
            }
            return player.CanGainItems(items.ToArray());
        }
        // shortcut: 执行组件独立选奖，全部选项都能容纳才安排交付；需按选项优化时再共用选择逻辑。
        return quest.SelectRewards.Count==0?Fits(quest.FixedRewards):quest.SelectRewards.All(reward=>Fits(quest.FixedRewards.Concat([reward])));
    }

    private static KnownGoal? Hunt(PlayerObject player, CompanionProfile profile, Map[] maps, Dictionary<int, KnownExit?> routes,
        HashSet<int>? wanted, int questId, string reason, ISet<string>? blocked, bool funds = false)
    {
        var world = Envir.Main;
        var training = wanted == null && !funds;
        var candidates = maps.Where(map => !map.Info.NoFight && (!training || !map.Info.NoExperience)).SelectMany(map => map.Info.Respawns.Where(spawn => spawn.Count > 0)
            .Select(spawn => (map, spawn, monster: world.GetMonsterInfo(spawn.MonsterIndex))))
            .Where(pair => pair.monster != null && CanHunt(player.Stats, player.Level, pair.monster, profile.Traits.Risk) &&
                (funds ? !pair.map.Info.NoDropMonster && CanFund(player, pair.monster) :
                    wanted != null ? wanted.Contains(pair.monster.Index) : pair.monster.Experience > 0))
            .OrderBy(pair => training && pair.map != player.CurrentMap ? 1 : 0)
            .ThenByDescending(pair => training ? TrainingValue(player, pair.monster,
                pair.map == player.CurrentMap ? Distance(player.CurrentLocation, pair.spawn.Location) : 10000) : 0)
            .ThenBy(pair => (pair.map == player.CurrentMap ? Distance(player.CurrentLocation, pair.spawn.Location) : 10000) +
                Math.Max(0, pair.monster.Level - player.Level) * 40).Take(32);
        foreach (var (map, spawn, monster) in candidates)
        {
            var key=$"{(funds ? "fund" : "hunt")}/{map.Info.Index}/{spawn.Location.X}/{spawn.Location.Y}/{monster.Index}/{questId}";
            if(blocked?.Contains(key)==true)continue;
            var live = world.Objects.OfType<MonsterObject>().Where(target => target.CurrentMap == map && !target.Dead && target.Master == null && target.Info.Index == monster.Index)
                .OrderBy(target => Distance(target.CurrentLocation, map == player.CurrentMap ? player.CurrentLocation : spawn.Location)).FirstOrDefault();
            var cells = live == null ? map.GetRespawnCells(spawn) : null;
            if (live == null && cells!.Count == 0) continue;
            var point = live?.CurrentLocation ?? cells!.OrderBy(cell => Distance(cell, spawn.Location)).First();
            if(funds&&!SafeFundingPoint(player,map,point))continue;
            return new("hunt", map.Info.Index, point.X, point.Y, $"{reason}：{monster.GameName}（{monster.Level}级，地图 {map.Info.Title}）",
                questId, MonsterIds: wanted?.Order().ToArray() ?? [monster.Index], NextExit: routes[map.Info.Index],Key:key);
        }
        return null;
    }

    private static int Distance(Point a, Point b) => Functions.MaxDistance(a, b);

    public static double TrainingValue(PlayerObject player, MonsterInfo monster, int distance)
    {
        var (hits, _) = CombatCost(player.Stats, player.Level, monster);
        // shortcut: 普攻耗时加直线赶路近似；不把此值当完整路线、法术或围攻收益。
        var seconds = hits * Math.Max(700, 1400 - player.Level * 14 - player.Stats[Stat.AttackSpeed] * 60) / 1000d + Math.Max(0, distance) * .5;
        return player.ReduceExp(monster.Experience, monster.Level) / Math.Max(1, seconds);
    }

    public static bool CanHunt(Stats player, int level, MonsterInfo monster, int risk)
    {
        if (monster.AI is 6 or 56 or 57 or 58 or 64 or 113 || monster.IsBoss || monster.Stats[Stat.HP] <= 0) return false;
        var (hits, loss) = CombatCost(player, level, monster);
        return hits <= 30 + risk / 3 && loss <= player[Stat.HP] * (50 + risk / 2d) / 100;
    }

    private static bool CanFund(PlayerObject player, MonsterInfo monster)
    {
        var (hits, loss) = CombatCost(player.Stats, player.Level, monster);
        // shortcut: 仅沿用单怪普攻成本作保守筛选，不保证复杂围攻或整条路线的收益。
        var drops = monster.AI is 1 or 2 or 4 or 5 or 7 or 9 or 28
            ? monster.Drops.Where(Server.MirObjects.Monsters.HarvestMonster.GroundSupply) : monster.Drops;
        return hits <= 8 && loss <= player.Stats[Stat.HP] / 10d && DropsGold(drops);
    }

    private static bool SafeFundingPoint(PlayerObject player,Map map,Point point)
    {
        // shortcut: 只检查目标周围已加载活怪，完整跨图沿途风险仍须按实际死亡继续完善。
        var loss=Envir.Main.Objects.OfType<MonsterObject>().Where(m=>m.CurrentMap==map&&!m.Dead&&!m.Hidden&&m.Master==null&&
            Distance(point,m.CurrentLocation)<=Math.Max(3,(int)m.Info.ViewRange))
            .Sum(m=>CombatCost(player.Stats,player.Level,m.Info).Loss);
        return loss<=player.Stats[Stat.HP]/10d;
    }

    // 首个成功即终止的分组保守只采信首项，后置金币可能永远被前项挡住。
    private static bool DropsGold(IEnumerable<DropInfo> drops, int depth = 0) => depth < 16 && drops.Any(drop =>
        drop.BaseProbability > 0 && !drop.QuestRequired && (drop.Gold > 1 || drop.GroupedDrop != null &&
            DropsGold(drop.GroupedDrop.First ? drop.GroupedDrop.Take(1) : drop.GroupedDrop, depth + 1)));

    private static (double Hits, double Loss) CombatCost(Stats player, int level, MonsterInfo monster)
    {
        // 原版鹿/稻草人的定义等级高于新手角色，按现有攻防和预计承伤判断，不能只比等级。
        var damage = (player[Stat.MinDC] + (double)player[Stat.MaxDC] - monster.Stats[Stat.MinAC] - monster.Stats[Stat.MaxAC]) / 2;
        // 原GetArmour在0至敏捷间取样；致命一击也必须先命中，不能用溢出伤害摊薄未命中成本。
        var hitChance = Math.Clamp((player[Stat.Accuracy] + 1d) / (Math.Max(0, monster.Stats[Stat.Agility]) + 1d), 0d, 1d);
        if (damage <= 0 || hitChance <= 0 || player[Stat.HP] <= 0) return (double.PositiveInfinity, double.PositiveInfinity);
        var hits = Math.Ceiling(Math.Ceiling(monster.Stats[Stat.HP] / damage) / hitChance);
        var interval = Math.Max(700, 1400 - level * 14 - player[Stat.AttackSpeed] * 60);
        var incoming = Math.Max(0, monster.Stats[Stat.MaxDC] - player[Stat.MinAC]);
        var spells = Math.Max(0, Math.Max(monster.Stats[Stat.MaxMC], monster.Stats[Stat.MaxSC]) - player[Stat.MinMAC]);
        var loss = Math.Ceiling(hits * interval / Math.Max(500d, monster.AttackSpeed)) * Math.Max(incoming, spells);
        return (hits, loss);
    }
}
