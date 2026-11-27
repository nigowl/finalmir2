using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

internal static class ClientPolishChecks
{
    public static void Run(Action<bool, string> check)
    {
        var directions = new[] { (true, false, false, false), (true, false, false, true), (false, false, false, true),
            (false, true, false, true), (false, true, false, false), (false, true, true, false), (false, false, true, false), (true, false, true, false) };
        for (var index = 0; index < directions.Length; index++)
        { var (up, down, left, right) = directions[index]; check(ClientMovement.KeyboardDirection(up, down, left, right) == (MirDirection)index, "方向键八向组合错误"); }
        check(ClientMovement.KeyboardDirection(true, true, false, false) == null && ClientMovement.KeyboardDirection(false, false, true, true) == null &&
            ClientMovement.KeyboardDirection(true, true, true, true) == null && ClientMovement.KeyboardDirection(true, true, false, true) == MirDirection.Right, "相反方向键没有抵消");
        var preferences = System.Text.Json.JsonSerializer.Deserialize<ClientPreferences>("{\"CrtFilter\":true,\"CrtStrength\":4}")!;
        check(preferences.CrtCurvature == 3 && preferences.CrtStrength == 4, "旧CRT设置未保留扫描线和默认弧面");
        preferences.CrtCurvature = 99; preferences.Normalize(); check(preferences.CrtCurvature == 5, "CRT弧面越过上限");
        preferences.CrtCurvature = -1; preferences.Normalize();
        check(preferences.CrtCurvature == 0 && System.Text.Json.JsonSerializer.Deserialize<ClientPreferences>(System.Text.Json.JsonSerializer.Serialize(preferences))!.CrtCurvature == 0, "平面CRT保存或下限错误");
        Organization(check); DualSummons(check);
    }

