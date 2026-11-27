using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionKnowledgeReader
{
    public static CompanionKnowledge? Read(CompanionRecord record, ISet<string>? blocked = null, CompanionRecord[]? records = null, bool allowPk = false, KnownGoal? previousGoal = null, Lazy<ILookup<Map, MapObject>>? objectsByMap = null, Dictionary<int, KnownQuest>? taskDefinitions = null, string guildPreference = "", List<KnownGoal>? taskChoices = null, int preferredQuest = 0, IReadOnlyDictionary<Guid,string>? guildPreferences = null)
    {
        var world=Envir.Main;
        var player=world.CharacterList.FirstOrDefault(c=>c.Index==record.CharacterId)?.Player;
        if(player?.CurrentMap==null)return null;
        var map=player.CurrentMap;
        objectsByMap ??= new(() => world.Objects.ToLookup(entity => entity.CurrentMap));
        var npcs=map.NPCs.Where(n=>n.Visible).OrderBy(n=>Functions.MaxDistance(n.CurrentLocation,player.CurrentLocation)).Take(100).ToArray();
        var quests=player.CurrentQuests.Select(q=>q.Info).Concat(npcs.SelectMany(n=>n.Quests)).DistinctBy(q=>q.Index).Take(100).ToArray();
        var spawns=map.Info.Respawns.OrderBy(s=>Functions.MaxDistance(s.Location,player.CurrentLocation)).Take(100)
            .Select(s=>new{Spawn=s,Monster=world.GetMonsterInfo(s.MonsterIndex)}).Where(x=>x.Monster!=null)
            .Select(x=>new KnownSpawn(map.Info.Index,x.Monster.Index,x.Spawn.Location.X,x.Spawn.Location.Y,x.Monster.Level,x.Monster.GameName,x.Monster.Experience)).ToArray();
        var npcData=npcs.Select(n=>new KnownNpc(n.Info.Index,n.ObjectID,map.Info.Index,n.CurrentLocation.X,n.CurrentLocation.Y,n.Name,n.Quests.Select(q=>q.Index).ToArray(),
            NPCScript.Get(n.ScriptID).Types.Select(type=>(int)type).ToArray())).ToArray();
        taskDefinitions ??= new();
        var taskData = quests.Select(quest =>
        {
            if (!taskDefinitions.TryGetValue(quest.Index, out var task))
                taskDefinitions[quest.Index] = task = new(quest.Index, quest.NpcIndex, quest.FinishNpcIndex,
                    CompanionPlanning.QuestMonsters(quest), quest.ItemTasks.Concat(quest.CarryItems).Select(t => t.Item.Index).Distinct().ToArray());
            return task;
        }).ToArray();
        var exits=CompanionPlanning.WalkingExits(map.Info).Take(500).Select(e=>new KnownExit(map.Info.Index,e.Source.X,e.Source.Y,e.MapIndex)).ToArray();
        var guild=CompanionGuildDevelopment.Read(player,record,records??[record],blocked,allowPk,guildPreference,guildPreferences);
        var goal=CompanionPlanning.Choose(player,record.Profile,blocked,guild?.Goal,previousGoal,objectsByMap,taskChoices,preferredQuest);
        var party=CompanionParty.Read(player,record.Profile,records??[record]);
        if(party?.Goal is { } follow && (goal==null||goal.Kind is "hunt" or "wait" &&
            goal.QuestId==0&&
            !goal.Key.StartsWith("fund/",StringComparison.Ordinal)&&!goal.Key.StartsWith("supply/",StringComparison.Ordinal)&&guild?.Action!="prepare"))goal=follow;
        // 守卫是否威胁本人沿用原攻击权限，不能把友好守卫改成可猎杀目标来绕过避险。
        var enemies=objectsByMap.Value[map].OfType<MonsterObject>().Where(monster=>(monster.Dead||player.IsAttackTarget(monster))&&
                Functions.InRange(monster.CurrentLocation,player.CurrentLocation,Globals.DataRange))
            .OrderBy(monster=>Functions.MaxDistance(monster.CurrentLocation,player.CurrentLocation)).Take(256)
            .Select(monster=>new KnownEnemy(monster.ObjectID,monster.Info.Index,monster.Level,monster.Info.Experience,
                CompanionPlanning.CanHunt(player.Stats,player.Level,monster.Info,record.Profile.Traits.Risk),
                CompanionLoot.CanHarvest(player,monster))).ToArray();
        var loot=CompanionLoot.Targets(player, objectsByMap.Value[map]);
        var townOnRoute = goal is { Kind: "accept" or "turn-in" or "service", QuestId: > 0 } &&
            CanUseTownOnRoute(player, goal);
        var revision=AdminChanges.Version(new{map.Info.Index,map.Info.NoFight,map.Info.NoDrug,map.Info.NoEscape,map.Info.NoTownTeleport,npcData,taskData,spawns,exits,goal,enemies,loot,
            guild,party,map.Info.NoRandom,townOnRoute,map.Info.NoHero,player.InSafeZone,player.CurrentBagWeight,player.Account.Gold,inventory=player.CarriedItems.Select(item=>item==null?"":$"{item.UniqueID}/{item.Count}")});
        return new(revision,map.Info.Index,map.Info.NoFight,map.Info.NoDrug,map.Info.NoEscape,npcData,taskData,spawns,exits,InSafeZone:player.InSafeZone,Goal:goal,Enemies:enemies,LootTargets:loot,NoTownTeleport:map.Info.NoTownTeleport,Guild:guild,Party:party,
            NoRandom:map.Info.NoRandom,TownOnRoute:townOnRoute,NoHero:map.Info.NoHero);
    }

    private static bool CanUseTownOnRoute(PlayerObject player, KnownGoal goal)
    {
        if (player.InSafeZone || player.CurrentMap.Info.NoTownTeleport || goal.MapId != player.BindMapIndex) return false;
        var town = Envir.Main.GetMap(player.BindMapIndex);
        if (town == null || town.Info.RequiredGroup || town.GetBindLocation(player.BindLocation) is not { } bind || town.GetSafeZone(bind) == null)
            return false;
        var npc = new System.Drawing.Point(goal.X, goal.Y);
        return player.CurrentMap != town || Functions.MaxDistance(player.CurrentLocation, npc) - Functions.MaxDistance(bind, npc) >= 64;
    }
}
