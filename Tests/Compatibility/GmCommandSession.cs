using System.Diagnostics;
using System.Text.Json;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

internal static class GmCommandSession
{
    public static async Task Run(string[] args)
    {
        var offset = Array.IndexOf(args, "--gm-checks") + 1;
        var port = int.Parse(args[offset]); var record = args[offset + 1];
        Packet.IsServer = false;
        using var connection = new GameConnection();
        var state = new GameState(); var packets = new List<Packet>();
        async Task Until(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                while (connection.Received.TryDequeue(out var packet))
                {
                    if (packet is S.GainedItem gained) state.Bind(gained.Item);
                    var observed = packet is S.GainedItem item ? new S.GainedItem { Item = item.Item.Clone() } : packet;
                    state.Apply(packet); packets.Add(observed);
                }
                if (condition()) return;
                if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                if (timer.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException("GM原TCP条件未满足。最近聊天：" + string.Join(" / ", packets.OfType<S.Chat>().TakeLast(5).Select(chat => chat.Message)));
                await Task.Delay(20);
            } while (true);
        }
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Command(string command, string expected)
        {
            packets.Clear(); await connection.Send(new C.Chat { Message = command });
            await Until(() => packets.OfType<S.Chat>().Any(chat => chat.Message.Contains(expected)));
        }
        await connection.Connect("127.0.0.1", port); await Until(() => packets.OfType<S.Connected>().Any());
        await connection.Send(new C.ClientVersion { VersionHash = [] }); await Until(() => packets.OfType<S.ClientVersion>().Any());
        await connection.Send(new C.Login { AccountID = "NativeGM", Password = "Native123" }); await Until(() => packets.OfType<S.LoginSuccess>().Any());
        await connection.Send(new C.StartGame { CharacterIndex = state.Characters.Single(character => character.Name == "NativeGM").Index });
        await Until(() => state.User != null && state.Map != null);
        string Snapshot() => JsonSerializer.Serialize(state.User!.Inventory.Where(item => item != null).Select(item => new { item.UniqueID, item.ItemIndex, item.Count }).OrderBy(item => item.UniqueID));
        if (args.Contains("--verify-gm"))
        {
            Check(Snapshot() == File.ReadAllText(record), "合堆后的物品编号/数量未在完整重启后保留。");
        }
        else
        {
            await Command("@books Warrior", "发放完成");
            var books = packets.OfType<S.GainedItem>().Select(packet => packet.Item).ToArray();
            Check(books.Length > 0 && books.All(book => book.Info.Type == ItemType.Book && book.Info.RequiredClass.HasFlag(RequiredClass.Warrior) && book.Count == 1) &&
                books.Select(book => book.Info.Shape).Distinct().Count() == books.Length && state.User!.Magics.Count == 0, "GM命令没有发放职业书籍或意外自动学习。");
            await Command("@cb", "命令已处理");
            await Command("@mk NativeEquipFixture 46", "命令已处理");
            Check(state.User!.Inventory.Count(item => item != null) == 46 && state.User.Inventory.All(item => item.Count == 1), "装备被合成一叠或件数丢失。");
            var full = Snapshot();
            await Command("@获取职业技能书 法师", "未发放");
            Check(Snapshot() == full && !packets.OfType<S.GainedItem>().Any() && state.User.Magics.Count == 0, "满包拒绝时部分发书或修改了技能。");
            await Command("@cb", "命令已处理");
            await Command("@mk NativeStackMeat 51", "命令已处理");
            await Command("@mk NativeStackMeat 50", "命令已处理");
            Check(state.User.Inventory.Count(item => item != null) == 1 && state.User.Inventory.Single(item => item != null).Count == 101, "两次获得的普通肉没有在服务端/客户端自动合为101份。");
            await Command("@mk 不存在的物品", "物品不存在");
            Check(packets.OfType<S.Chat>().Any(chat => chat.Message.Contains("例子：")), "有权限的物品错误没有命令例子。");
            await Command("@mm 不存在的地图", "地图不存在");
            await Command("@books 未知职业", "职业错误");
            var start = state.User.Location;
            await Command($"@mm \"Native Test Return\" {start.X} {start.Y}", "0");
            await Until(() => state.Map!.MapIndex == 2);
            await Command($"@mm 0 {start.X} {start.Y}", "0"); await Until(() => state.Map!.MapIndex == 1);
            File.WriteAllText(record, Snapshot());
        }
        await connection.Send(new C.LogOut()); await Until(() => packets.OfType<S.LogOutSuccess>().Any());
        Console.WriteLine(args.Contains("--verify-gm") ? "PASS GM original TCP restart: stacked item IDs/counts preserved." :
            "PASS GM original TCP: class books are inventory items, no automatic learning, atomic full-bag rejection, 46 individual equipment items, 101 stacked meat, detailed errors and name/code map travel.");
    }
}
