using Crystal.Native;
using Server.Helpers;
using S = ServerPackets;

internal static class ItemPresentationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var meat = new ItemInfo { Index = 902, Name = "鲜鹿肉", Type = ItemType.Meat, StackSize = 1 };
        var first = new UserItem(meat) { UniqueID = 100, Count = 51, CurrentDura = 12000, MaxDura = 20000 };
        var second = first.Clone(); second.UniqueID = 101; second.Count = 50;
        var quality = first.Clone(); quality.UniqueID = 102; quality.Count = 3; quality.CurrentDura = 13000;
        var bound = first.Clone(); bound.UniqueID = 103; bound.Count = 4; bound.SoulBoundId = 99;
        var gearInfo = new ItemInfo { Index = 903, Name = "测试武器", Type = ItemType.Weapon, StackSize = 999,
            RequiredClass = RequiredClass.Wizard, RequiredGender = RequiredGender.Female, RequiredAmount = 20,
            Stats = new Stats { [Stat.MinDC] = 3, [Stat.MaxDC] = 8, [Stat.MinMC] = 2, [Stat.MaxMC] = 5 } };
        var gear = new UserItem(gearInfo) { UniqueID = 104, Count = 1, AddedStats = new Stats { [Stat.MaxDC] = 1 } };
        UserItem[] grid = [first, second, quality, bound, gear];
        InventoryStacks.Compact(grid);
        check(first.Count == 101 && grid[1] == null && grid[0].UniqueID == 100 && grid[2].Count == 3 && grid[3].Count == 4 && grid[4] == gear,
            "登录自动合堆丢失数量/编号或混合了品质、绑定和装备");
        check(meat.StackSize == 999 && gearInfo.StackSize == 1 && !gear.CanStackWith(gear.Clone()), "普通肉合堆或装备单件上限错误");
        check(ItemPresentation.Name(first) == "鲜鹿肉×101" && ItemPresentation.Name(gear) == "测试武器", "堆叠名称或装备×1显示错误");
        foreach (var type in new[] { ItemType.Potion, ItemType.Scroll, ItemType.Book, ItemType.Food, ItemType.Script, ItemType.Transform, ItemType.Pets, ItemType.MonsterSpawn })
            check(ItemPresentation.CanQuickUse(new UserItem(new ItemInfo { Type = type })), $"{type} 使用类道具不能进入快捷栏");
        foreach (var type in Enum.GetValues<ItemType>().Where(type => new ItemInfo { Type = type }.IsEquipment)
            .Concat(new[] { ItemType.Amulet, ItemType.Quest, ItemType.Meat, ItemType.CraftingMaterial, ItemType.Ore, ItemType.Bait }))
            check(!ItemPresentation.CanQuickUse(new UserItem(new ItemInfo { Type = type })), $"{type} 非使用类道具进入快捷栏");
        check(!ItemPresentation.CanQuickUse(null) && !ItemPresentation.CanQuickUse(new UserItem(new ItemInfo()) { Info = null! }), "空物品被视为可使用");
        check(!ItemPresentation.Details(gear).Contains("耐久"), "0/0耐久没有隐藏");
        gear.MaxDura = 8000;
        check(ItemPresentation.Details(gear).Contains("耐久 0/8"), "破损0/8装备被误隐藏");
        check(ItemPresentation.Category(new UserItem(new ItemInfo { Type = ItemType.Quest })).Contains("专用任务栏") &&
            !ItemPresentation.Category(first).Contains("任务物品"), "专用任务物品与普通任务材料混淆");
        check(ItemPresentation.Stats(gear).Contains("攻击 3–9") && ItemPresentation.Stats(gear).Contains("魔法 2–5"), "属性区间没有合并或漏了附加属性");
        var state = new GameState();
        var worn = new UserItem(new ItemInfo { Type = ItemType.Weapon, Stats = new Stats { [Stat.MaxDC] = 5 } }) { Count = 1 };
        var equipment = new UserItem[14]; equipment[0] = worn;
        state.Apply(new S.UserInformation { ObjectID = 1, RealId = 5, Level = 10, Class = MirClass.Warrior, Gender = MirGender.Male,
            Inventory = [first, quality, bound, gear], Equipment = equipment, QuestInventory = [] });
        check(ItemPresentation.Requirements(gear, state).Count(requirement => !requirement.Met) >= 3, "等级/职业/性别不符未标为不可穿戴");
        check(ItemPresentation.Comparison(gear, worn).Any(pair => pair.text == "攻击 +3 / +4" && pair.change > 0), "同位置对比忽略最低/最高攻击");
        check(ItemPresentation.Slots(ItemType.Ring).SequenceEqual(new[] { 7, 8 }) && ItemPresentation.Slots(ItemType.Potion).Length == 0, "双戒指对比或非装备格位错误");
        var extra = first.Clone(); extra.UniqueID = 106; extra.Count = 900;
        state.User!.Inventory = [first, extra];
        state.Apply(new S.MergeItem { GridFrom = MirGridType.Inventory, GridTo = MirGridType.Inventory, IDFrom = 106, IDTo = 100, Success = false });
        check(first.Count == 101 && extra.Count == 900, "拒绝合堆在本地扣除了物品");
        state.Apply(new S.MergeItem { GridFrom = MirGridType.Inventory, GridTo = MirGridType.Inventory, IDFrom = 106, IDTo = 100, Success = true });
        check(first.Count == 999 && extra.Count == 2, "服务端合堆回执没有保留溢出数量");
        state.Apply(new S.GainedItem { Item = new UserItem(meat) { UniqueID = 107, Count = 99, CurrentDura = 12000, MaxDura = 20000 } });
        check(state.User.Inventory.Sum(item => item?.Count ?? 0) == 1100 && extra.Count == 101, "新拾取物品没有自动合堆或数量丢失");
        InventoryStacks.Compact(grid);
        check(grid.Sum(item => item?.Count ?? 0) == 1007, "重复合堆丢失数量");
    }
}