    private static void Organization(Action<bool, string> check)
    {
        var info = new ItemInfo { Index = 5, Name = "金创药", Type = ItemType.Potion, StackSize = 20 };
        var other = new ItemInfo { Index = 6, Name = "魔法药", Type = ItemType.Potion, StackSize = 20 };
        UserItem Item(ItemInfo definition, ulong id, ushort count) => new(definition) { UniqueID = id, Count = count };
        var inventory = new UserItem[80]; inventory[0] = Item(info, 1, 10); inventory[6] = Item(info, 2, 20);
        inventory[8] = Item(other, 3, 5); inventory[9] = Item(info, 4, 2); inventory[9].SoulBoundId = 99;
        var state = new GameState(); state.Apply(new S.UserInformation { ObjectID = 1, Inventory = inventory, Equipment = new UserItem[14], QuestInventory = [] });
        state.Items[info.Index] = info;
        state.Apply(new S.SplitItem1 { Grid = MirGridType.Inventory, UniqueID = 2, Count = 1, Success = true });
        state.Apply(new S.SplitItem { Grid = MirGridType.Inventory, Item = Item(info, 20, 1) });
        check(inventory[6].Count == 19 && inventory[1]?.UniqueID == 20, "背包拆分回执未同步或重新合堆");
        state.Apply(new S.MergeItem { GridFrom = MirGridType.Inventory, GridTo = MirGridType.Inventory, IDFrom = 20, IDTo = 2, Success = true });
        var organizer = new ClientInventoryOrganizer();
        check(!organizer.Begin(1, 100, false) && !organizer.Begin(1, 30099, false), "整理早于30秒触发");
        check(!organizer.Begin(1, 30100, true) && organizer.Begin(1, 30101, false), "物品操作期间整理未等待");
        var before = inventory.Where(item => item != null).Sum(item => item.Count);
        var merge = organizer.Next(inventory, 1, 30101)!;
        check(merge is { IDFrom: 2, IDTo: 1 } && organizer.Locked(1) && organizer.Locked(2) && inventory[0].Count == 10 && inventory[6].Count == 20, "合堆预改资产、错用快捷源或遗漏满堆来源");
        organizer.Sent(merge);
        organizer.Observe(new S.MergeItem { GridFrom = MirGridType.Inventory, GridTo = MirGridType.Inventory, IDFrom = 20, IDTo = 1, Success = true });
        check(organizer.Pending == merge && !organizer.Begin(2, 31000, false), "无关回执解除了整理锁");
        var reply = new S.MergeItem { GridFrom = merge.GridFrom, GridTo = merge.GridTo, IDFrom = merge.IDFrom, IDTo = merge.IDTo, Success = true };
        state.Apply(reply); organizer.Observe(reply);
        check(organizer.Begin(state.InventoryRevision, 31000, false) && organizer.Next(inventory, state.InventoryRevision, 31000) == null &&
            inventory[0].UniqueID == 1 && inventory[0].Count == 30 && inventory[6] == null && inventory.Where(item => item != null).Sum(item => item.Count) == before, "原合堆回执未守恒或忽略绑定差异");
        check(!organizer.Begin(state.InventoryRevision, 100000, false), "未变化背包重复扫描");
        var snapshot = string.Join('/', inventory.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.Count}"));
        var order = ClientInventoryOrganizer.DisplayOrder(inventory);
        check(order.Length == 74 && order.Distinct().Count() == 74 && order.All(slot => slot >= 6) &&
            order.Take(2).All(slot => inventory[slot] != null) && snapshot == string.Join('/', inventory.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.Count}")), "排序改变真实格位/数量或遗漏快捷槽");
        inventory[1] = new UserItem(new ItemInfo { Type = ItemType.Weapon });
        check(ItemPresentation.BagSlots(inventory).Contains(1) && !ItemPresentation.BagSlots(inventory).Contains(0), "旧快捷装备无法取出或快捷药品重复显示");
        var legacy = new Server.MirDatabase.CharacterInfo { Inventory = inventory };
        check(legacy.ResizeInventory() == 86 && ItemPresentation.BagSlots(legacy.Inventory).Count(slot => slot >= 6) == 80 &&
            legacy.Inventory.Take(80).SequenceEqual(inventory) && legacy.Inventory.Skip(80).All(item => item == null), "80格背包加6格快捷栏扩容丢失旧格位或物品");
        inventory[1] = null!;
        check(ItemPresentation.BagSlots([]).Length == 0 && ItemPresentation.BagSlots(inventory.Take(6).ToArray()).Length == 0, "小容量快捷栏出现虚假背包格");
        organizer.Reset(); organizer.Begin(1, 1, false);
        var use = new C.UseItem { Grid = MirGridType.Inventory, UniqueID = 1 }; organizer.Sent(use);
        check(!organizer.Begin(2, 30001, false), "未确认使用物品时开始整理");
        organizer.Observe(new S.UseItem { Grid = use.Grid, UniqueID = use.UniqueID });
        check(organizer.Begin(2, 30002, false), "实际物品回执未解除等待");
        inventory[7] = Item(info, 9, 2); merge = organizer.Next(inventory, 2, 30002)!;
        check(merge != null && !organizer.Begin(3, 35002, false) && organizer.Paused && organizer.Pending == merge, "未知合堆被自动重发或解锁");
        organizer.Observe(new S.MapChanged()); organizer.Observe(new S.Death());
        check(organizer.Pending == merge && organizer.Paused, "死亡/切图清除了未知物品锁");
        organizer.Observe(new S.MergeItem { GridFrom = merge!.GridFrom, GridTo = merge.GridTo, IDFrom = merge.IDFrom, IDTo = merge.IDTo, Success = false });
        check(organizer.Pending == null && organizer.Paused && !organizer.Begin(4, 100000, false), "迟到回执自动恢复未知整理");
        organizer.Observe(new S.LogOutSuccess()); check(!organizer.Paused && organizer.Pending == null, "退出后未清理整理会话");
    }

    private static void DualSummons(Action<bool, string> check)
    {
        var state = new GameState(); state.Apply(new S.UserInformation { ObjectID = 1, Class = MirClass.Taoist, HP = 100, MP = 100,
            Inventory = new UserItem[80], Equipment = new UserItem[14], QuestInventory = [] });
        foreach (var spell in new[] { Spell.SummonSkeleton, Spell.SummonShinsu }) state.User!.Magics.Add(new ClientMagic { Spell = spell, BaseCost = 10 });
        state.User!.Equipment[9] = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = 0 }) { Count = 100 };
        var prefs = new ClientPreferences(); const long now = 100000;
        check(ClassSkillRules.Next(state, prefs, null, true, now) == Spell.SummonShinsu, "道士没有先召已学的神兽");
        state.Actors[2] = new Actor { Id = 2, Kind = ObjectType.Monster, MasterObjectId = 1, Image = (int)Monster.Shinsu1 };
        check(ClassSkillRules.Confirmed(state, Spell.SummonShinsu, 0, 0, 100) && !ClassSkillRules.Confirmed(state, Spell.SummonSkeleton, 0, 0, 100) &&
            ClassSkillRules.Next(state, prefs, null, true, now) == Spell.SummonSkeleton, "神兽错误确认骷髅或阻止第二只召唤");
        state.Actors[3] = new Actor { Id = 3, Kind = ObjectType.Monster, MasterObjectId = 1, Image = (int)Monster.BoneFamiliar, Hidden = true };
        check(ClassSkillRules.Next(state, prefs, null, true, now) == Spell.None, "双宠齐全后仍重复召唤");
        state.User.Magics.Add(new ClientMagic { Spell = Spell.SummonHolyDeva });
        check(ClassSkillRules.Next(state, prefs, null, true, now) == Spell.None, "已有两只仍尝试第三只召唤");
        state.Actors[3].Dead = true; state.DisabledSkills.Add(Spell.SummonHolyDeva);
        check(ClassSkillRules.Next(state, prefs, null, true, now) == Spell.SummonSkeleton, "骷髅死亡后没有单独补召");
        state.Actors[3].Dead = false; state.Actors[3].MasterObjectId = 99;
        check(!ClassSkillRules.HasPet(state, Spell.SummonSkeleton) && ClassSkillRules.Next(state, prefs, null, true, now) == Spell.SummonSkeleton, "别人的宠物被算作本人召唤");
        prefs.SmartBattle = false;
        check(ClassSkillRules.Next(state, prefs, null, true, now) == Spell.None, "关闭组合后仍自动双召唤");
    }
}
