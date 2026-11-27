#nullable enable
namespace Mir2.Companions;

public static class CompanionSupplyRules
{
    public static bool IsPotion(UserItem? item, Stat stat) => item is { Count: > 0, Info.Type: ItemType.Potion, Info.Shape: 0 or 1 } && item.Info.Stats[stat] > 0;

    public static int Count(IEnumerable<UserItem?> items, Stat stat) => items.Where(item => IsPotion(item, stat)).Sum(item => (int)item!.Count);

    public static bool IsTownScroll(UserItem? item) => item is { Count: > 0, Info.Type: ItemType.Scroll, Info.Shape: 1 };

    public static int TownCount(IEnumerable<UserItem?> items, MirClass role, MirGender gender, int level) => items
        .Where(item => IsTownScroll(item) && Usable(item!.Info, role, gender, level) &&
            (item.RentalInformation == null || item.RentalInformation.BindingFlags == BindMode.None)).Sum(item => (int)item!.Count);

    public static int Target(Stat stat, MirClass role, int frugality, int level = int.MaxValue)
    {
        var full = stat == Stat.MP ? role == MirClass.Warrior ? 0 : Math.Max(6, 10 - frugality / 25) : Math.Max(12, 20 - frugality / 10);
        // 新手区持续掉红蓝，不能要求先攒齐地牢储备才肯做入门任务或换装。
        return Math.Min(full, level < 10 ? 4 : level < 20 ? 8 : full);
    }

    public static bool NeedsRefill(IEnumerable<UserItem?> items, Stat stat, MirClass role, int frugality, int level = int.MaxValue) =>
        Count(items, stat) < (Target(stat, role, frugality, level) + 1) / 2;

    public static double BudgetDivisor(int frugality) => 2d + frugality / 25d;

    public static int PurchaseLimit(UserItem item, double price, uint gold, int frugality, int hpPotions)
    {
        if (!(price > 0) || price > gold) return 0;
        if (MagicBloodStone.IsStone(item.Info)) return price <= gold / 2d ? 1 : 0;
        if (price <= gold / BudgetDivisor(frugality))
            return (int)Math.Min(ushort.MaxValue, Math.Floor(gold / (price * 2)));
        // 无红药时允许先保留一瓶，不能因节俭拿着买药钱继续裸补给猎杀。
        return hpPotions == 0 && IsPotion(item, Stat.HP) ? 1 : 0;
    }

    public static bool NeedsRepair(UserItem? item, uint gold) => item?.Info != null && item.MaxDura > 0 &&
        item.CurrentDura < item.MaxDura / 2 && !item.Info.Bind.HasFlag(BindMode.DontRepair) && item.RepairPrice() < gold / 2;

    public static bool Usable(ItemInfo info, MirClass role, MirGender gender, int level) =>
        info.RequiredType == RequiredType.Level && info.RequiredAmount <= level &&
        info.RequiredClass.HasFlag((RequiredClass)(1 << (int)role)) && info.RequiredGender.HasFlag((RequiredGender)(1 << (int)gender));
}
