using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionDevelopment
{
    public static KnownGoal? Choose(PlayerObject player, CompanionProfile profile, NPCObject[] npcs,
        Dictionary<int, KnownExit?> routes, ISet<string>? blocked, KnownGoal? previousGoal = null, bool hero = false)
    {
        if (player.Account.Gold == 0 || !player.Info.Inventory.Any(item => item == null)) return null;
        if (hero && (!player.HeroSpawned || player.Hero.Dead)) return null;
        var subject = hero ? player.Hero : (HumanObject)player;
        if (CompanionPublicServices.Choose(player, profile, blocked, true, hero) is { } publicDevelopment) return publicDevelopment;
        var publicItems = player.PublicShopOffers().Select(offer => Envir.Main.GetItemInfo(offer.ItemIndex)).ToArray();
        var learned = subject.Info.Magics.Select(m => m.Spell).ToHashSet();
        var distances = CompanionSupplies.RouteDistances(player, routes);
        var offers = npcs.Where(npc => distances.ContainsKey(npc.CurrentMap.Info.Index) && blocked?.Contains($"develop/{npc.ObjectID}") != true)
            .Select(npc => (npc, script: NPCScript.Get(npc.ScriptID)))
            .Where(pair => CompanionSupplies.Sells(pair.script))
            .SelectMany(pair => pair.script.Goods.Where(item =>
            {
                if (item.Info.Type is ItemType.Book or ItemType.Amulet && publicItems.Any(info => info.Index == item.ItemIndex ||
                    info.Type == ItemType.Amulet && item.Info.Type == ItemType.Amulet && info.Shape == item.Info.Shape)) return false;
                var missing = CompanionInventoryRules.DevelopmentCount(item, player.Info.Inventory, subject.Info.Equipment,
                    subject.Class, subject.Gender, subject.Level, learned);
                // 护符在原50张目标下用过半才专程返店，已打开货架仍可补满。
                if (missing == 0 || item.Info.Type == ItemType.Amulet && missing <= 25) return false;
                var unit = item.Clone(); unit.Count = 1;
                var price = unit.Price() * (double)NPCScript.PriceRate(player, pair.npc);
                if (CompanionSupplyRules.PurchaseLimit(item, price, player.Account.Gold, profile.Traits.Frugality,
                        CompanionSupplyRules.Count(player.Info.Inventory, Stat.HP)) == 0 ||
                    item.Info.UnitWeight > player.Stats[Stat.BagWeight] - player.CurrentBagWeight) return false;
                if (item.Info.Type == ItemType.Book)
                    return (!hero || CompanionHeroRules.Usable(item, subject.Class, subject.Gender, subject.Level)) &&
                        Envir.Main.MagicInfoList.Any(m => (int)m.Spell == item.Info.Shape && m.Level1 <= subject.Level);
                var slot = item.Info.Type == ItemType.Amulet ? (int)EquipmentSlot.Amulet :
                    CompanionInventoryRules.UpgradeSlot(item, subject.Info.Equipment, subject.Class, CompanionInventoryRules.Caster(learned));
                return slot >= 0 && (!hero || CompanionHeroRules.Usable(item, subject.Class, subject.Gender, subject.Level)) && subject.CanEquipItem(item, slot);
            }).Select(item => (pair.npc, item)))
            .OrderBy(pair => pair.item.Info.Type == ItemType.Book ? 0 : pair.item.Info.Type == ItemType.Amulet ? 1 : 2)
            // 已改走可用商店时不能因旧店90秒排除到期就折返，仍逐次核对需求、预算与可达资格。
            .ThenByDescending(pair => previousGoal?.Key == $"develop/{pair.npc.ObjectID}")
            .ThenBy(pair => distances[pair.npc.CurrentMap.Info.Index].Hops)
            .ThenBy(pair => Functions.MaxDistance(distances[pair.npc.CurrentMap.Info.Index].Entry, pair.npc.CurrentLocation))
            .ThenBy(pair => pair.item.Price());
        var selected = offers.FirstOrDefault();
        return selected.npc == null ? hero ? null : Choose(player, profile, npcs, routes, blocked, previousGoal, true) : new("service", selected.npc.CurrentMap.Info.Index,
            selected.npc.CurrentLocation.X, selected.npc.CurrentLocation.Y, $"主动发展，购买{(hero ? "英雄所需" : "")}{selected.item.FriendlyName}：{selected.npc.Name}",
            NpcId: selected.npc.ObjectID, NextExit: routes[selected.npc.CurrentMap.Info.Index], Key: $"develop/{selected.npc.ObjectID}");
    }
}
