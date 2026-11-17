using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionKnowledgeReader
{
    public static CompanionKnowledge? Read(CompanionRecord record, ISet<string>? blocked = null, CompanionRecord[]? records = null, bool allowPk = false)
    {
        var world=Envir.Main;
        var player=world.CharacterList.FirstOrDefault(c=>c.Index==record.CharacterId)?.Player;
        if(player?.CurrentMap==null)return null;
        var map=player.CurrentMap;
        var npcs=map.NPCs.Where(n=>n.Visible).OrderBy(n=>Functions.MaxDistance(n.CurrentLocation,player.CurrentLocation)).Take(100).ToArray();
        var quests=player.CurrentQuests.Select(q=>q.Info).Concat(npcs.SelectMany(n=>n.Quests)).DistinctBy(q=>q.Index).Take(100).ToArray();
        var spawns=map.Info.Respawns.OrderBy(s=>Functions.MaxDistance(s.Location,player.CurrentLocation)).Take(100)
            .Select(s=>new{Spawn=s,Monster=world.GetMonsterInfo(s.MonsterIndex)}).Where(x=>x.Monster!=null)
            .Select(x=>new KnownSpawn(map.Info.Index,x.Monster.Index,x.Spawn.Location.X,x.Spawn.Location.Y,x.Monster.Level,x.Monster.GameName,x.Monster.Experience)).ToArray();
        var npcData=npcs.Select(n=>new KnownNpc(n.Info.Index,n.ObjectID,map.Info.Index,n.CurrentLocation.X,n.CurrentLocation.Y,n.Name,n.Quests.Select(q=>q.Index).ToArray(),
            NPCScript.Get(n.ScriptID).Types.Select(type=>(int)type).ToArray())).ToArray();
        var taskData=quests.Select(q=>new KnownQuest(q.Index,q.NpcIndex,q.FinishNpcIndex,CompanionPlanning.QuestMonsters(q),q.ItemTasks.Concat(q.CarryItems).Select(t=>t.Item.Index).Distinct().ToArray())).ToArray();
        var exits=CompanionPlanning.WalkingExits(map.Info).Take(500).Select(e=>new KnownExit(map.Info.Index,e.Source.X,e.Source.Y,e.MapIndex)).ToArray();
        var guild=CompanionGuildDevelopment.Read(player,record,records??[record],blocked,allowPk);
        var goal=CompanionPlanning.Choose(player,record.Profile,blocked,guild?.Goal);
        var party=CompanionParty.Read(player,record.Profile,records??[record]);
        if(party?.Goal is { } follow && (goal==null||goal.Kind is "hunt" or "wait" && !goal.Key.StartsWith("fund/",StringComparison.Ordinal)))goal=follow;
        var enemies=world.Objects.OfType<MonsterObject>().Where(monster=>monster.CurrentMap==map&&Functions.InRange(monster.CurrentLocation,player.CurrentLocation,Globals.DataRange))
            .OrderBy(monster=>Functions.MaxDistance(monster.CurrentLocation,player.CurrentLocation)).Take(256)
            .Select(monster=>new KnownEnemy(monster.ObjectID,monster.Info.Index,monster.Level,monster.Info.Experience,
                CompanionPlanning.CanHunt(player.Stats,player.Level,monster.Info,record.Profile.Traits.Risk),
                CompanionLoot.CanHarvest(player,monster))).ToArray();
        var loot=CompanionLoot.Targets(player);
        var revision=AdminChanges.Version(new{map.Info.Index,map.Info.NoFight,map.Info.NoDrug,map.Info.NoEscape,map.Info.NoTownTeleport,npcData,taskData,spawns,exits,goal,enemies,loot,
            guild,party,player.CurrentBagWeight,player.Account.Gold,inventory=player.Info.Inventory.Select(item=>item==null?"":$"{item.UniqueID}/{item.Count}")});
        return new(revision,map.Info.Index,map.Info.NoFight,map.Info.NoDrug,map.Info.NoEscape,npcData,taskData,spawns,exits,InSafeZone:player.InSafeZone,Goal:goal,Enemies:enemies,LootTargets:loot,NoTownTeleport:map.Info.NoTownTeleport,Guild:guild,Party:party);
    }
}
