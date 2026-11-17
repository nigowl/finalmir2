using Crystal.Native;
using S = ServerPackets;

internal static class ClientItemChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState();
        foreach (var (amount, image) in new (uint, int)[] { (0,112),(1,112),(99,112),(100,114),(999,114),(1000,116),
            (999999,116),(1000000,117),(4999999,117),(5000000,121),(9999999,121),(10000000,122),(uint.MaxValue,122) })
        {
            state.Apply(new S.ObjectGold { ObjectID = 90, Gold = amount, Location = new(2,3) });
            var gold = state.Actors[90];
            check(gold.Image == image && gold.Gold == amount && gold.Name == $"{amount} 金币" && gold.Location == new System.Drawing.Point(2,3),
                $"地面金币数量分档或真实数量错误：{amount}");
        }
        foreach (var image in new ushort[] { 117, 121, 122 })
        {
            state.Apply(new S.ObjectItem { ObjectID = 91, Image = image, Name = "兑换物品" });
            check(state.Actors[91].Image == image && state.Actors[91].Gold == 0, "金币外观规则错误转换了真实金条/金砖/金盒物品");
        }
        var messages = new List<string>(); state.Message += messages.Add;
        var typed = new List<(string, ChatType)>(); state.ChatMessage += (text, type) => typed.Add((text, type));
        state.Apply(new S.Chat { Message = "组队", Type = ChatType.Group });
        state.Apply(new S.ObjectChat { Text = "普通", Type = ChatType.Normal });
        check(typed.SequenceEqual(new[] { ("组队", ChatType.Group), ("普通", ChatType.Normal) }), "聊天归约丢失服务器消息类型");
        var item = new UserItem(new ItemInfo { Index = 1, Type = ItemType.Weapon }) { UniqueID = 42, Count = 3, CurrentDura = 1000, MaxDura = 5000 };
        state.Apply(new S.UserInformation { ObjectID = 1, Inventory = [item], Equipment = new UserItem[14], QuestInventory = [] });
        state.Apply(new S.DropItem { UniqueID = 42, Count = 1, Success = false });
        check(item.Count == 3 && state.User!.Inventory[0] == item, "Rejected drop removed inventory locally");
        state.Apply(new S.DropItem { UniqueID = 42, Count = 1, HeroItem = true, Success = true });
        check(item.Count == 3, "Hero drop changed the player inventory");
        state.Apply(new S.DropItem { UniqueID = 99, Count = 1, Success = true });
        check(item.Count == 3, "Unrelated drop changed a different item");
        state.Apply(new S.DropItem { UniqueID = 42, Count = 1, Success = true });
        check(item.Count == 2, "Partial drop ignored the server-confirmed quantity");
        state.Apply(new S.RepairItem { UniqueID = 42 });
        check(item.CurrentDura == 1000 && item.MaxDura == 5000, "Repair unlock acknowledgement invented a successful repair");
        state.Apply(new S.ItemRepaired { UniqueID = 42, CurrentDura = 4900, MaxDura = 4900 });
        check(item.CurrentDura == 4900 && item.MaxDura == 4900, "Repair result lost authoritative durability values");
        state.Apply(new S.DropItem { UniqueID = 42, Count = 2, Success = true });
        check(state.User!.Inventory[0] == null, "Full stack drop left a phantom inventory item");
        state.Apply(new S.EquipItem { UniqueID = 42, Grid = MirGridType.Inventory, Success = false });
        state.Apply(new S.UseItem { UniqueID = 42, Success = false });
        state.Apply(new S.NewMagic { Magic = new ClientMagic { Spell = Spell.FireBall } });
        check(messages.Any(text => text.StartsWith("装备失败")) && messages.Any(text => text.StartsWith("使用失败")) && messages.Any(text => text.StartsWith("学会技能")), "Common item/skill feedback omitted server results");
    }
}
