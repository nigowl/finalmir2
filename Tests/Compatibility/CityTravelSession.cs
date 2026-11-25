using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

internal static class CityTravelSession
{
    public static async Task Run(string[] args)
    {
        var offset = Array.IndexOf(args, "--city-travel-tcp") + 1;
        var port = int.Parse(args[offset]);
        using var catalog = JsonDocument.Parse(File.ReadAllText(args[offset + 1]));
        var cities = catalog.RootElement.GetProperty("cities").EnumerateArray().ToArray();
        var fields = catalog.RootElement.GetProperty("fields").EnumerateArray().ToArray();
        var account = args[offset + 2];
        var field = int.Parse(args[offset + 3]);
        var verify = args.Contains("--verify-city-travel");
        using var connection = new GameConnection();
        var state = new GameState();
        var packets = new List<Packet>();
        void Pump()
        {
            while (connection.Received.TryDequeue(out var packet))
            {
                state.Apply(packet);
                packets.Add(packet);
            }
        }
        async Task<T> Receive<T>() where T : Packet
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(12))
            {
                Pump();
                if (packets.OfType<T>().FirstOrDefault() is { } expected) return expected;
                if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                await Task.Delay(15);
            }
            throw new TimeoutException($"缺少 {typeof(T).Name} 回执，当前位置 {state.Map?.MapIndex} {state.User?.Location}。");
        }
        void Require(bool value, string failure) { if (!value) throw new InvalidDataException(failure); }
        static Point ReadPoint(JsonElement root, string key)
        {
            var point = root.GetProperty(key);
            return new(point.GetProperty("x").GetInt32(), point.GetProperty("y").GetInt32());
        }
        async Task<T> Send<T>(Packet packet) where T : Packet
        {
            // 保持原服每五秒的数据包上限，连续点选不触发连接保护。
            await Task.Delay(180);
            Pump();
            packets.Clear();
            await connection.Send(packet);
            return await Receive<T>();
        }
        await connection.Connect("127.0.0.1", port);
        await Receive<S.Connected>();
        await Send<S.ClientVersion>(new C.ClientVersion { VersionHash = [] });
        if (!verify && !args.Contains("--existing-city-account"))
            Require((await Send<S.NewAccount>(new C.NewAccount { AccountID = account, Password = "Travel123" })).Result == 8,
                "隔离测试账号注册失败。");
        var login = await Send<S.LoginSuccess>(new C.Login { AccountID = account, Password = "Travel123" });
        var id = verify || args.Contains("--existing-city-character") ? login.Characters.Single().Index : (await Send<S.NewCharacterSuccess>(new C.NewCharacter {
            Name = account, Class = MirClass.Warrior, Gender = MirGender.Male })).CharInfo.Index;
        await Send<S.UserInformation>(new C.StartGame { CharacterIndex = id });
        await Receive<S.MapInformation>();
        var originalGold = state.User!.Gold;
        int Scrolls() => state.User!.Inventory.Where(item => item?.Info.Type == ItemType.Scroll && item.Info.Shape == 1).Sum(item => item!.Count);
        string Inventory() => JsonSerializer.Serialize(state.User!.Inventory.Where(item => item != null)
            .Select(item => new { item.UniqueID, item.ItemIndex, item.Count }).OrderBy(item => item.UniqueID));
        string Snapshot() => JsonSerializer.Serialize(new { state.Map!.MapIndex, state.User!.Location.X, state.User.Location.Y,
            state.User.Gold, Inventory = Inventory() });
        var record = args.FirstOrDefault(a => a.StartsWith("--city-travel-record="))?["--city-travel-record=".Length..];
        int city = 0;
        async Task<uint> Guide(int index)
        {
            var point = ReadPoint(cities[index], "npc");
            for (var attempt = 0; attempt < 100 && !state.Actors.Values.Any(a => a.Kind == ObjectType.Merchant && a.Location == point); attempt++)
            { await Task.Delay(20); Pump(); }
            for (var step = 0; step < 8 && !Functions.InRange(state.User!.Location, point, 4); step++)
            {
                var direction = Functions.DirectionFromPoint(state.User.Location, point);
                await Send<S.UserLocation>(new C.Walk { Direction = direction });
                await Task.Delay(650);
            }
            var actor = state.Actors.Values.Single(a => a.Kind == ObjectType.Merchant && a.Location == point && !a.Hidden);
            return actor.Id;
        }
        Task<S.NPCResponse> Call(uint npc, string key) =>
            Send<S.NPCResponse>(new C.CallNPC { ObjectID = npc, Key = $"[@{key}]" });
        async Task Go(int target)
        {
            var npc = await Guide(city);
            var scrolls = Scrolls();
            var main = await Call(npc, "MAIN");
            Require(main.Page.Any(line => line.Contains($"<{cities[target].GetProperty("name").GetString()}/@CITY{target}>")),
                $"城镇目的地 {city}→{target} 缺少可见链接。实际对话 {string.Join(" / ", main.Page)}");
            S.MapChanged move;
            try { move = await Send<S.MapChanged>(new C.CallNPC { ObjectID = npc, Key = $"[@CITY{target}]" }); }
            catch (TimeoutException error) { throw new InvalidDataException($"城镇 {city}→{target} 失败：{string.Join(" / ", packets.OfType<S.NPCResponse>().SelectMany(p => p.Page))}", error); }
            Require(move.MapIndex == cities[target].GetProperty("mapIndex").GetInt32() &&
                move.Location == ReadPoint(cities[target], "landing"), $"城镇目的地 {city}→{target} 的原服落点错误。");
            Require((await Receive<S.NPCResponse>()).Page.Count == 0 && state.User!.Gold == originalGold && Scrolls() == Math.Max(1, scrolls),
                "城镇传送未关闭对话、意外扣费或重复赠卷。");
            city = target;
        }
        using var blocker = new GameConnection();
        if (args.Contains("--occupied-city"))
        {
            async Task<T> BlockerPacket<T>() where T : Packet
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(12))
                {
                    while (blocker.Received.TryDequeue(out var packet)) if (packet is T match) return match;
                    if (blocker.Errors.TryDequeue(out var error)) throw new IOException(error);
                    await Task.Delay(15);
                }
                throw new TimeoutException("占位角色缺少 " + typeof(T).Name);
            }
            await Task.Delay(5200);
            await blocker.Connect("127.0.0.1", port); await BlockerPacket<S.Connected>();
            await blocker.Send(new C.ClientVersion { VersionHash = [] }); await BlockerPacket<S.ClientVersion>();
            await blocker.Send(new C.Login { AccountID = "TravelOwned", Password = "Travel123" });
            var peer = await BlockerPacket<S.LoginSuccess>();
            await blocker.Send(new C.StartGame { CharacterIndex = peer.Characters.Single().Index });
            var occupant = await BlockerPacket<S.UserInformation>();
            Require(occupant.Location == ReadPoint(cities[1], "landing"), "占位角色不在比奇固定落点。");
            await Go(1);
            Require(state.User.Location == occupant.Location, "多人传送没有进入同一格。");
            await Go(0);
            await blocker.Send(new C.LogOut()); await BlockerPacket<S.LogOutSuccess>();
            Console.WriteLine("PASS 安全区外出发及原TCP多人同格城镇传送。");
        }
        if (verify)
        {
            Require(state.Map!.MapIndex == fields[field].GetProperty("mapId").GetInt32() &&
                state.User!.Location == ReadPoint(fields[field], "landing"), "正常保存重启后危险区坐标错误。");
            if (record != null) Require(Snapshot() == File.ReadAllText(record), "正常保存重启后金币或物品编号/数量错误。");
        }
        else
        {
            if (field == 0 && !args.Contains("--expect-city-rejection") && !args.Contains("--skip-city-loop"))
            {
                // 完全有向图的每条边进入原 CallNPC，包含同地图内两个不同城镇。
                var route = new List<int>();
                var used = new HashSet<(int, int)>();
                void Visit(int from)
                {
                    for (var to = 0; to < cities.Length; to++)
                        if (to != from && used.Add((from, to))) Visit(to);
                    route.Add(from);
                }
                Visit(0); route.Reverse();
                foreach (var stop in route.Skip(1)) await Go(stop);
                Console.WriteLine($"PASS {used.Count} 条城镇原TCP路线。");
            }
            var owner = Array.FindIndex(cities, c => c.GetProperty("mapIndex").GetInt32() == fields[field].GetProperty("region").GetInt32());
            if (owner != city) await Go(owner);
            var guide = await Guide(city);
            var menu = await Call(guide, "MAIN");
            Require(menu.Page.Any(line => line.Contains("@CITY") && line.Split("@CITY").Length > 2) &&
                !menu.Page.Any(line => line.Contains("@FIELDS")), "目的地没有并排显示或仍然分页。");
            for (var index = 0; index < fields.Length; index++)
                Require(menu.Page.Any(line => line.Contains($"@FIELD{index}>")) ==
                    (fields[index].GetProperty("region").GetInt32() == cities[city].GetProperty("mapIndex").GetInt32() ||
                     cities[city].GetProperty("mapIndex").GetInt32() == 77 && fields[index].GetProperty("region").GetInt32() == 64),
                    "本地危险区域目录错误。");
            // 直接请求确认页后，原服务端仍应保留主菜单并拒绝执行。
            var before = state.User.Location;
            await connection.Send(new C.CallNPC { ObjectID = guide, Key = $"[@GO{field}]" });
            menu = await Call(guide, "MAIN");
            Require(state.User.Location == before && state.Map!.MapIndex == cities[city].GetProperty("mapIndex").GetInt32(),
                "未显示的危险区确认链接被执行。");
            Require(menu.Page.Any(line => line.Contains($"@FIELD{field}>")), "当前打怪目的地未显示。");
            var fee = fields[field].GetProperty("gold").GetUInt32();
            var goldBefore = state.User.Gold;
            var inventoryBefore = Inventory();
            var scrollsBefore = Scrolls();
            var confirmation = await Call(guide, $"FIELD{field}");
            Require(confirmation.Page.Any(line => line.Contains("危险区域")) && confirmation.Page.Any(line => line.Contains($"@GO{field}>")) &&
                confirmation.Page.Any(line => line.Contains(fee.ToString("N0"))) &&
                state.User.Gold == goldBefore && Inventory() == inventoryBefore && !packets.OfType<S.MapChanged>().Any(),
                "打怪链接跳过风险确认、费用不符或提前改变资产。");
            // 返回列表等于取消，不产生扣费和道具。
            await Call(guide, "MAIN");
            Require(state.User.Gold == goldBefore && Inventory() == inventoryBefore, "取消传送改变资产。");
            await Call(guide, $"FIELD{field}");
            if (args.Contains("--expect-city-rejection"))
            {
                var response = await Call(guide, $"GO{field}");
                Require(response.Page.Count > 0 && state.User.Gold == goldBefore && Inventory() == inventoryBefore &&
                    state.User.Location == before && !packets.OfType<S.MapChanged>().Any(), "拒绝传送改变了资产或坐标。");
                Console.WriteLine($"PASS 拒绝传送 {account}：{string.Join(" ", response.Page)}");
                await Send<S.LogOutSuccess>(new C.LogOut()); return;
            }
            var result = await Send<S.MapChanged>(new C.CallNPC { ObjectID = guide, Key = $"[@GO{field}]" });
            Require(result.MapIndex == fields[field].GetProperty("mapId").GetInt32() && result.Location == ReadPoint(fields[field], "landing"),
                "原服危险区落点错误。");
            Require((await Receive<S.NPCResponse>()).Page.Count == 0 && state.User.Gold == goldBefore - fee &&
                Scrolls() == Math.Max(1, scrollsBefore) && packets.OfType<S.LoseGold>().Count() == 1,
                "传送未关闭对话、实扣费用错误或赠卷数量错误。");
            var earned = Inventory();
            await connection.Send(new C.CallNPC { ObjectID = guide, Key = $"[@GO{field}]" });
            await Task.Delay(300); Pump();
            Require(Inventory() == earned && state.User.Gold == goldBefore - fee, "重复确认再次扣费或发卷。");
        }
        if (record != null && !verify) File.WriteAllText(record, Snapshot());
        var destination = state.Map!.MapIndex;
        await Send<S.LogOutSuccess>(new C.LogOut());
        Console.WriteLine($"PASS 打怪区 {field}，地图 {destination}，重启复查 {verify}。");
    }
}
