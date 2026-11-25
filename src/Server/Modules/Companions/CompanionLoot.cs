using Mir2.Companions;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionLoot
{
    public static bool CanHarvest(PlayerObject player, MonsterObject monster)
    {
        if (monster is not Server.MirObjects.Monsters.HarvestMonster || !monster.Dead || monster.Harvested ||
            monster.EXPOwner != null && monster.EXPOwner != player && !player.IsMember(monster)) return false;
        var empty = player.Info.Inventory.Count(item => item == null);
        var limit = player.Stats[Stat.BagWeight];
        if (CompanionInventoryRules.CanHarvest(player.CurrentBagWeight, limit, empty, false)) return true;
        // 一项已收齐不代表整份任务完成，只能按此尸体真实掉落的未完成物品放行。
        var needed = player.CurrentQuests.Where(quest => !quest.Completed)
            .SelectMany(quest => quest.ItemTaskCount.Where(task => !task.Complete)).Select(task => task.ItemID).ToHashSet();
        return CompanionInventoryRules.CanHarvest(player.CurrentBagWeight, limit, empty,
            CompanionPlanning.CanDrop(monster.Info.Drops, needed));
    }

    // 原PickUp没有目标编号，必须先按同格原顺序找可拾取项，不能跳过重物选择其下的金币。
    public static ItemObject? FirstPickup(PlayerObject player, IEnumerable<MapObject> objects) => objects.OfType<ItemObject>()
        .FirstOrDefault(item => (item.Owner == null || item.Owner == player ||
            item.Owner.Race == ObjectType.Player && player.GroupMembers?.Any(member => member == item.Owner) == true) &&
            (item.Item == null ? player.CanGainGold(item.Gold) : player.CanGainItem(item.Item)));

    public static uint[] Targets(PlayerObject player, IEnumerable<MapObject> mapObjects)
    {
        var needed = player.CurrentQuests.SelectMany(quest => quest.ItemTaskCount.Where(task => !task.Complete)).Select(task => task.ItemID).ToHashSet();
        var positions = mapObjects.OfType<ItemObject>()
            .Where(item => Functions.InRange(item.CurrentLocation, player.CurrentLocation, Globals.DataRange))
            .Select(item => item.CurrentLocation).Distinct().OrderBy(point => Functions.MaxDistance(point, player.CurrentLocation)).Take(128);
        return positions.Select(point => FirstPickup(player, player.CurrentMap.GetCell(point).Objects))
            .Where(item => item != null && CompanionInventoryRules.CanCollect(player.CurrentBagWeight, player.Stats[Stat.BagWeight],
                player.Info.Inventory.Count(entry => entry == null), item.Item?.Weight ?? 0,
                item.Item != null && needed.Contains(item.Item.ItemIndex), item.Item == null))
            .Select(item => item!.ObjectID).ToArray();
    }
}
