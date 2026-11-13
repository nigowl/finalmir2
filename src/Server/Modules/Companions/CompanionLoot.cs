using Mir2.Companions;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionLoot
{
    // 原PickUp没有目标编号，必须先按同格原顺序找可拾取项，不能跳过重物选择其下的金币。
    public static ItemObject? FirstPickup(PlayerObject player, IEnumerable<MapObject> objects) => objects.OfType<ItemObject>()
        .FirstOrDefault(item => (item.Owner == null || item.Owner == player ||
            item.Owner.Race == ObjectType.Player && player.GroupMembers?.Any(member => member == item.Owner) == true) &&
            (item.Item == null ? player.CanGainGold(item.Gold) : player.CanGainItem(item.Item)));

    public static uint[] Targets(PlayerObject player)
    {
        var needed = player.CurrentQuests.SelectMany(quest => quest.ItemTaskCount.Where(task => !task.Complete)).Select(task => task.ItemID).ToHashSet();
        var positions = Server.MirEnvir.Envir.Main.Objects.OfType<ItemObject>()
            .Where(item => item.CurrentMap == player.CurrentMap && Functions.InRange(item.CurrentLocation, player.CurrentLocation, Globals.DataRange))
            .Select(item => item.CurrentLocation).Distinct().OrderBy(point => Functions.MaxDistance(point, player.CurrentLocation)).Take(128);
        return positions.Select(point => FirstPickup(player, player.CurrentMap.GetCell(point).Objects))
            .Where(item => item != null && CompanionInventoryRules.CanCollect(player.CurrentBagWeight, player.Stats[Stat.BagWeight],
                player.Info.Inventory.Count(entry => entry == null), item.Item?.Weight ?? 0,
                item.Item != null && needed.Contains(item.Item.ItemIndex), item.Item == null))
            .Select(item => item!.ObjectID).ToArray();
    }
}
