using System.Drawing;
using Mir2.Companions;
using Server.MirEnvir;
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
        var publicItems = player.PublicShopOffers().Select(offer => Envir.Main.GetItemInfo(offer.ItemIndex))
            .Where(info => CompanionSupplyRules.Usable(info, player.Class, player.Gender, player.Level)).Select(info => new UserItem(info)).ToArray();
        // 商城已供应的耗材不再为便宜的NPC报价跨图；不足时先原地回收或低风险筹资。
        hp &= !publicItems.Any(item => CompanionSupplyRules.IsPotion(item, Stat.HP));
        mp &= !publicItems.Any(item => CompanionSupplyRules.IsPotion(item, Stat.MP));
        town &= !publicItems.Any(CompanionSupplyRules.IsTownScroll);
        if (!hp && !mp && !town) return null;
        var distances = RouteDistances(player, routes);
        bool Eligible(NPCObject npc, UserItem item) => CompanionSupplyRules.Usable(item.Info, player.Class, player.Gender, player.Level) && Affordable(player, profile, npc, item);
        var vendors = npcs.Where(npc => distances.ContainsKey(npc.CurrentMap.Info.Index) && blocked?.Contains($"supply/{npc.ObjectID}") != true)
            .Select(npc => (npc, script: NPCScript.Get(npc.ScriptID)))
            .Where(pair => Sells(pair.script))
            .Select(pair => (pair.npc, medicine:pair.script.Goods.Any(item =>
                (hp && CompanionSupplyRules.IsPotion(item, Stat.HP) || mp && CompanionSupplyRules.IsPotion(item, Stat.MP)) && Eligible(pair.npc,item)),
                scroll:town && pair.script.Goods.Any(item => CompanionSupplyRules.IsTownScroll(item) && Eligible(pair.npc,item))))
            .Where(pair => pair.medicine || pair.scroll)
            .OrderByDescending(pair => pair.medicine)
            .ThenBy(pair => distances[pair.npc.CurrentMap.Info.Index].Hops)
            .ThenBy(pair => Functions.MaxDistance(distances[pair.npc.CurrentMap.Info.Index].Entry, pair.npc.CurrentLocation));
        var selected=vendors.FirstOrDefault();var vendor=selected.npc;
        return vendor == null ? null : new("service", vendor.CurrentMap.Info.Index, vendor.CurrentLocation.X, vendor.CurrentLocation.Y,
            (selected.medicine ? "药品库存不足，前往补给：" : "缺少回城卷轴，前往储备1张：") + vendor.Name, NpcId: vendor.ObjectID,
            NextExit: routes[vendor.CurrentMap.Info.Index], Key: $"supply/{vendor.ObjectID}");
    }

    internal static Dictionary<int, (int Hops, Point Entry)> RouteDistances(PlayerObject player, Dictionary<int, KnownExit?> routes)
    {
        var origin = player.CurrentMap.Info.Index;
        var maps = Envir.Main.MapInfoList.Where(map => routes.ContainsKey(map.Index)).DistinctBy(map => map.Index).ToDictionary(map => map.Index);
        var distances = new Dictionary<int, (int Hops, Point Entry)> { [origin] = (0, player.CurrentLocation) };
        var queue = new Queue<int>(); queue.Enqueue(origin);
        // shortcut: 沿原公开BFS比较跨图数及入口直线距离，实测仍绕路时再评估完整图内成本。
        while (queue.TryDequeue(out var id))
        {
            if (!maps.TryGetValue(id, out var map)) continue;
            foreach (var exit in CompanionPlanning.WalkingExits(map))
                if (routes.ContainsKey(exit.MapIndex) && distances.TryAdd(exit.MapIndex, (distances[id].Hops + 1, exit.Destination)))
                    queue.Enqueue(exit.MapIndex);
        }
        return distances;
    }

    public static bool NeedsFunds(PlayerObject player, CompanionProfile profile, NPCObject[] npcs)
    {
        if (!CompanionSupplyRules.NeedsRefill(player.Info.Inventory, Stat.HP, player.Class, profile.Traits.Frugality) ||
            !player.Info.Inventory.Any(item => item == null)) return false;
        var publicPrices = player.PublicShopOffers().Where(offer => Envir.Main.GetItemInfo(offer.ItemIndex) is { } info &&
                CompanionSupplyRules.IsPotion(new UserItem(info), Stat.HP) &&
                CompanionSupplyRules.Usable(info, player.Class, player.Gender, player.Level) &&
                info.UnitWeight <= player.Stats[Stat.BagWeight] - player.CurrentBagWeight)
            .Select(offer => (double)offer.Price).ToArray();
        var cheapest = publicPrices.Length > 0 ? publicPrices.Min() : npcs.Select(npc => (npc, script: NPCScript.Get(npc.ScriptID))).Where(pair => Sells(pair.script))
            .SelectMany(pair => pair.script.Goods.Where(item => CompanionSupplyRules.IsPotion(item, Stat.HP) &&
                CompanionSupplyRules.Usable(item.Info, player.Class, player.Gender, player.Level) &&
                item.Info.UnitWeight <= player.Stats[Stat.BagWeight] - player.CurrentBagWeight).Select(item => Price(player, pair.npc, item)))
            .Where(price => price > 0 && double.IsFinite(price)).DefaultIfEmpty(double.PositiveInfinity).Min();
        // 最低一瓶采购仍在本判断之前；其余沿用人格预算，不硬编码金币门槛。
        return double.IsFinite(cheapest) && cheapest > player.Account.Gold / CompanionSupplyRules.BudgetDivisor(profile.Traits.Frugality);
    }

    internal static bool Sells(NPCScript script) => script.NPCPages.Any(page =>
        page.Key.Equals(NPCScript.BuyKey, StringComparison.OrdinalIgnoreCase) || page.Key.Equals(NPCScript.BuySellKey, StringComparison.OrdinalIgnoreCase));

    private static double Price(PlayerObject player, NPCObject npc, UserItem goods)
    {
        var unit = goods.Clone(); unit.Count = 1;
        return unit.Price() * (double)NPCScript.PriceRate(player, npc);
    }

    private static bool Affordable(PlayerObject player, CompanionProfile profile, NPCObject npc, UserItem goods)
    {
        // shortcut: 受保护物品和非武器装备仍不自动减负，不能为购药绕过重量限制。
        return CompanionSupplyRules.PurchaseLimit(goods, Price(player, npc, goods), player.Account.Gold, profile.Traits.Frugality,
            CompanionSupplyRules.Count(player.Info.Inventory, Stat.HP)) > 0 &&
            goods.Info.UnitWeight <= player.Stats[Stat.BagWeight] - player.CurrentBagWeight;
    }
}
