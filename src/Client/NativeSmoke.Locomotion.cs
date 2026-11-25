using System.Reflection;
using Client.MirObjects;
using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyLocomotion()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-actions.", StringComparison.Ordinal))
            throw new InvalidOperationException("走跑检查必须使用隔离版本。");
        var preferences = (ClientPreferences)typeof(Main).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!;
        preferences.SmartBattle = false;
        await Until(() => Client.HandshakeReady);
        Client.Login("NativeWizard", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var hud = (TextureRect)Client.FindChild("HudBackground", true, false);
        if (!hud.IsVisibleInTree() || hud.Texture?.GetSize() != new Vector2(1024, 152))
            throw new Exception("进入真实地图后 HUD 底图没有绘制。");
        var potion = State.User!.Inventory.First(item => item?.Info.Type == ItemType.Potion);
        if (Environment.GetEnvironmentVariable("MIR2_EXPECT_MOVE_MS") is { } move &&
            (State.PlayerTiming.MoveMilliseconds != int.Parse(move) || State.PlayerTiming.ActionSpeedPercent != 90 ||
                State.Me?.PlayerTiming != State.PlayerTiming))
            throw new Exception($"没有使用服务端速度配置：{State.PlayerTiming}。");
        Client.UseItem(potion.UniqueID); await Packet<S.UseItem>();
        var start = State.User.Location;
        await VerifyRestrictedExit(start);
        var route = Enum.GetValues<MirDirection>().Select(direction => Client.World.PathTo(Functions.PointMove(start, direction, 24)))
            .First(candidate => candidate.Length == 25 && candidate.Zip(candidate.Skip(1), Main.Direction).Distinct().Count() == 1);
        var direction = Main.Direction(route[0], route[^1]);
        await VerifyMovementWhileArtPending(route[1]);
        await Until(() => Client.World.PlayerAnimationReady(direction, MirAction.Walking) && Client.World.PlayerAnimationReady(direction, MirAction.Running));
        var walk = await MeasureLocomotion(route[^1], false);
        await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
        var runRoute = Enum.GetValues<MirDirection>().Select(runDirection => Client.World.PathTo(Functions.PointMove(route[^1], runDirection, 24)))
            .Where(candidate => candidate.Length == 25 && candidate.Zip(candidate.Skip(1), Main.Direction).Distinct().Count() == 1)
            .OrderByDescending(candidate => State.Actors.Values.Where(actor => actor != State.Me && actor.Kind is ObjectType.Merchant or ObjectType.Monster)
                .SelectMany(actor => candidate.Select(point => Functions.MaxDistance(point, actor.Location))).DefaultIfEmpty(99).Min())
            .First();
        await VerifyOccupiedDestination(runRoute[2]);
        var run = await MeasureLocomotion(runRoute[^1], true);
        if (walk.Gaps + run.Gaps > 0 || walk.Skips + run.Skips > 0)
            throw new Exception($"走跑切站立或跳帧：walk={walk}, run={run}");
        stage = "accepted exit sends original map change";
        var map = State.Map!.MapIndex;
        packets.RemoveAll(packet => packet is S.MapChanged);
        Client.SetDestination(Functions.PointMove(start, MirDirection.Right, 4));
        var transition = await Packet<S.MapChanged>();
        if (transition.MapIndex == map || transition.Location != start)
            throw new Exception("合法入口未返回正确地图和落点。");
        GD.Print("PASS native locomotion: 24-cell walk/run, original TCP acknowledgements, continuous gait without standing gaps/skipped animation frames.");
        await Client.Quit();
    }

    private async Task VerifyRestrictedExit(Point start)
    {
        stage = "run into a group-restricted exit, walk fallback and walk rejection";
        var first = Functions.PointMove(start, MirDirection.UpLeft, 1);
        var second = Functions.PointMove(start, MirDirection.UpLeft, 2);
        var gate = Functions.PointMove(start, MirDirection.UpLeft, 3);
        var map = State.Map!.MapIndex;
        var rejected = 0L;
        var accepted = 0L;
        void Observe(Packet packet)
        {
            if (packet is not S.UserLocation location) return;
            if (location.Location == first && rejected == 0) rejected = Environment.TickCount64;
            if (location.Location == second && accepted == 0) accepted = Environment.TickCount64;
        }
        Client.SetDestination(first);
        await Until(() => State.User!.Location == first);
        Client.PacketReceived += Observe;
        try
        {
            Client.SetDestination(gate, true);
            var deadline = Environment.TickCount64 + 2200;
            await Until(() => accepted != 0 || Environment.TickCount64 >= deadline);
            if (rejected == 0 || accepted <= rejected || accepted - rejected >= 450 || State.Map?.MapIndex != map)
                throw new Exception($"跑步到受限入口未及时回原坐标和退成一步，拒绝={rejected}，接受={accepted}。");
            Client.SetDestination(second);
            packets.Clear();
            Client.SetDestination(gate);
            deadline = Environment.TickCount64 + 2200;
            await Until(() => packets.OfType<S.UserLocation>().Any(p => p.Location == second) || Environment.TickCount64 >= deadline);
            if (State.Map?.MapIndex != map || State.User!.Location != second || !packets.OfType<S.UserLocation>().Any(p => p.Location == second))
                throw new Exception("步行到受限入口没有确认停在原格。");
            GD.Print($"PASS native denied movement: run/walk both reply at source, fallback walk in {accepted - rejected}ms.");
        }
        finally { Client.PacketReceived -= Observe; }
        Client.SetDestination(start);
        await Until(() => State.User!.Location == start);
        await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task VerifyMovementWhileArtPending(Point goal)
    {
        stage = "walking while hair library is still downloading";
        var me = State.Me!;
        var hair = OriginalPlayerArt.Resolve(me.Class, me.Gender, me.Armour, me.Hair, me.Weapon, MirAction.Walking).Hair;
        await Until(() => Client.Assets.Frames(hair) != null);
        var libraries = (Dictionary<string, Task<LibraryFrames>>)typeof(ResourceCache).GetField("libraries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client.Assets)!;
        var ready = libraries[hair];
        var start = State.User!.Location;
        libraries[hair] = new TaskCompletionSource<LibraryFrames>().Task;
        try
        {
            Client.SetDestination(goal);
            var until = Environment.TickCount64 + 1500;
            await Until(() => State.User.Location != start || Environment.TickCount64 >= until);
            if (State.User.Location == start) throw new Exception("发型素材下载阻止了走路发包。");
        }
        finally { libraries[hair] = ready; }
        Client.SetDestination(start);
        await Until(() => State.User.Location == start);
        await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
        GD.Print("PASS native pending art: original TCP movement proceeds while a character layer is unavailable.");
    }

    private async Task VerifyOccupiedDestination(Point goal)
    {
        stage = "temporary occupancy and stationary player after confirmed movement";
        const uint blocker = 0x70003001;
        var before = State.User!.Location;
        State.Actors.Add(blocker, new Actor { Id = blocker, Kind = ObjectType.Player, Location = goal });
        try
        {
            Client.SetDestination(goal, true);
            await ToSignal(GetTree().CreateTimer(.45), SceneTreeTimer.SignalName.Timeout);
            if (State.User.Location != before || Client.World.ActorFrame(State.Me!, Environment.TickCount64, out _)?.Start != FrameSet.Player[MirAction.Standing].Start)
                throw new Exception("目的地被临时占用时角色仍移动或原地跑。");
        }
        finally { State.Actors.Remove(blocker); }
        await Until(() => State.User.Location != before);
        Client.SetDestination(State.User.Location);
        await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
        Client.SetDestination(before, false);
        await Until(() => State.User.Location == before);
        await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task<(int Gaps, int Skips)> MeasureLocomotion(Point goal, bool run)
    {
        stage = run ? "continuous 24-cell running frames" : "continuous 24-cell walking frames";
        var gaps = 0; var skips = 0; var previousFrame = -1; var previousAction = MirAction.Standing;
        var previousTime = Environment.TickCount64; var began = previousTime;
        var previousLocation = State.User!.Location; var previousAck = 0L;
        var steps = new List<(int Distance, long Interval)>();
        var frameTimes = new List<double>(); var cpu = new List<double>();
        void Received(Packet packet)
        {
            if (packet is not S.UserLocation location) return;
            var now = Environment.TickCount64;
            steps.Add((Functions.MaxDistance(previousLocation, location.Location), previousAck == 0 ? 0 : now - previousAck));
            previousLocation = location.Location; previousAck = now;
        }
        Client.PacketReceived += Received;
        try
        {
            Client.SetDestination(goal, run);
            while (State.User.Location != goal)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var now = Environment.TickCount64;
                frameTimes.Add(now - previousTime); cpu.Add(Client.World.LastDrawMilliseconds);
                var me = State.Me!;
                var frame = Client.World.ActorFrame(me, now, out var index);
                if (steps.Count > 0 && me.Action is MirAction.Walking or MirAction.Running && State.User.Location != goal && frame != null)
                {
                    if (frame.Start == FrameSet.Player[MirAction.Standing].Start) gaps++;
                    else
                    {
                        var current = index - frame.Start - (int)me.Direction * frame.OffSet;
                        if (previousAction == me.Action && previousFrame >= 0 && now - previousTime < 65 &&
                            (current - previousFrame + frame.Count) % frame.Count > 1) skips++;
                        previousFrame = current;
                    }
                }
                previousAction = me.Action; previousTime = now;
                if (now - began > 30000)
                    throw new Exception($"走跑检查30秒未完成：{State.User.Location} → {goal}，已确认步长 {string.Join(',', steps.Select(step => step.Distance))}，剩余路线 {Client.MiniMap.Route.Count}。");
            }
        }
        finally { Client.PacketReceived -= Received; }
        frameTimes.Sort(); cpu.Sort();
        GD.Print($"PROFILE locomotion {(run ? "run" : "walk")}: {Environment.TickCount64 - began}ms, steps={string.Join(',', steps.Select(step => $"{step.Distance}:{step.Interval}"))}, gaps={gaps}, skips={skips}, frame-p95={frameTimes[(int)(frameTimes.Count * .95)]:F2}ms, world-p95={cpu[(int)(cpu.Count * .95)]:F2}ms");
        if (steps.Any(step => step.Distance == 0) || (run && steps.Count(step => step.Distance == 2) < 10) || (!run && steps.Any(step => step.Distance != 1)))
            throw new Exception("走跑原TCP步长或拒绝回执异常。");
        var interval = State.PlayerTiming.MoveMilliseconds;
        if (steps.Skip(1).Any(step => step.Interval < interval - 25 || step.Interval > interval + 150))
            throw new Exception($"连续走跑的回执间隔明显偏离服务端配置 {interval}ms。");
        return (gaps, skips);
    }
}
