using System.Runtime.CompilerServices;
using System.Reflection;
using Server.MirDatabase;
using Server.MirNetwork;
using Server.MirObjects;
using S = ServerPackets;

internal static class NpcSessionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var script = (NPCScript)RuntimeHelpers.GetUninitializedObject(typeof(NPCScript));
        var main = new NPCPage(NPCScript.MainKey);
        main.SegmentList.Add(new NPCSegment(main, [], [NPCScript.BuyNewKey, NPCScript.RepairKey, "[@@INPUT]"], [], [], []));
        var buy = new NPCPage(NPCScript.BuyNewKey);
        var repair = new NPCPage(NPCScript.RepairKey);
        var input = new NPCPage("[@@INPUT]");
        script.NPCPages = [main, buy, repair, input]; script.Goods = [];
        var player = new NpcPlayer { Info = new CharacterInfo(), Account = new AccountInfo() };
        script.Call(player, 71, main.Key);
        check(player.NPCPage == main && player.NPCObjectID == 71, "NPC main did not establish its authorized dialogue");
        script.Call(player, 72, buy.Key);
        check(player.NPCPage == main, "A different NPC reused the current dialogue's shop link");
        script.Call(player, 71, buy.Key);
        check(player.NPCPage == buy && player.Packets.OfType<S.NPCGoods>().Any(), "A built-in shop page without dialogue sent goods but retained an invalid purchase session");
        player.Packets.Clear(); script.Call(player, 71, repair.Key);
        check(player.NPCPage == buy && !player.Packets.OfType<S.NPCRepair>().Any(), "An unoffered link bypassed NPC page validation");
        script.Call(player, 71, main.Key); script.Call(player, 71, repair.Key);
        check(player.NPCPage == repair && player.Packets.OfType<S.NPCRepair>().Any(), "An empty repair page retained the previous NPC session");
        var fresh = new NpcPlayer { Info = new CharacterInfo(), Account = new AccountInfo() };
        script.Call(fresh, 71, buy.Key);
        check(fresh.NPCPage == null && fresh.Packets.Count == 0, "An unsolicited shop page created a session or threw");

        script.Call(player, 71, main.Key); player.Packets.Clear();
        script.Call(player, 72, input.Key);
        check(player.Packets.Count == 0 && player.NPCPage == main, "其他NPC借用了当前文本输入链接");
        script.Call(player, 71, input.Key);
        check(player.Packets.OfType<S.NPCRequestInput>().SingleOrDefault() is { NPCID: 71, PageName: "[@@INPUT]" } && player.NPCPage == main,
            "原输入页面未先请求输入，或提前切换原会话");
        player.Packets.Clear(); player.NPCData["NPCInputStr"] = ""; script.Call(player, 71, input.Key);
        check(player.NPCPage == input && !player.NPCData.ContainsKey("NPCInputStr") && player.Packets.OfType<S.NPCResponse>().Count() == 1,
            "合法空输入无法执行原页面，或输入遗留到后续会话");
        player.Packets.Clear(); script.Call(player, 71, input.Key);
        check(player.Packets.Count == 0, "未再次提供的文本输入链接被重复执行");

        var connection = (MirConnection)RuntimeHelpers.GetUninitializedObject(typeof(MirConnection));
        connection.Stage = GameStage.Game; connection.Player = player;
        var confirm = typeof(MirConnection).GetMethod("NPCConfirmInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var invalid in new ClientPackets.NPCConfirmInput[]
        {
            new() { NPCID = 0, PageName = input.Key, Value = "无会话" },
            new() { NPCID = 72, PageName = input.Key, Value = "旧会话" },
            new() { NPCID = 71, PageName = input.Key, Value = new string('长', 51) },
            new() { NPCID = 71, PageName = main.Key, Value = "非输入页面" }
        })
        {
            confirm.Invoke(connection, [invalid]);
            check(!player.NPCData.ContainsKey("NPCInputStr") && player.Packets.Count == 0, "无效输入越过原连接边界或遗留旧值：" + invalid.Value);
        }

        var equipment = new ItemInfo { Index = 900001, Name = "占格装备", Type = ItemType.Weapon };
        var potion = new ItemInfo { Index = 900002, Name = "购药容量检查", Type = ItemType.Potion, StackSize = 20, Price = 2 };
        var goods = new UserItem(potion) { UniqueID = 900003, Count = 1 };
        script.Goods = [goods];
        player.NPCPage = buy; player.Account.Gold = 100;
        player.Info.Inventory = Enumerable.Range(0, 46).Select(i => new UserItem(equipment) { UniqueID = (ulong)(910000 + i) }).ToArray();
        var original = player.Info.Inventory.Select(item => item.UniqueID).ToArray();
        script.Buy(player, goods.UniqueID, 1);
        check(player.Messages.Last().Contains("46/46") && !player.Messages.Last().Contains("负重") && player.Account.Gold == 100 &&
            player.Info.Inventory.Select(item => item.UniqueID).SequenceEqual(original), "旧46格满包的购买拒绝原因不准确或修改了资产");
        player.Info.ResizeInventory();
        check(player.Info.Inventory.Length == Globals.BaseInventorySize && player.Info.Inventory.Take(46).Select(item => item.UniqueID).SequenceEqual(original) &&
            player.Info.Inventory.Skip(46).All(item => item == null) && player.CanGainItem(goods), "旧库存扩到当前默认容量后仍拒绝商品或改变了旧物品");
    }

    private sealed class NpcPlayer : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public readonly List<string> Messages = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void ReceiveChat(string text, ChatType type) => Messages.Add(text);
    }
}
