using Crystal.Native;
using System.Diagnostics;
using System.Text.Json;
using Server.MirDatabase;
using Server.MirObjects;
using C = ClientPackets;
using S = ServerPackets;

internal static class ItemRecycleChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var preferences = new ClientPreferences { AutoSell = true };
        preferences.ItemRules["测试物品"] = ItemActions.Display | ItemActions.Recycle;
        check(preferences.ForItem("测试物品").HasFlag(ItemActions.Display) && !preferences.ForItem("测试物品").HasFlag(ItemActions.Pickup), "物品三列没有分别保存");
        var item = new UserItem(new ItemInfo { Name = "测试物品", Type = ItemType.Meat, Price = 100, StackSize = 100 }) { UniqueID = 7, Count = 10 };
        var player = new Player { Account = new AccountInfo { Gold = 100 }, Info = new CharacterInfo { HP = 100 } };
        player.Info.Inventory[0] = item;
        var request = new C.RecycleItem { RequestId = 1, UniqueID = 7, Count = 3 };
        player.RecycleItem(request);
        var result = player.Packets.OfType<S.RecycleItemResult>().Last();
        check(result.Success && item.Count == 7 && player.Account.Gold == 250 && result.Gold == 150, "远程回收没有按真实数量/半价结算");
        player.RecycleItem(request);
        check(item.Count == 7 && player.Account.Gold == 250 && player.Packets.OfType<S.DeleteItem>().Count() == 1, "重复回收重复销毁物品或增加金币");
        void Refused(int id, ushort count)
        {
            var before = player.Account.Gold; var number = item.Count;
            player.RecycleItem(new C.RecycleItem { RequestId = id, UniqueID = 7, Count = count });
            check(!player.Packets.OfType<S.RecycleItemResult>().Last().Success && player.Account.Gold == before && item.Count == number, "回收拒绝后资产变化");
        }
        Refused(2, 0); Refused(3, 8);
        item.Info.Bind = BindMode.DontSell; Refused(4, 1); item.Info.Bind = BindMode.None;
        item.Info.Type = ItemType.Quest; Refused(5, 1); item.Info.Type = ItemType.Meat;
        item.AddedStats[Stat.MaxDC] = 1; Refused(6, 1); item.AddedStats.Clear();
        player.Dead = true; Refused(7, 1); player.Dead = false;
        player.Account.Gold = uint.MaxValue; Refused(8, 1); player.Account.Gold = 250;
        player.RecycleItem(new C.RecycleItem { RequestId = 9, UniqueID = 7, Count = 7 });
        check(player.Info.Inventory[0] == null && player.Account.Gold == 600, "整叠回收未清空唯一编号或金额错误");
        var wasServer = Packet.IsServer;
        try
        {
            Packet.IsServer = true;
            var received = (C.RecycleItem)Packet.ReceivePacket(request.GetPacketBytes().ToArray(), out var extra);
            check(received.UniqueID == 7 && received.Count == 3 && received.RequestId == 1 && extra.Length == 0, "回收请求协议字段错误");
            Packet.IsServer = false;
            var reply = (S.RecycleItemResult)Packet.ReceivePacket(result.GetPacketBytes().ToArray(), out extra);
            check(reply.Success && reply.Gold == 150 && reply.UniqueID == 7 && extra.Length == 0, "回收结果协议字段错误");
        }
        finally { Packet.IsServer = wasServer; }
    }

    public static async Task VerifySaved(int port, string record)
    {
        if (!new Mir2.Hosting.ProjectPaths().Data.Contains("mir2-actions.", StringComparison.Ordinal))
            throw new InvalidOperationException("回收恢复检查仅允许隔离版本。");
        Packet.IsServer = false;
        using var connection = new GameConnection();
        var state = new GameState(); var packets = new List<Packet>();
        async Task Until(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                while (connection.Received.TryDequeue(out var packet)) { state.Apply(packet); packets.Add(packet); }
                if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                if (timer.ElapsedMilliseconds > 15000) throw new TimeoutException("回收恢复检查未收到原TCP回执。");
                await Task.Delay(20);
            }
        }
        await connection.Connect("127.0.0.1", port); await Until(() => packets.OfType<S.Connected>().Any());
        await connection.Send(new C.ClientVersion { VersionHash = [] }); await Until(() => packets.OfType<S.ClientVersion>().Any());
        await connection.Send(new C.Login { AccountID = "NativeWizard", Password = "Native123" }); await Until(() => state.Characters.Count > 0);
        await connection.Send(new C.StartGame { CharacterIndex = state.Characters.Single().Index }); await Until(() => state.User != null);
        var snapshot = JsonSerializer.Serialize(new { state.User!.Gold, Items = state.User.Inventory.Where(item => item != null).OrderBy(item => item.UniqueID).Select(item => new { item.UniqueID, item.ItemIndex, item.Count }) });
        if (snapshot != File.ReadAllText(record)) throw new Exception("回收后的金币、物品唯一编号及数量未完整保存恢复。");
        await connection.Send(new C.LogOut()); await Until(() => packets.OfType<S.LogOutSuccess>().Any());
        Console.WriteLine("PASS recycling original TCP restart: gold, inventory IDs/counts and protected items preserved.");
    }
}
