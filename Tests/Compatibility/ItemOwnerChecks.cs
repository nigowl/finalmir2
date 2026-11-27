using Crystal.Native;
using S = ServerPackets;

internal static class ItemOwnerChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState(); var revision = state.InventoryRevision;
        state.Apply(new S.UserName { Id = 44, Name = "未登录迟到" });
        check(!state.RequestItemOwnerName(44) && state.ItemOwnerName(44) == "未知角色" && state.InventoryRevision == revision,
            "未登录查询或姓名回执创建状态");
        S.UserInformation User(uint id, string name) => new() { ObjectID = 2, RealId = id, Name = name,
            Inventory = [], QuestInventory = [], Equipment = new UserItem[14] };
        state.Apply(User(70, "本人中文")); var me = state.Me;
        check(state.ItemOwnerName(70) == "本人中文" && !state.RequestItemOwnerName(70), "本人绑定名称未使用数据库编号或重复请求本人");
        foreach (var id in new[] { -1, int.MinValue, 0 }) check(!state.RequestItemOwnerName(id), "无效绑定编号仍发请求");
        var item = new UserItem(new ItemInfo { Index = 1, Name = "绑定原物", Type = ItemType.Ring })
            { UniqueID = 100, SoulBoundId = 44, CurrentDura = 1200, MaxDura = 5000 };
        state.User!.Inventory = [item]; state.User.Gold = 1234;
        check(ItemPresentation.Requirements(item, state).Single(rule => rule.Text.StartsWith("绑定")).Text == "绑定：未知角色",
            "未查询绑定主人时猜测了姓名");
        state.Apply(new S.UserName { Id = 44, Name = "未请求名称" });
        check(state.ItemOwnerName(44) == "未知角色", "未请求的名称回执写入缓存");
        check(state.RequestItemOwnerName(44), "首次绑定名称查询未登记");
        check(Enumerable.Range(0, 100).All(_ => !state.RequestItemOwnerName(44)), "多物品/反复悬停重复发送未决查名");
        revision = state.InventoryRevision; state.Apply(new S.UserName { Id = 44, Name = "离线主人" });
        var rule = ItemPresentation.Requirements(item, state).Single(rule => rule.Text.StartsWith("绑定"));
        check(state.ItemOwnerName(44) == "离线主人" && rule.Text == "绑定：离线主人" && !rule.Met && state.InventoryRevision == revision + 1,
            "原姓名回执未刷新绑定详情或放开他人使用权限");
        check(!state.RequestItemOwnerName(44), "已知主人仍重复查询");
        revision = state.InventoryRevision; state.Apply(new S.UserName { Id = 44, Name = "离线主人" });
        check(state.InventoryRevision == revision, "重复姓名回执反复刷新全部物品");
        check(state.RequestItemOwnerName(2), "场景同号被误当成本人数据库编号");
        state.Apply(new S.UserName { Id = 2, Name = "另一个数据库角色" });
        check(ReferenceEquals(state.Me, me) && me!.Name == "本人中文" && state.User.Name == "本人中文" && state.User.RealId == 70 &&
            state.ItemOwnerName(2) == "另一个数据库角色" && !state.Actors.ContainsKey(44), "绑定查名改写场景/本人身份或创建幽灵对象");
        check(ReferenceEquals(state.User.Inventory[0], item) && item.UniqueID == 100 && item.SoulBoundId == 44 && item.Count == 1 &&
            item.CurrentDura == 1200 && item.MaxDura == 5000 && state.User.Gold == 1234, "姓名回执改变物品引用/资产/绑定编号");
        check(state.RequestItemOwnerName(45), "另一个主人未独立登记"); state.Apply(new S.UserName { Id = 45, Name = "" });
        check(state.ItemOwnerName(45) == "未知角色" && !state.RequestItemOwnerName(45), "空名称显示为空或无界重复查名");
        check(state.RequestItemOwnerName(int.MaxValue) && !state.RequestItemOwnerName(int.MaxValue), "原服无该角色时未保留待查状态");
        revision = state.InventoryRevision; state.Apply(new S.UserName { Id = uint.MaxValue, Name = "越界迟到" });
        check(state.InventoryRevision == revision, "无关联姓名回执触发物品修改");
        state.Apply(new S.MapChanged { MapIndex = 2 });
        check(state.ItemOwnerName(44) == "离线主人" && !state.RequestItemOwnerName(int.MaxValue), "切图丢失已查/未决名称并重复查询");
        state.Apply(new S.Death()); check(state.ItemOwnerName(44) == "离线主人", "死亡错误清理仍有效的名称缓存");
        state.Apply(new S.LogOutSuccess()); state.Apply(new S.UserName { Id = 44, Name = "旧角色迟到" });
        check(state.ItemOwnerName(44) == "未知角色" && !state.RequestItemOwnerName(44), "退出后仍接受旧角色名称");
        state.Apply(User(44, "新角色"));
        check(state.ItemOwnerName(44) == "新角色" && state.ItemOwnerName(2) == "未知角色" && state.RequestItemOwnerName(2), "换角泄漏旧缓存或冒用场景编号");
        state.Reset(); state.Apply(new S.UserName { Id = 2, Name = "断线迟到" }); state.Apply(User(70, "重新登录"));
        check(state.ItemOwnerName(2) == "未知角色" && state.RequestItemOwnerName(2), "断线重连未清理待查询状态");
        item.SoulBoundId = 70;
        check(ItemPresentation.Requirements(item, state).Single(requirement => requirement.Text.StartsWith("绑定")) is { Met: true, Text: "绑定：重新登录" },
            "名称显示改变原本人的绑定使用资格");
    }
}
