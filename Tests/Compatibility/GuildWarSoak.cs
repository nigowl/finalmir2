using System.Diagnostics;
using System.Text.Json;
using Crystal.Native;
using Mir2.Hosting;
using C = ClientPackets;
using S = ServerPackets;

internal static class GuildWarSoak
{
    public static async Task Run(int port, int seconds, string record)
    {
        var paths = new ProjectPaths();
        if (!paths.Data.Contains("/mir2-guild-war.", StringComparison.Ordinal) || port == 7000 || seconds is < 0 or > 86400)
            throw new InvalidOperationException("多人持续检查仅允许独立行会战争夹具和端口。");
        var peers = new List<Peer>(); var clock = Stopwatch.StartNew(); var started = DateTimeOffset.UtcNow;
        try
        {
            for (var i = 0; i < 16; i++)
            {
                if (i > 0) await Task.Delay(5200);
                var peer = new Peer(); peers.Add(peer); await peer.Login(port, i);
            }
            foreach (var peer in peers) peer.Drain();
            if (seconds == 0)
            {
                using var saved = JsonDocument.Parse(File.ReadAllText(record));
                if (saved.RootElement.GetProperty("Peers").GetRawText() != JsonSerializer.Serialize(peers.Select(peer => peer.Snapshot())))
                    throw new InvalidDataException("多人持续检查后完整重启改变身份、坐标、行会资金或物品编号/数量/耐久。");
                Console.WriteLine("PASS original TCP 16-player identities, positions, personal/guild gold and item IDs/counts/durability survive complete restart.");
            }
            else
            {
                var baseline = peers.Select(peer => peer.Snapshot()).ToArray();
                var running = Stopwatch.StartNew(); var nextSample = TimeSpan.Zero; var up = true; var sweeps = 0; var moves = 0; var chats = 0;
                while (running.Elapsed.TotalSeconds < seconds)
                {
                    var order = up ? peers.OrderBy(peer => peer.State.User!.Location.Y).ThenBy(peer => peer.State.User!.Location.X) :
                        peers.OrderByDescending(peer => peer.State.User!.Location.Y).ThenBy(peer => peer.State.User!.Location.X);
                    foreach (var peer in order.ToArray())
                    {
                        peer.Drain(); var location = peer.State.User!.Location;
                        var expected = new System.Drawing.Point(location.X, location.Y + (up ? -1 : 1)); peer.Packets.Clear();
                        await peer.Connection.Send(new C.Walk { Direction = up ? MirDirection.Up : MirDirection.Down });
                        await Wait(() => { foreach (var client in peers) client.Drain(); return peer.State.User.Location == expected; }, 5000);
                        moves++;
                    }
                    up = !up; sweeps++;
                    if (running.Elapsed >= nextSample)
                    {
                        foreach (var peer in peers)
                        {
                            peer.Drain();
                            if (!peer.Connection.Connected || peer.State.User?.HP <= 0 || !peer.State.InGuild)
                                throw new IOException("多人持续检查出现意外断线、死亡或会籍丢失。");
                        }
                        var sample = new { StartedUtc = started, LoginSeconds = clock.Elapsed.TotalSeconds - running.Elapsed.TotalSeconds,
                            Seconds = running.Elapsed.TotalSeconds, Connections = peers.Count, Sweeps = sweeps, Moves = moves, Chats = chats,
                            Packets = peers.Sum(peer => peer.PacketCount), QueuePeak = peers.Max(peer => peer.QueuePeak),
                            Peers = peers.Select(peer => peer.Snapshot()).ToArray() };
                        var json = JsonSerializer.Serialize(sample); File.WriteAllText(record + ".progress.tmp", json); File.Move(record + ".progress.tmp", record + ".progress.json", true);
                        Console.WriteLine($"SOAK {sample.Seconds:F0}s/{seconds}s, {peers.Count} clients, {moves} acknowledged moves, {sample.Packets} packets, queue peak {sample.QueuePeak}.");
                        var sender = peers[chats % peers.Count]; var text = $"多人持续检查 {chats + 1}";
                        await sender.Connection.Send(new C.Chat { Message = "!~" + text });
                        await Wait(() => { foreach (var peer in peers) peer.Drain(); return sender.Packets.OfType<S.Chat>().Any(packet => packet.Type == ChatType.Guild && packet.Message.Contains(text)); });
                        chats++;
                        nextSample = running.Elapsed + TimeSpan.FromMinutes(1);
                    }
                    foreach (var peer in peers) peer.Packets.Clear();
                    await Task.Delay(PlayerActionTiming.Default.MoveMilliseconds + 100);
                }
                // 结束在最初格位，再验证所有原资产；不把走路恢复生命当成资产漂移。
                if (!up)
                    foreach (var peer in peers.OrderByDescending(peer => peer.State.User!.Location.Y).ToArray())
                    {
                        var expected = new System.Drawing.Point(peer.State.User!.Location.X, peer.State.User.Location.Y + 1);
                        await peer.Connection.Send(new C.Walk { Direction = MirDirection.Down });
                        await Wait(() => { foreach (var client in peers) client.Drain(); return peer.State.User.Location == expected; }, 5000); moves++;
                    }
                if (JsonSerializer.Serialize(baseline) != JsonSerializer.Serialize(peers.Select(peer => peer.Snapshot())))
                    throw new InvalidDataException("持续走路/聊天改变了身份、初始位置、会籍、资金或物品资产。");
                File.WriteAllText(record, JsonSerializer.Serialize(new { StartedUtc = started, CompletedUtc = DateTimeOffset.UtcNow,
                    Seconds = running.Elapsed.TotalSeconds, Connections = peers.Count, Sweeps = sweeps, Moves = moves, Chats = chats,
                    Packets = peers.Sum(peer => peer.PacketCount), QueuePeak = peers.Max(peer => peer.QueuePeak), Peers = peers.Select(peer => peer.Snapshot()).ToArray() }));
                Console.WriteLine($"PASS original TCP multiplayer soak: {running.Elapsed.TotalSeconds:F1}s, {peers.Count} clients, {moves} acknowledged moves; no unexpected disconnection or asset drift.");
            }
            foreach (var peer in peers) { peer.Packets.Clear(); await peer.Connection.Send(new C.LogOut()); await peer.Next<S.LogOutSuccess>(); }
            Console.WriteLine("PASS all 16 clients received original LogOutSuccess.");
        }
        finally { foreach (var peer in peers) peer.Connection.Dispose(); }
    }

