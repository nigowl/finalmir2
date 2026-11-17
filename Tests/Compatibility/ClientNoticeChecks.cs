using Crystal.Native;
using S = ServerPackets;

internal static class ClientNoticeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState(); var notices = new List<(string Text, ChatType Type)>();
        var chats = new List<(string Text, ChatType Type)>();
        state.ChatMessage += (text, type) => chats.Add((text, type));
        state.ItemMessage += (text, type) => notices.Add((text, type));
        state.Apply(new S.UserInformation { ObjectID = 1, HP = 30, Gold = 100, Inventory = new UserItem[8], Equipment = new UserItem[14], QuestInventory = [] });
        var info = new ItemInfo { Index = 1, Name = "回归药品", Type = ItemType.Potion, StackSize = 99 };
        state.Items[1] = info;
        state.Apply(new S.GainedItem { Item = new UserItem(info) { UniqueID = 10, Count = 4 } });
        check(notices[^1] == ("获得 回归药品 ×4", ChatType.System2), "获得物品提示丢失原名称或数量");
        state.Apply(new S.UseItem { UniqueID = 10, Success = false });
        check(notices.Count == 1 && chats[^1].Type == ChatType.Hint && state.User!.Inventory[0].Count == 4, "使用失败仍扣除物品或显示成功");
        state.Apply(new S.UseItem { UniqueID = 10, Success = true });
        check(notices[^1] == ("使用成功：回归药品。", ChatType.System2) && state.User!.Inventory[0].Count == 3, "使用回执的提示或数量错误");
        state.Apply(new S.DropItem { UniqueID = 10, Count = 1, Success = true });
        check(notices[^1].Text == "丢弃成功：回归药品 ×1。" && state.User!.Inventory[0].Count == 2, "丢弃提示没有在删除前读取物品名称");
        state.Apply(new S.DeleteItem { UniqueID = 10, Count = 1 });
        check(notices[^1].Text == "扣除物品：回归药品 ×1。" && state.User!.Inventory[0].Count == 1, "服务器扣除物品没有真实回执提示");
        var previous = notices.Count; state.Apply(new S.DeleteItem { UniqueID = 99, Count = 1 });
        check(notices.Count == previous, "未知物品扣除伪造了名称提示");
        state.Apply(new S.GainedGold { Gold = 37 });
        check(state.User!.Gold == 137 && notices[^1] == ("获得金币 ×37", ChatType.System2), "金币到账或提示错误");
        state.Apply(new S.LoseGold { Gold = 20 });
        check(state.User.Gold == 117 && notices[^1] == ("消耗金币 ×20", ChatType.System2), "金币支出或提示错误");
        var itemCount = notices.Count; var chatCount = chats.Count;
        state.Apply(new S.Chat { Message = "购买失败：余额不足", Type = ChatType.System });
        state.Apply(new S.Chat { Message = "命令执行错误：地图不存在", Type = ChatType.Hint });
        state.Apply(new S.Chat { Message = "购买成功：普通聊天文字", Type = ChatType.Normal });
        check(notices.Count == itemCount && chats.Count == chatCount + 3, "错误/命令/普通聊天误入物品消息区域");
        state.Apply(new S.Chat { Message = "购买成功：回归药品 ×1", Type = ChatType.System });
        check(notices.Count == itemCount + 1 && chats.Count == chatCount + 3, "原服购买成功消息没有独立分流");
    }
}
