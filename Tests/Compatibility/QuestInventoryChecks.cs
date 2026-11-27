using System.Net;
using System.Net.Sockets;
using Crystal.Native;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirNetwork;
using Server.MirObjects;
using S = ServerPackets;

internal static class QuestInventoryChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var quest = new ItemInfo { Index = 990001, Name = "任务专用品", Type = ItemType.Quest, StackSize = 10, Weight = 20 };
        var meat = new ItemInfo { Index = 990002, Name = "普通鹿肉", Type = ItemType.Meat, Weight = 2 };
        var original = Envir.Main.ItemInfoList; Envir.Main.ItemInfoList = [quest, meat];
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var peer = new TcpClient(); peer.Connect((IPEndPoint)listener.LocalEndpoint);
        var connection = new MirConnection(99991, listener.AcceptTcpClient());
        try
        {
            var player = new Player { Info = new CharacterInfo { Index = 99991, Name = "任务栏检查", CreationIP = "127.0.0.1", Heroes = new HeroInfo[1] }, Account = new AccountInfo(), Connection = connection, Stats = new Stats() };
            player.Info.Mount = new MountInfo(player);
            var task = new UserItem(quest) { UniqueID = 100, Count = 3 };
            var ordinary = new UserItem(meat) { UniqueID = 101, Count = 2 };
            player.Info.Inventory[0] = task; player.Info.Inventory[1] = ordinary;
            player.OrganizeQuestInventory(false);
            check(player.Info.Inventory[0] == null && player.Info.Inventory[1] == ordinary && player.Info.QuestInventory[0] == task && player.CurrentBagWeight == ordinary.Weight,
                "登录未归类专用任务物品，或误移动普通材料/未释放负重");
            check(player.Packets.Count == 0, "登录整理在UserInformation之前发出了物品增删包");
            player.OrganizeQuestInventory(false);
            check(task.Count == 3 && player.Info.QuestInventory.Count(item => item != null) == 1, "重复登录整理复制了任务物品");
            for (var index = 0; index < player.Info.Inventory.Length; index++) player.Info.Inventory[index] = new UserItem(meat) { UniqueID = (ulong)(200 + index) };
            var gained = new UserItem(quest) { UniqueID = 300, Count = 2 };
            check(player.CanGainItem(gained) && player.CanGainItems([gained]), "普通背包满时错误拒绝任务专用品");
            player.Packets.Clear(); player.GainItem(gained);
            check(task.Count == 5 && player.Packets.OfType<S.GainedQuestItem>().Single().Item.Count == 2 && !player.Packets.OfType<S.GainedItem>().Any() &&
                player.Info.Inventory.All(item => item.Info == meat), "普通获取路径没有直接进入任务栏或回执数量错误");
            var state = new GameState(); state.Apply(new S.NewItemInfo { Info = quest });
            state.Apply(new S.UserInformation { ObjectID = 1, Inventory = new UserItem[46], Equipment = new UserItem[14], QuestInventory = new UserItem[40] });
            foreach (var packet in player.Packets) state.Apply(packet);
            check(state.User!.Inventory.All(item => item == null) && state.User.QuestInventory[0].Count == 2, "客户端任务回执占用了普通背包");
            for (var index = 0; index < player.Info.QuestInventory.Length; index++)
                player.Info.QuestInventory[index] = new UserItem(quest) { UniqueID = (ulong)(400 + index), Count = quest.StackSize };
            player.Info.QuestInventory[0].Count = (ushort)(quest.StackSize - 1);
            check(!player.CanGainItems([new UserItem(quest), new UserItem(quest)]) && player.Info.QuestInventory[0].Count == quest.StackSize - 1,
                "批量任务奖励复用了同一份合堆容量或预检查修改了资产");
            player.Info.QuestInventory[0].Count = quest.StackSize;
            var blocked = new UserItem(quest) { UniqueID = 500 };
            player.Info.Inventory[0] = blocked; player.Packets.Clear(); player.OrganizeQuestInventory();
            check(player.Info.Inventory[0] == blocked && player.Packets.OfType<S.Chat>().Any(packet => packet.Message.Contains("保留在背包")) &&
                !player.CanGainItem(gained), "任务栏满时整理丢失物品或没有明确拒绝");
            player.Info.QuestInventory[0] = null; player.Packets.Clear(); player.OrganizeQuestInventory();
            check(player.Info.Inventory[0] == null && player.Info.QuestInventory[0].UniqueID == 500 &&
                player.Packets[0] is S.DeleteItem && player.Packets[1] is S.GainedQuestItem, "仓库/交易回执后自动归类丢失编号或回执顺序错误");
            player.RecalculateQuestBag(new QuestInfo());
            check(player.Info.QuestInventory[0]?.UniqueID == 500, "完成无关任务删除了自动归类的专用品");
            var page = new NPCPage("[@MAIN]"); var segment = new NPCSegment(page, [], [], [], [], []);
            segment.ParseCheck("CHECKITEM 任务专用品 1");
            check(segment.Check(player), "NPC CHECKITEM无法读取任务栏中的专用品");
            player.Packets.Clear(); segment.ParseAct(segment.ActList, "TAKEITEM 任务专用品 1"); segment.Check(player);
            check(player.Info.QuestInventory[0] == null && player.Packets.OfType<S.DeleteQuestItem>().Any(), "NPC TAKE没有从任务栏交付专用品");
            using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) player.Info.Save(writer);
            stream.Position = 0; using var reader = new BinaryReader(stream);
            var restored = new CharacterInfo(reader, Envir.Version, Envir.CustomVersion);
            check(restored.QuestInventory.Sum(item => item?.Count ?? 0) == player.Info.QuestInventory.Sum(item => item?.Count ?? 0) &&
                restored.QuestInventory.Select(item => item?.UniqueID).SequenceEqual(player.Info.QuestInventory.Select(item => item?.UniqueID)) &&
                restored.Inventory.Select(item => item?.UniqueID).SequenceEqual(player.Info.Inventory.Select(item => item?.UniqueID)), "原角色存档往返丢失任务栏/背包编号或数量");
        }
        finally { connection.Disconnect(20); Envir.Main.ItemInfoList = original; }
    }
}
