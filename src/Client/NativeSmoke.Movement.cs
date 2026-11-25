using Godot;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyMovementOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("跑步检查必须使用隔离原生fixture。");
        await EnterUiFixture();
        await ProfileMovement();
        var start = State.User!.Location;
        var direction = Enum.GetValues<MirDirection>().First(dir =>
            Enumerable.Range(0, 12).All(step => Client.World.Walkable(Functions.PointMove(start, dir, step))) &&
            Enumerable.Range(0, 12).All(step => Client.World.Walkable(Functions.PointMove(Functions.PointMove(start, dir, step), Functions.NextDir(Functions.NextDir(dir)), 1))));
        var goal = Functions.PointMove(start, direction, 4);
        stage = "turn respects original one-step restart then resumes running";
        Client.SetDestination(start, false);
        var acknowledged = packets.OfType<S.UserLocation>().Count();
        Client.Send(new C.Turn { Direction = direction });
        await Until(() => packets.OfType<S.UserLocation>().Count() > acknowledged);
        var distances = new List<int>(); var previous = State.User.Location;
        void Received(Packet packet)
        {
            if (packet is not S.UserLocation location) return;
            distances.Add(Functions.MaxDistance(previous, location.Location)); previous = location.Location;
        }
        Client.PacketReceived += Received;
        try
        {
            Client.SetDestination(goal, true); await Until(() => State.User.Location == goal);
            if (distances.FirstOrDefault() != 1 || !distances.Contains(2) || distances.Any(distance => distance == 0))
                throw new Exception("转身后没有按原规则一步起跑或移动被拒绝：" + string.Join(',', distances));
            GD.Print("PASS native run qualification: original turn requires exactly one restart step, then two-cell run with no rejected movement.");
            Client.SetDestination(start, true); await Until(() => State.User.Location == start);
            await VerifyChasingPlayer(start, direction, distances);
        }
        finally { Client.PacketReceived -= Received; }
        await Client.Quit();
    }

    private async Task VerifyChasingPlayer(Point start, MirDirection direction, List<int> distances)
    {
        stage = "second original TCP player for run-mode pursuit";
        using var bot = new GameConnection(); var other = new GameState(); var received = new List<Packet>();
        void Drain()
        {
            while (bot.Received.TryDequeue(out var packet)) { other.Apply(packet); received.Add(packet); }
            if (bot.Errors.TryDequeue(out var error)) throw new IOException(error);
        }
        async Task BotUntil(Func<bool> condition) => await Until(() => { Drain(); return condition(); });
        await bot.Connect(Client.Assets.Host, Client.Assets.GamePort);
        await BotUntil(() => received.OfType<S.Connected>().Any());
        await bot.Send(new C.ClientVersion { VersionHash = [] }); await BotUntil(() => received.OfType<S.ClientVersion>().Any());
        await bot.Send(new C.Login { AccountID = "NativeGM", Password = "Native123" }); await BotUntil(() => received.OfType<S.LoginSuccess>().Any());
        await bot.Send(new C.StartGame { CharacterIndex = other.Characters.Single(character => character.Name == "NativeGM").Index });
        await BotUntil(() => other.User != null);
        var side = Functions.NextDir(Functions.NextDir(direction));
        Point Target(int distance) => Functions.PointMove(Functions.PointMove(start, direction, distance), side, 1);
        async Task Place(int distance, bool offset = true)
        {
            var point = offset ? Target(distance) : Functions.PointMove(start, direction, distance);
            await bot.Send(new C.Chat { Message = $"@mv {point.X} {point.Y}" });
            await BotUntil(() => other.User!.Location == point && State.Actors.GetValueOrDefault(other.User.ObjectID)?.Location == point);
        }
        void Mode(bool run)
        {
            var settings = SettingsPanel(); ((CheckBox)settings.FindChild("MovementMode", true, false)).ButtonPressed = run;
            CloseSettingsPanel(settings);
        }
        await Place(3); Mode(false); Client.SetDestination(start, true);
        distances.Clear(); Client.SelectTarget(other.User!.ObjectID);
        await Until(() => Functions.MaxDistance(State.User!.Location, other.User.Location) <= 1);
        if (distances.Count == 0 || distances.Any(distance => distance != 1)) throw new Exception("走路模式选中目标后仍跑步。");
        Client.SetDestination(start, true); await Until(() => State.User!.Location == start);
        Mode(true); await Place(3); distances.Clear();
        // 刻意先选走路目的地，确认选怪会重新读取当前设置，而不是继承旧动作。
        Client.SetDestination(start, false); Client.SelectTarget(other.User.ObjectID);
        await Until(() => Functions.MaxDistance(State.User!.Location, other.User.Location) <= 1);
        if (distances.FirstOrDefault() != 2 || distances.Any(distance => distance == 0))
            throw new Exception("跑步模式追击仍沿用走路或选择最近一步格：" + string.Join(',', distances));
        GD.Print("PASS native target modes: explicit walk stays one-cell; switching to run overrides prior walk and reaches an adjacent cell by running.");
        Client.SetDestination(start, true); await Until(() => State.User!.Location == start);
        await Place(2, false);
        var pursuitStart = Functions.PointMove(start, direction, 1);
        Client.SetDestination(pursuitStart, true); await Until(() => State.User!.Location == pursuitStart);
        stage = "running closes distance to an actually walking target";
        var targetStart = other.User.Location;
        await bot.Send(new C.Walk { Direction = direction }); await BotUntil(() => other.User.Location != targetStart);
        distances.Clear(); Client.SetDestination(start, false); Client.SelectTarget(other.User.ObjectID);
        var started = Environment.TickCount64; var nextWalk = started + 620; var walks = 1;
        while (walks < 5 || Functions.MaxDistance(State.User!.Location, other.User.Location) > 1)
        {
            Drain();
            if (Environment.TickCount64 >= nextWalk && walks < 6)
            {
                var before = other.User.Location;
                await bot.Send(new C.Walk { Direction = direction }); await BotUntil(() => other.User.Location != before);
                walks++; nextWalk = Environment.TickCount64 + 620;
            }
            if (Environment.TickCount64 - started > 6000) throw new Exception("移动目标追击仍保持固定间距：" + string.Join(',', distances));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (!distances.Contains(2) || distances.Any(distance => distance == 0)) throw new Exception("追上目标时没有实际两格跑步或被原服拒绝。");
        GD.Print($"PASS native moving pursuit: target walking, gap closed to {Functions.MaxDistance(State.User!.Location, other.User.Location)}, {Environment.TickCount64-started}ms, own steps={string.Join(',',distances)}, target walks={walks}.");
        Client.SetDestination(State.User!.Location, false);
        await bot.Send(new C.LogOut()); await BotUntil(() => received.OfType<S.LogOutSuccess>().Any());
    }
}
