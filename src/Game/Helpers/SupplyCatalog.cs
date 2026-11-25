namespace Server.Helpers;

public static class SupplyCatalog
{
    public static (List<ItemInfo> Added, List<(ItemInfo Item, ushort Count, byte Weight)> Resized) Prepare(IReadOnlyList<ItemInfo> items, int counter)
    {
        var added = new List<ItemInfo>();
        var resized = new List<(ItemInfo Item, ushort Count, byte Weight)>();
        var next = Math.Max(counter, items.Select(item => item.Index).DefaultIfEmpty().Max());
        foreach (var shape in new short[] { 0, 1, 2 })
        {
            var source = items.FirstOrDefault(item => item.Type == ItemType.Amulet && item.Shape == shape && item.Price > 0);
            if (source == null) continue;
            foreach (var (size, count) in PublicShopRules.AmuletSizes)
                Add(new ItemInfo { Name = $"{PublicShopRules.GroupName(source)}（{size}）", Type = ItemType.Amulet, Shape = shape,
                    Image = source.Image, Weight = (byte)(count / 100), StackSize = (ushort)count, Price = source.Price,
                    RequiredClass = source.RequiredClass, RequiredType = source.RequiredType, RequiredAmount = source.RequiredAmount,
                    RequiredGender = source.RequiredGender, ToolTip = "按施法消耗次数，装备在护符栏或手镯栏使用。" });
        }
        var art = items.FirstOrDefault(item => item.Type == ItemType.Stone && item.Image > 0) ?? items.FirstOrDefault(item => item.Type == ItemType.Torch);
        if (art != null)
            foreach (var (size, capacity) in new[] { ("小", 100_000), ("中", 300_000), ("大", 500_000) })
                Add(new ItemInfo { Name = $"魔血石（{size}）", Type = ItemType.Torch, Shape = 100, Image = art.Image, Weight = 10,
                    StackSize = 1, Durability = 1, Price = (uint)capacity, Stats = new Stats { [Stat.MagicBlood] = capacity },
                    Bind = BindMode.DontRepair | BindMode.NoSRepair | BindMode.DontUpgrade | BindMode.UnableToDisassemble,
                    ToolTip = "装备在火把栏；每2秒最多恢复30点生命和30点魔法，按实际恢复量共用储量。禁药地图、死亡及交易时暂停，满血满蓝不消耗。" });
        return (added, resized);

        void Add(ItemInfo item)
        {
            var existing = items.FirstOrDefault(existing => existing.Name == item.Name);
            if (existing != null)
            {
                var oldSize = item.Type == ItemType.Amulet && existing.StackSize == (item.StackSize switch { 500 => 100, 1000 => 300, 2000 => 500, _ => 0 });
                if (existing.Type != item.Type || existing.Shape != item.Shape || existing.StackSize != item.StackSize && !oldSize ||
                    existing.Stats[Stat.MagicBlood] != item.Stats[Stat.MagicBlood]) throw new InvalidDataException($"物品“{item.Name}”已有不同定义，未覆盖。");
                if (oldSize) resized.Add((existing, item.StackSize, item.Weight));
                return;
            }
            item.Index = checked(++next); added.Add(item);
        }
    }
}
