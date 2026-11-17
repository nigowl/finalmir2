using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionStorage
{
    public static KnownGoal? Choose(PlayerObject player,NPCObject[] npcs,Dictionary<int,KnownExit?> routes,ISet<string>? blocked,bool take=false)
    {
        // 任务背包与普通背包语义不同，全版本任务物都不入自动仓库；配方材料仅暂存、不出售。
        var quests=Envir.Main.QuestInfoList.SelectMany(quest=>quest.ItemTasks.Concat(quest.CarryItems)).Select(task=>task.Item.Index).ToHashSet();
        var inventory=player.Info.Inventory;
        var full=CompanionInventoryRules.NeedsSpace(player.CurrentBagWeight,player.Stats[Stat.BagWeight],inventory.Count(item=>item==null));
        var storage=player.Account.Storage.Select((item,slot)=>(item,slot)).Where(pair=>player.Account.IsValidStorageIndex(pair.slot)).ToArray();
        KnownStorage? move=null;
        if(!take && full && storage.Any(pair=>pair.item==null))
        {
            var caster=player.Info.Magics.Any(magic=>magic.Info.Spell is Spell.FireBall or Spell.GreatFireBall or Spell.ThunderBolt or Spell.SoulFireBall);
            var item=CompanionInventoryRules.StorageUnused(inventory,player.Info.Equipment,quests,player.Class,player.Gender,player.Level,
                player.Info.Magics.Select(magic=>magic.Info.Spell).ToHashSet(),caster)
                .Concat(CompanionInventoryRules.StorageSurplus(inventory,quests,player.Level,storage.Count(pair=>pair.item==null))).FirstOrDefault();
            if(item!=null)move=new(item.UniqueID,item.Count,false);
        }
        if(take && !full && inventory.Count(item=>item==null)>2)
        {
            foreach(var pair in storage.Where(pair=>CompanionInventoryRules.StorageMaterial(pair.item,quests,player.Level)).OrderBy(pair=>pair.item.Count))
            {
                var item=pair.item;var missing=5-inventory.Where(i=>i?.ItemIndex==item.ItemIndex).Sum(i=>(int)i.Count);
                var room=Math.Max(0,player.Stats[Stat.BagWeight]*.8m-player.CurrentBagWeight)/item.Info.UnitWeight;
                var count=(ushort)Math.Min(Math.Min(Math.Max(0,missing),item.Count),room);
                if(count==0 || count<item.Count&&!storage.Any(p=>p.item==null))continue;
                move=new(item.UniqueID,count,true);break;
            }
        }
        if(move==null)return null;
        var vendor=npcs.Where(npc=>blocked?.Contains($"warehouse/{npc.ObjectID}")!=true &&
                NPCScript.Get(npc.ScriptID).NPCPages.Any(page=>page.Key.Equals(NPCScript.StorageKey,StringComparison.OrdinalIgnoreCase)))
            .OrderBy(npc=>npc.CurrentMap==player.CurrentMap?Functions.MaxDistance(player.CurrentLocation,npc.CurrentLocation):10000).FirstOrDefault();
        return vendor==null?null:new("service",vendor.CurrentMap.Info.Index,vendor.CurrentLocation.X,vendor.CurrentLocation.Y,
            (take?"取回基础材料储备：":"背包余量不足，自动存放闲置物品：")+vendor.Name,NpcId:vendor.ObjectID,
            NextExit:routes[vendor.CurrentMap.Info.Index],Key:$"warehouse/{vendor.ObjectID}",StorageItems:[move]);
    }
}
