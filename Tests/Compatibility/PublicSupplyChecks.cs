using Crystal.Native;
using Server.Helpers;

internal static class PublicSupplyChecks
{
    public static void Run(Action<bool, string> check)
    {
        var originals = new List<ItemInfo>();
        foreach (var shape in new short[] { 0, 1, 2 })
            originals.Add(new ItemInfo { Index = shape + 1, Name = "旧耗材" + shape, Type = ItemType.Amulet, Shape = shape, Price = 2, StackSize = 500 });
        originals.Add(new ItemInfo { Index = 4, Name = "旧石头", Type = ItemType.Stone, Image = 1 });
        var added = SupplyCatalog.Prepare(originals, 10).Added;
        check(added.Count == 12 && added.Select(item => item.Index).Distinct().Count() == 12 && added.Min(item => item.Index) == 11,
            "补给规格缺失或覆盖旧编号");
        foreach (var shape in new short[] { 0, 1, 2 })
        {
            var variants = added.Where(item => item.Type == ItemType.Amulet && item.Shape == shape).ToArray();
            check(variants.Select(item => item.StackSize).SequenceEqual(new ushort[] { 500, 1000, 2000 }) && variants.All(item => item.Durability == 0 && item.Price == 2), "毒符三规格次数/价格错误");
            check(variants.Select(PublicShopRules.GroupName).Distinct().Count() == 1 && variants.All(item => PublicShopRules.UnitCount(item) == item.StackSize), "毒符未按包分组");
        }
        var all = originals.Concat(added).ToList();
        check(originals.Where(item => item.Type == ItemType.Amulet).All(item => PublicShopRules.Category(item) == -1) &&
            added.Count(item => PublicShopRules.Category(item) == 2) == 9, "旧的无规格符毒仍在公共商店");
        var oldPacks = added.Where(item => item.Type == ItemType.Amulet).Select(item =>
        {
            using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) item.Save(writer);
            memory.Position = 0; var old = new ItemInfo(new BinaryReader(memory));
            old.StackSize = (ushort)(item.StackSize switch { 500 => 100, 1000 => 300, _ => 500 }); return old;
        }).ToList();
        var owned = new UserItem(oldPacks[0]) { UniqueID = 77, Count = 73 };
        var upgraded = SupplyCatalog.Prepare(originals.Concat(oldPacks).Concat(added.Where(MagicBloodStone.IsStone)).ToList(), 22);
        check(upgraded.Added.Count == 0 && upgraded.Resized.Count == 9 && owned.Count == 73 && oldPacks[0].StackSize == 100,
            "旧规格迁移预检提前修改物品或生成重复编号");
        foreach (var change in upgraded.Resized) { change.Item.StackSize = change.Count; change.Item.Weight = change.Weight; }
        check(owned.Count == 73 && owned.UniqueID == 77 && owned.Info.StackSize == 500 && oldPacks.All(item => PublicShopRules.Category(item) == 2),
            "规格升级改变玩家剩余次数/唯一编号或未上架新规格");
        check(SupplyCatalog.Prepare(all, 22) is { Added.Count: 0, Resized.Count: 0 } && originals[0].Name == "旧耗材0", "补给重复生成或改写旧定义");
        var conflict = new ItemInfo { Name = added[0].Name, Type = ItemType.Weapon };
        try { SupplyCatalog.Prepare(originals.Append(conflict).ToList(), 22); check(false, "冲突定义被覆盖"); }
        catch (InvalidDataException) { check(conflict.Type == ItemType.Weapon, "冲突修改原物品"); }
        var stones = added.Where(MagicBloodStone.IsStone).ToArray();
        check(stones.Select(item => item.Stats[Stat.MagicBlood]).SequenceEqual(new[] { 100000, 300000, 500000 }) &&
            stones.All(item => item.StackSize == 1 && item.UnitWeight == .1m && PublicShopRules.Category(item) == 3), "魔血石规格/负重/分类错误");
        var stone = new UserItem(stones[0]) { UniqueID = 7, CurrentDura = 1, MaxDura = 1 };
        check(MagicBloodStone.Consume(stone, 0, 0) == (0, 0) && MagicBloodStone.Remaining(stone) == 100000, "满血满蓝消耗魔血石");
        check(MagicBloodStone.Consume(stone, 10, 80) == (10, 30) && MagicBloodStone.Remaining(stone) == 99960, "魔血石没有按实际缺失共用储量");
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) stone.Save(writer);
        memory.Position = 0;
        var restored = new UserItem(new BinaryReader(memory)) { Info = stones[0] };
        check(restored.UniqueID == 7 && MagicBloodStone.Remaining(restored) == 99960 && restored.Price() < stones[0].Price,
            "魔血石储量/编号没有通过原物品格式保存或售价未减少");
        restored.AddedStats[Stat.MagicBlood] = -99993;
        check(MagicBloodStone.Consume(restored, 50, 50) == (7, 0) && MagicBloodStone.Remaining(restored) == 0 &&
            MagicBloodStone.Consume(restored, 50, 50) == (0, 0), "魔血石透支储量");
        var amulet = new UserItem(added[0]) { Count = 100, CurrentDura = 500, MaxDura = 500 };
        check(ItemPresentation.Details(amulet).Contains("剩余次数 100") && !ItemPresentation.Details(amulet).Contains("耐久") &&
            !ItemPresentation.Name(amulet).Contains('×'), "毒符详情仍把次数当耐久/件数");
        check(ItemPresentation.Details(stone).Contains("99,960/100,000") && !ItemPresentation.Stats(stone).Any(), "魔血石储量显示不正确");
        var potion = new ItemInfo { Index = 100, Type = ItemType.Potion, StackSize = 999, Stats = new Stats { [Stat.HP] = 10 } };
        UserItem[] batch=[new(potion){Count=999},new(potion){Count=1}];
        check(InventoryStacks.CanAdd(new UserItem[2], batch) && !InventoryStacks.CanAdd(new UserItem[1], batch), "1000件采购跨999堆叠格位预检错误");
        check(PublicShopRules.GroupName(potion) == "金创药" && PublicShopRules.Category(new ItemInfo { Type = ItemType.Potion }) == -1,
            "普通恢复药分组或空药品白名单错误");
    }
}
