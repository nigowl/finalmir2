#nullable enable
namespace Mir2.Companions;

public static class CompanionInventoryRules
{
    public static bool NeedsSpace(int weight, int limit, int emptySlots) => emptySlots <= 2 || limit > 0 && weight * 5L >= limit * 4L;

    public static bool CanCollect(int weight, int limit, int emptySlots, int addedWeight, bool needed, bool gold) =>
        gold || weight >= 0 && addedWeight >= 0 && limit > 0 && (long)weight + addedWeight <= limit &&
        (needed || emptySlots > 2 && ((long)weight + addedWeight) * 5 <= limit * 4L);

    public static bool CanHarvest(int weight, int limit, int emptySlots, bool needed) =>
        weight >= 0 && limit > 0 && emptySlots > 0 && (needed || !NeedsSpace(weight, limit, emptySlots));

    public static bool PlainWeapon(UserItem? item, ISet<int> protectedItems, int level) =>
        item?.Info?.Type == ItemType.Weapon && item.Count == 1 && PlainItem(item, protectedItems, level);

    public static bool PlainMeat(UserItem? item, ISet<int> protectedItems, int level) =>
        item?.Info?.Type == ItemType.Meat && item.Count <= item.Info.StackSize && PlainItem(item, protectedItems, level);

    private static bool PlainItem(UserItem? item, ISet<int> protectedItems, int level) =>
        item?.Info is { } info && info.RequiredType == RequiredType.Level && info.RequiredAmount <= level &&
        info.Grade <= ItemGrade.Common && info.Set == ItemSet.None && info.Unique == SpecialItemMode.None &&
        info.Bind == BindMode.None && !info.CanMine && !info.IsFishingRod && !info.NeedIdentify && info.Slots == 0 &&
        item.Count > 0 && item.Price() > 0 && !protectedItems.Contains(item.ItemIndex) &&
        item.SoulBoundId < 0 && item.WeddingRing < 0 && !item.GMMade && !item.Cursed &&
        item.RentalInformation == null && item.ExpireInfo == null && item.SealedInfo == null &&
        item.AddedStats.Count == 0 && item.GemCount == 0 && item.RefinedValue == RefinedValue.None && item.RefineAdded == 0 && item.RefineSuccessChance == 0 &&
        item.Awake.Type == AwakeType.None && item.Awake.GetAwakeLevel() == 0 && item.Slots.Length == 0;

    public static KnownSale[] SurplusMaterials(IEnumerable<UserItem?> inventory, ISet<int> protectedItems, int level)
    {
        var result = new List<KnownSale>();
        foreach (var group in inventory.Where(item => item?.Info?.Type is ItemType.CraftingMaterial or ItemType.Food)
            .Select(item => item!).Where(item => item.Weight > 0 && PlainItem(item, protectedItems, level)).GroupBy(item => item.ItemIndex))
        {
            var reserve = 5;
            foreach (var item in group)
            {
                var keep = Math.Min(reserve, item.Count); reserve -= keep;
                if (item.Count > keep) result.Add(new(item.UniqueID, (ushort)(item.Count - keep)));
            }
        }
        return result.ToArray();
    }

    public static UserItem[] SurplusArmour(IEnumerable<UserItem?> inventory, UserItem? equipped, ISet<int> protectedItems, int level)
    {
        if(equipped?.Info?.Type!=ItemType.Armour)return [];
        // 只出售同一定义的普通重复衣服，每种仍留耐久最好的一件，不推断不同属性之间的优劣。
        return inventory.Where(item=>item?.Info?.Type==ItemType.Armour&&item.UniqueID!=equipped.UniqueID&&item.Count==1&&
                item.MaxDura==item.Info.Durability&&PlainItem(item,protectedItems,level))
            .Select(item=>item!).GroupBy(item=>item.ItemIndex)
            .SelectMany(group=>group.OrderByDescending(item=>item.CurrentDura).Skip(1))
            .OrderByDescending(item=>item.Weight).ToArray();
    }

    public static UserItem[] SurplusWeapons(IEnumerable<UserItem?> inventory, UserItem? equipped,
        MirClass role, MirGender gender, int level, bool caster, ISet<int> protectedItems)
    {
        if (equipped?.Info?.Type != ItemType.Weapon) return [];
        var items = inventory.Where(item => item?.Info?.Type == ItemType.Weapon).Select(item => item!).ToArray();
        bool Usable(UserItem item) => item.CurrentDura > 0 && CompanionSupplyRules.Usable(item.Info, role, gender, level);
        var attack = caster && role == MirClass.Wizard ? Stat.MaxMC : caster && role == MirClass.Taoist ? Stat.MaxSC : Stat.MaxDC;
        int Value(UserItem item) => item.Info.Stats[attack] + item.AddedStats[attack];
        // 备装也可能是任务/强化物；先在全部可用武器中保留，再筛选能出售的普通余货。
        var backup = items.Where(Usable).OrderByDescending(Value).ThenByDescending(item => item.CurrentDura).FirstOrDefault();
        return items.Where(item => item.UniqueID != equipped.UniqueID && item != backup &&
            PlainWeapon(item, protectedItems, level) && (!Usable(item) || Value(item) <= Value(equipped)))
            .OrderByDescending(item => item.Weight).ToArray();
    }
}
