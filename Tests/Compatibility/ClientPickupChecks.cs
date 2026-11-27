using Crystal.Native;
using Point = System.Drawing.Point;
using S = ServerPackets;

internal static class ClientPickupChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState(); state.Apply(new S.UserInformation { ObjectID = 1, HP = 100, Location = new Point(10, 10),
            Inventory = new UserItem[8], Equipment = new UserItem[14], QuestInventory = [] });
        state.Apply(new S.ObjectItem { ObjectID = 2, Name = "鲜鹿肉 (101)", Location = new Point(10, 10) });
        state.Apply(new S.ObjectGold { ObjectID = 3, Gold = 100, Location = new Point(11, 10) });
        var preferences = new ClientPreferences(); var pickup = new ClientPickup();
        check(!preferences.AutoPickup && pickup.Next(state, preferences, 1000) == null, "自动拣取默认开启");
        preferences.AutoPickup = true;
        check(ClientPickup.Name(state.Actors[2]) == "鲜鹿肉" && ClientPickup.Name(state.Actors[3]) == "金币", "堆叠数量或金币金额污染拣取名称");
        preferences.PickupWhitelist = true; preferences.PickupNames = [];
        check(pickup.Next(state, preferences, 1000) == null, "空白名单变成全部拣取");
        preferences.PickupNames = ["鲜鹿肉"];
        check(pickup.Next(state, preferences, 1000)?.Id == 2, "指定名单没有匹配整叠物品");
        state.Actors[3].Location = new Point(10, 10);
        check(pickup.Next(state, preferences, 1000) == null, "混合格仍发送原PickUp，可能拣取排除物品");
        preferences.PickupNames = ["鲜鹿肉", "金币"];
        check(pickup.Next(state, preferences, 1000)?.Id == 2, "全部允许的同格被禁止拣取");
        state.Actors[3].Location = new Point(11, 10);
        preferences.PickupWhitelist = false; preferences.PickupNames = ["鲜鹿肉"];
        check(pickup.Next(state, preferences, 1000)?.Id == 3, "黑名单没有排除物品");
        preferences.PickupNames = ["鹿肉"];
        check(pickup.Next(state, preferences, 1000)?.Id == 2, "完整名称过滤被错误改成模糊匹配");
        check(pickup.Next(state, preferences, 1000, item => item.Id == 3)?.Id == 3, "不可达物品阻塞其他可达候选");
        check(!pickup.Begin(state, preferences, state.Actors[3], 1000), "未到格子就发送拾取");
        check(pickup.Begin(state, preferences, state.Actors[2], 1000) && pickup.Pending == 2 && state.User!.Inventory.All(item => item == null), "拾取没有保留待确认请求或预先造物品");
        check(pickup.Next(state, preferences, 1100) == null && !pickup.Begin(state, preferences, state.Actors[2], 1100), "等待回执时重复发送拾取");
        pickup.Apply(new S.ObjectRemove { ObjectID = 3 });
        check(pickup.Pending == 2, "无关对象移除清除了未决拾取");
        pickup.Tick(4100);
        check(pickup.Paused && pickup.Pending == 2 && pickup.Next(state, preferences, 9000) == null, "拾取超时后自动重发");
        pickup.Apply(new S.ObjectRemove { ObjectID = 2 });
        check(pickup.Pending == 0 && !pickup.Paused, "迟到的原对象移除没有释放待确认拾取");
        state.User!.Gold = uint.MaxValue;
        preferences.PickupNames = ["鲜鹿肉"];
        check(pickup.Next(state, preferences, 10000) == null, "金币溢出仍被自动选择"); state.User.Gold = 0;
        state.StorageActive = true;
        check(pickup.Next(state, preferences, 10000) == null, "仓库交互中自动拣取"); state.StorageActive = false;
        Array.Fill(state.User.Inventory, new UserItem(new ItemInfo { Index = 7, Type = ItemType.Potion }) { UniqueID = 7 });
        check(pickup.Next(state, preferences, 10000)?.Id == 3, "背包满时金币被当作普通物品跳过");
        preferences.PickupWhitelist = true; preferences.PickupNames = ["鲜鹿肉"];
        check(pickup.Next(state, preferences, 10000) == null, "拣取金币绕过了白名单或背包容量");
        state.MaterialItemNames.Add("鲜鹿肉"); state.User.QuestInventory = new UserItem[40];
        check(pickup.Next(state, preferences, 10000)?.Id == 2, "普通包满时错误跳过材料栏仍可放入的材料");
        Array.Fill(state.User.QuestInventory, new UserItem(new ItemInfo { Type = ItemType.CraftingMaterial }));
        check(pickup.Next(state, preferences, 10000) == null, "材料栏满时仍持续请求拣取材料");
        state.MaterialItemNames.Clear();
        Array.Clear(state.User.Inventory); preferences.PickupWhitelist = false;
        preferences.PickupNames = [];
        check(pickup.Begin(state, preferences, state.Actors[2], 10000), "有效拾取不能重新开始");
        pickup.Apply(new S.MapChanged());
        check(pickup.Pending == 0 && !pickup.Paused, "切图仍保留旧目标拾取");
        preferences.PickupNames = [" 红药 ", "红药", "", new string('a', 41)]; preferences.Normalize();
        check(preferences.PickupNames.SequenceEqual(["红药"]), "名单没有规范去重或限制长度");
    }
}
