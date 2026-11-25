using Server.MirDatabase;

namespace Server.Helpers;

public static class HeroInventory
{
    public static UserItem[] Restore(CharacterInfo owner, HeroInfo hero)
    {
        var items = hero.Inventory.Where(item => item != null).ToArray();
        if (items.Length == 0) { hero.Inventory = Array.Empty<UserItem>(); return items; }
        var ids = owner.Inventory.Concat(owner.Equipment).Where(item => item != null).Select(item => item.UniqueID).ToHashSet();
        if (items.Any(item => !ids.Add(item.UniqueID)))
            throw new InvalidDataException("英雄旧背包存在重复物品编号，已阻止迁移。");

        var start = Math.Min(6, owner.Inventory.Length);
        var missing = items.Length - owner.Inventory.Skip(start).Count(item => item == null);
        // 旧包满时仅扩展承接所需格位，保留每件物品及编号，不堆叠或丢弃。
        if (missing > 0) Array.Resize(ref owner.Inventory, owner.Inventory.Length + missing);
        foreach (var item in items)
        {
            var slot = Array.FindIndex(owner.Inventory, start, value => value == null);
            owner.Inventory[slot] = item;
        }
        // 保留原存档字段布局，之后只在主人快照保存背包。
        hero.Inventory = Array.Empty<UserItem>();
        return items;
    }
}
