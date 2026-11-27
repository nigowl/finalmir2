#nullable enable
namespace Mir2.Companions;

public static class CompanionPublicShopRules
{
    public static (PublicShopEntry Offer, ushort Count)? GuildMaterial(IEnumerable<PublicShopEntry> offers, Func<int,ItemInfo?> find,
        int itemId, int required, long reserve, uint gold, decimal room, int owned = 0)
    {
        var offer = offers.FirstOrDefault(o => o.ItemIndex == itemId);
        var info = find(itemId);
        var missing = required-owned;
        if (offer == null || info == null || info.Type != ItemType.CraftingMaterial || missing <= 0 || owned < 0 || reserve < 0 ||
            offer.Price == 0 || gold < reserve || PublicShopRules.UnitCount(info) != 1) return null;
        var count = Math.Min(missing, Math.Min(offer.MaxCount, Math.Min(info.StackSize, (gold-reserve)/offer.Price)));
        if (info.UnitWeight > 0) count = (long)Math.Min(count, Math.Max(0,room)/info.UnitWeight);
        return count > 0 ? (offer,(ushort)count) : null;
    }
    public static (PublicShopEntry Offer, ushort Count)? Choose(IEnumerable<PublicShopEntry> offers, Func<int, ItemInfo?> find,
        UserItem?[] inventory, UserItem?[] equipment, MirClass role, MirGender gender, int level, ISet<Spell> learned,
        uint gold, decimal room, int frugality, bool townAllowed, bool development = false)
    {
        if (!inventory.Any(item => item == null)) return null;
        var hp = CompanionSupplyRules.Count(inventory, Stat.HP);
        var choices = new List<(PublicShopEntry Offer, ushort Count, int Priority, double Value)>();
        foreach (var offer in offers)
        {
            var info = find(offer.ItemIndex);
            if (info == null || offer.Price == 0 || !CompanionSupplyRules.Usable(info, role, gender, level)) continue;
            var unit = PublicShopRules.UnitCount(info);
            var item = new UserItem(info) { Count = unit, CurrentDura = info.Durability, MaxDura = info.Durability };
            int missing, priority;
            if (MagicBloodStone.IsStone(info))
            {
                if(hp==0)continue;
                missing=CompanionInventoryRules.DevelopmentCount(item,inventory,equipment,role,gender,level,learned);priority=-1;
            }
            else if (!development && CompanionSupplyRules.IsPotion(item, Stat.HP) && CompanionSupplyRules.NeedsRefill(inventory, Stat.HP, role, frugality, level))
            { missing = CompanionSupplyRules.Target(Stat.HP, role, frugality, level) - hp; priority = hp == 0 ? -2 : 0; }
            else if (!development && CompanionSupplyRules.IsPotion(item, Stat.MP) && CompanionSupplyRules.NeedsRefill(inventory, Stat.MP, role, frugality, level))
            { missing = CompanionSupplyRules.Target(Stat.MP, role, frugality, level) - CompanionSupplyRules.Count(inventory, Stat.MP); priority = 1; }
            else if (!development && townAllowed && CompanionSupplyRules.IsTownScroll(item))
            { missing = 1 - CompanionSupplyRules.TownCount(inventory, role, gender, level); priority = 2; }
            else if (development && info.Type is ItemType.Book or ItemType.Amulet)
            {
                if (CompanionSupplyRules.NeedsRefill(inventory, Stat.HP, role, frugality, level)) continue;
                missing = CompanionInventoryRules.DevelopmentCount(item, inventory, equipment, role, gender, level, learned);
                if (info.Type == ItemType.Amulet && missing <= 25) continue;
                priority = info.Type == ItemType.Book ? 3 : 4;
            }
            else continue;
            if (missing <= 0) continue;
            // 报价和数量按整包结算，不能将500次护符的缺额当成500包。
            var needed = (missing + unit - 1) / unit;
            var limit = CompanionSupplyRules.PurchaseLimit(item, offer.Price, gold, frugality, hp);
            var count = Math.Min(Math.Min(needed, offer.MaxCount), Math.Min(limit, info.StackSize / unit));
            if (item.Weight > 0) count = (int)Math.Min(count, Math.Max(0, room) / item.Weight);
            if (count > 0) choices.Add((offer, (ushort)count, priority,
                (double)offer.Price / Math.Max(1, Math.Max(info.Stats[Stat.MagicBlood],Math.Max(info.Stats[Stat.HP], info.Stats[Stat.MP])))));
        }
        var best = choices.OrderBy(choice => choice.Priority).ThenBy(choice => choice.Value).ThenBy(choice => choice.Offer.Price).FirstOrDefault();
        return best.Count > 0 ? (best.Offer, best.Count) : null;
    }
}
