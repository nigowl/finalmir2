using Mir2.Companions;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionSupplies
{
    public static KnownGoal? Choose(PlayerObject player, CompanionProfile profile, NPCObject[] npcs,
        Dictionary<int, KnownExit?> routes, ISet<string>? blocked)
    {
        if (player.Account.Gold == 0 || !player.Info.Inventory.Any(item => item == null)) return null;
        // 回店路上也会消耗药品，节俭只能影响预算，不能等到只剩一瓶才返程。
        var hp = CompanionSupplyRules.NeedsRefill(player.Info.Inventory, Stat.HP, player.Class, profile.Traits.Frugality);
        var mp = CompanionSupplyRules.NeedsRefill(player.Info.Inventory, Stat.MP, player.Class, profile.Traits.Frugality);
        var town = !player.CurrentMap.Info.NoTownTeleport && CompanionSupplyRules.TownCount(player.Info.Inventory, player.Class, player.Gender, player.Level)==0;
        if (!hp && !mp && !town) return null;
        bool Eligible(NPCObject npc, UserItem item) => CompanionSupplyRules.Usable(item.Info, player.Class, player.Gender, player.Level) && Affordable(player, profile, npc, item);
        var vendors = npcs.Where(npc => blocked?.Contains($"supply/{npc.ObjectID}") != true)
            .Select(npc => (npc, script: NPCScript.Get(npc.ScriptID)))
            .Where(pair => pair.script.NPCPages.Any(page => page.Key.Equals(NPCScript.BuyKey,StringComparison.OrdinalIgnoreCase) || page.Key.Equals(NPCScript.BuySellKey,StringComparison.OrdinalIgnoreCase)))
            .Select(pair => (pair.npc, medicine:pair.script.Goods.Any(item =>
                (hp && CompanionSupplyRules.IsPotion(item, Stat.HP) || mp && CompanionSupplyRules.IsPotion(item, Stat.MP)) && Eligible(pair.npc,item)),
                scroll:town && pair.script.Goods.Any(item => CompanionSupplyRules.IsTownScroll(item) && Eligible(pair.npc,item))))
            .Where(pair => pair.medicine || pair.scroll)
            .OrderByDescending(pair => pair.medicine)
            .ThenBy(pair => pair.npc.CurrentMap == player.CurrentMap ? Functions.MaxDistance(player.CurrentLocation, pair.npc.CurrentLocation) : 10000);
        var selected=vendors.FirstOrDefault();var vendor=selected.npc;
        return vendor == null ? null : new("service", vendor.CurrentMap.Info.Index, vendor.CurrentLocation.X, vendor.CurrentLocation.Y,
            (selected.medicine ? "药品库存不足，前往补给：" : "缺少回城卷轴，前往储备1张：") + vendor.Name, NpcId: vendor.ObjectID,
            NextExit: routes[vendor.CurrentMap.Info.Index], Key: $"supply/{vendor.ObjectID}");
    }

    private static bool Affordable(PlayerObject player, CompanionProfile profile, NPCObject npc, UserItem goods)
    {
        var unit = goods.Clone(); unit.Count = 1;
        var price = unit.Price() * (double)NPCScript.PriceRate(player, npc);
        // shortcut: 受保护物品和非武器装备仍不自动减负，不能为购药绕过重量限制。
        return price > 0 && price <= player.Account.Gold / CompanionSupplyRules.BudgetDivisor(profile.Traits.Frugality) &&
            goods.Info.Weight <= player.Stats[Stat.BagWeight] - player.CurrentBagWeight;
    }
}
