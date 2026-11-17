using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionDevelopment
{
    public static KnownGoal? Choose(PlayerObject player, CompanionProfile profile, NPCObject[] npcs,
        Dictionary<int, KnownExit?> routes, ISet<string>? blocked)
    {
        if (player.Account.Gold == 0 || !player.Info.Inventory.Any(item => item == null)) return null;
        var learned = player.Info.Magics.Select(m => m.Spell).ToHashSet();
        var distances = CompanionSupplies.RouteDistances(player, routes);
        var offers = npcs.Where(npc => distances.ContainsKey(npc.CurrentMap.Info.Index) && blocked?.Contains($"develop/{npc.ObjectID}") != true)
            .Select(npc => (npc, script: NPCScript.Get(npc.ScriptID)))
            .Where(pair => CompanionSupplies.Sells(pair.script))
            .SelectMany(pair => pair.script.Goods.Where(item =>
            {
                if (CompanionInventoryRules.DevelopmentCount(item, player.Info.Inventory, player.Info.Equipment,
                    player.Class, player.Gender, player.Level, learned) == 0) return false;
                var unit = item.Clone(); unit.Count = 1;
                var price = unit.Price() * (double)NPCScript.PriceRate(player, pair.npc);
                if (CompanionSupplyRules.PurchaseLimit(item, price, player.Account.Gold, profile.Traits.Frugality,
                        CompanionSupplyRules.Count(player.Info.Inventory, Stat.HP)) == 0 ||
                    item.Info.UnitWeight > player.Stats[Stat.BagWeight] - player.CurrentBagWeight) return false;
                if (item.Info.Type == ItemType.Book)
                    return Envir.Main.MagicInfoList.Any(m => (int)m.Spell == item.Info.Shape && m.Level1 <= player.Level);
                var slot = item.Info.Type == ItemType.Amulet ? (int)EquipmentSlot.Amulet :
                    CompanionInventoryRules.UpgradeSlot(item, player.Info.Equipment, player.Class, CompanionInventoryRules.Caster(learned));
                return slot >= 0 && player.CanEquipItem(item, slot);
            }).Select(item => (pair.npc, item)))
            .OrderBy(pair => pair.item.Info.Type == ItemType.Book ? 0 : pair.item.Info.Type == ItemType.Amulet ? 1 : 2)
            .ThenBy(pair => distances[pair.npc.CurrentMap.Info.Index].Hops)
            .ThenBy(pair => Functions.MaxDistance(distances[pair.npc.CurrentMap.Info.Index].Entry, pair.npc.CurrentLocation))
            .ThenBy(pair => pair.item.Price());
        var selected = offers.FirstOrDefault();
        return selected.npc == null ? null : new("service", selected.npc.CurrentMap.Info.Index,
            selected.npc.CurrentLocation.X, selected.npc.CurrentLocation.Y, $"主动发展，购买{selected.item.FriendlyName}：{selected.npc.Name}",
            NpcId: selected.npc.ObjectID, NextExit: routes[selected.npc.CurrentMap.Info.Index], Key: $"develop/{selected.npc.ObjectID}");
    }
}
