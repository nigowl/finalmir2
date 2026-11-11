using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crystal.Native;
using Mir2.Management;
using C = ClientPackets;
using S = ServerPackets;

internal static class TradeRecoverySession
{
    public static async Task Run(string[] args)
    {
        var offset = Array.IndexOf(args, "--trade-recovery") + 1;
        var mode = args[offset]; var port = int.Parse(args[offset + 1]); var address = args[offset + 2]; var record = args[offset + 3];
        Packet.IsServer = false;
        using var left = new Session(); using var right = new Session();
        await left.Login(port, "TradeLeft"); await Task.Delay(5200); await right.Login(port, "TradeRight");
        string Snapshot() => JsonSerializer.Serialize(new[] { left, right }.Select(s => new { s.State.User!.Name, s.State.User.Gold,
            items = s.State.User.Inventory.Where(i => i != null).OrderBy(i => i.UniqueID).Select(i => new { i.UniqueID, i.ItemIndex, i.Count }) }));
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("MIR2_RUNTIME_ROOT")!, "admin.key")).Trim());
        var guild = (await http.GetFromJsonAsync<GuildRow[]>("/management/admin/guilds"))!.Single(g => g.Name == "NativeGuild");
        if (guild.Members != 1 || guild.Leader != "NativeGuildLead") throw new Exception("Trade recovery lost the existing guild or its leader.");
        async Task Post(string path, object? body = null)
        {
            using var response = await http.PostAsJsonAsync(path, body ?? new { }); response.EnsureSuccessStatusCode();
        }
        if (mode == "verify")
        {
            if (Snapshot() != File.ReadAllText(record)) throw new Exception("Forced-exit recovery changed item IDs/counts or either player's gold.\n" + Snapshot());
            if (left.State.Trading || right.State.Trading) throw new Exception("Recovered an obsolete trading session.");
            await Post("/management/save");
            Console.WriteLine("PASS trade forced-exit recovery: both accounts, item IDs/counts, gold and cleared sessions match the pre-trade state; existing guild and leader retained."); return;
        }
        if (mode != "prepare") throw new ArgumentException("Unknown trade recovery mode.");
        await Post($"/management/admin/characters/{right.State.User!.RealId}/travel", new { mapId = left.State.Map!.MapIndex, x = left.State.User!.Location.X, y = left.State.User.Location.Y + 1, setBind = false, reason = "Isolated trade crash regression", confirmation = "TradeRight" });
        await right.Until(() => right.State.User!.Location.Y == left.State.User!.Location.Y + 1);
        await left.Connection.Send(new C.Turn { Direction = MirDirection.Down }); await right.Connection.Send(new C.Turn { Direction = MirDirection.Up });
        await right.Connection.Send(new C.ChangeTrade { AllowTrade = true });
        await left.Until(() => left.State.User!.Direction == MirDirection.Down); await right.Until(() => right.State.User!.Direction == MirDirection.Up);
        await Task.Delay(300); File.WriteAllText(record, Snapshot());
        await left.Connection.Send(new C.TradeRequest()); await right.Until(() => right.State.TradeInvitation.Length > 0);
        await right.Connection.Send(new C.TradeReply { AcceptInvite = true }); await left.Until(() => left.State.Trading); await right.Until(() => right.State.Trading);
        var leftSlot = Array.FindIndex(left.State.User!.Inventory, i => i?.Info?.Type == ItemType.Weapon);
        var rightSlot = Array.FindIndex(right.State.User!.Inventory, i => i?.Info?.Type == ItemType.Weapon);
        if (leftSlot < 0 || rightSlot < 0) throw new Exception("Trade fixture did not initialize weapons.");
        await left.Connection.Send(new C.DepositTradeItem { From = leftSlot, To = 0 }); await right.Connection.Send(new C.DepositTradeItem { From = rightSlot, To = 0 });
        await left.Connection.Send(new C.TradeGold { Amount = 11 }); await right.Connection.Send(new C.TradeGold { Amount = 23 });
        await left.Until(() => left.State.TradeItems[0] != null && left.State.GuestTradeGold == 23);
        await right.Until(() => right.State.TradeItems[0] != null && right.State.GuestTradeGold == 11);
        await Post("/management/save");
        File.WriteAllText(record + ".ready", "Both clients hold escrow in the durable snapshot.");
        var timer = Stopwatch.StartNew();
        while (left.Connection.Connected || right.Connection.Connected)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Fixture coordinator did not terminate the test server.");
            await Task.Delay(100);
        }
        Console.WriteLine("PASS trade crash checkpoint: two live escrows saved before forced server termination.");
    }
    private sealed class Session : IDisposable
    {
        public readonly GameConnection Connection = new();
        public readonly GameState State = new();
        private readonly List<Packet> packets = new();
        public async Task Login(int port, string name)
        {
            await Connection.Connect("127.0.0.1", port); await Packet<S.Connected>();
            await Connection.Send(new C.ClientVersion { VersionHash = [] }); await Packet<S.ClientVersion>();
            await Connection.Send(new C.Login { AccountID = name, Password = "Trade123" }); await Packet<S.LoginSuccess>();
            await Connection.Send(new C.StartGame { CharacterIndex = State.Characters.Single(c => c.Name == name).Index }); await Packet<S.UserInformation>();
        }
        private async Task<T> Packet<T>() where T : Packet
        {
            await Until(() => packets.Any(p => p is T)); var packet = packets.OfType<T>().First(); packets.Remove(packet); return packet;
        }
        public async Task Until(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                while (Connection.Received.TryDequeue(out var packet)) { State.Apply(packet); packets.Add(packet); }
                if (condition()) return;
                if (Connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                if (timer.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException("Trade recovery condition was not reached.");
                await Task.Delay(20);
            } while (true);
        }
        public void Dispose() => Connection.Dispose();
    }
}