    private static async Task Wait(Func<bool> ready, int milliseconds = 30000)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        { if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException("原TCP多人检查等待回执超时。"); await Task.Delay(10); }
    }

    private sealed class Peer
    {
        public readonly GameConnection Connection = new();
        public readonly GameState State = new();
        public readonly List<Packet> Packets = [];
        public long PacketCount; public int QueuePeak;
        public void Drain()
        {
            QueuePeak = Math.Max(QueuePeak, Connection.Received.Count);
            while (Connection.Received.TryDequeue(out var packet)) { PacketCount++; Packets.Add(packet); State.Apply(packet); }
            if (Connection.Errors.TryDequeue(out var error)) throw new IOException(error);
            if (Packets.Count > 8192) throw new IOException("持续检查自身未及时消费数据包。");
        }
        public async Task<T> Next<T>() where T : Packet
        {
            await Wait(() => { Drain(); return Packets.Any(packet => packet is T); });
            var result = Packets.OfType<T>().First(); Packets.Remove(result); return result;
        }
        public async Task Login(int port, int index)
        {
            await Connection.Connect("127.0.0.1", port); await Next<S.Connected>();
            await Connection.Send(new C.ClientVersion { VersionHash = [] }); await Next<S.ClientVersion>();
            await Connection.Send(new C.Login { AccountID = $"WarClient{index:00}", Password = "Native123" });
            await Connection.Send(new C.StartGame { CharacterIndex = (await Next<S.LoginSuccess>()).Characters.Single().Index });
            await Next<S.UserInformation>(); await Wait(() => { Drain(); return State.InGuild; });
            Packets.Clear();
        }
        public object Snapshot() => new { State.User!.Name, State.User.RealId, State.User.Location, State.User.Gold,
            State.User.GuildName, GuildGold = State.Guild!.Gold,
            Items = State.User.Inventory.Concat(State.User.Equipment).Where(item => item != null).Select(item => new { item.UniqueID, item.Count, item.CurrentDura }).ToArray() };
    }
}
