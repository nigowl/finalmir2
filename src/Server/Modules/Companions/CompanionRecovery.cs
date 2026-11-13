using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;
using Server.MirObjects.Monsters;

namespace Server.ConsoleHost;

public static class CompanionRecovery
{
    public static KnownGoal? Choose(PlayerObject player, Map[] maps, NPCObject[] npcs,
        Dictionary<int, KnownExit?> routes, ISet<string>? blocked)
    {
        var questItems = player.CurrentQuests.SelectMany(q => q.Info.ItemTasks.Concat(q.Info.CarryItems)).Select(t => t.Item.Index).ToHashSet();
        var meat = player.Info.Inventory.Any(item => CompanionInventoryRules.PlainMeat(item, questItems, player.Level));
        var vendor = npcs.Where(npc =>
        {
            var script = NPCScript.Get(npc.ScriptID);
            return meat ? script.Types.Contains(ItemType.Meat) : script.Goods.Any(item => item.Info.Type == ItemType.Weapon &&
                item.Info.RequiredType == RequiredType.Level && item.Info.RequiredAmount <= player.Level &&
                item.Info.RequiredClass.HasFlag((RequiredClass)(1 << (int)player.Class)) &&
                item.Info.RequiredGender.HasFlag((RequiredGender)(1 << (int)player.Gender)) &&
                item.Price() > 0 && item.Price() * 6L <= player.Account.Gold);
        }).OrderBy(npc => npc.CurrentMap == player.CurrentMap ? Functions.MaxDistance(player.CurrentLocation, npc.CurrentLocation) : 10000)
            .FirstOrDefault(npc => blocked?.Contains($"service/{npc.ObjectID}") != true);
        if (vendor != null)
            return new("service", vendor.CurrentMap.Info.Index, vendor.CurrentLocation.X, vendor.CurrentLocation.Y,
                meat ? "出售普通肉类，筹集补装资金：" + vendor.Name : "购买可负担的新手武器：" + vendor.Name,
                NpcId: vendor.ObjectID, NextExit: routes[vendor.CurrentMap.Info.Index], Key: $"service/{vendor.ObjectID}");
        if (meat) return null;
        var corpse = Envir.Main.Objects.OfType<HarvestMonster>().Where(monster => monster.Dead && !monster.Harvested &&
            monster.CurrentMap == player.CurrentMap && Functions.MaxDistance(player.CurrentLocation,monster.CurrentLocation)<=80 &&
            monster.Info.Drops.Any(drop=>!drop.QuestRequired&&drop.Item?.Type==ItemType.Meat) &&
            (monster.EXPOwner == null || monster.EXPOwner == player || player.IsMember(monster)))
            .OrderBy(monster => monster.CurrentMap == player.CurrentMap ? Functions.MaxDistance(player.CurrentLocation, monster.CurrentLocation) : 10000)
            .FirstOrDefault(monster => blocked?.Contains($"harvest/{monster.ObjectID}") != true);
        return corpse == null ? null : new("harvest", corpse.CurrentMap.Info.Index, corpse.CurrentLocation.X, corpse.CurrentLocation.Y,
            "暂缺武器，采集已解除归属保护的尸体筹集补装资金：" + corpse.Name,
            NextExit: routes[corpse.CurrentMap.Info.Index], Key: $"harvest/{corpse.ObjectID}");
    }

}
