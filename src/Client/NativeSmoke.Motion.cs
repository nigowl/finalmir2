using Godot;
using Client.MirObjects;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifySmoothOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("流畅性检查必须使用隔离原生fixture。");
        stage = "selection animation first full cycle";
        await Until(() => Client.HandshakeReady);
        Client.Login("NativeSmoke", "Native123"); await Packet<S.LoginSuccess>();
        var preview = Client.FindChildren("*", "Control", true, false).OfType<OriginalArt>().Single(art => art.Page == "select");
        var seen = new HashSet<int>(); var blanks = 0; var appeared = false;
        for (var sample = 0; sample < 300; sample++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (preview.PreviewFrame >= 0) { appeared = true; seen.Add(preview.PreviewFrame); }
            else if (appeared) blanks++;
        }
        if (!preview.PreviewReady || blanks != 0 || seen.Count != 16) throw new Exception($"选角动画缺帧或闪空：{seen.Count}/16，blanks={blanks}。");
        await CombatScreenshot("selection-stable");
        GD.Print($"PASS selection: all {seen.Count} original frames, no blank frame after first appearance, full-cycle preload.");
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(character => character.Name == "NativeWarrior").Index });
        await Until(() => State.User != null && !Client.World.Loading && Client.MiniMap.Revision.Length == 64);
        var panel = SettingsPanel(); Setting(panel, "CRT").ButtonPressed = false; CloseSettingsPanel(panel);
        if (OS.GetCmdlineUserArgs().Contains("--verify-smooth")) { await VerifyGoldNotices(); await VerifyCombatStance(); }
        await ProfileMovement();
        await Client.Quit();
    }

    private async Task ProfileMovement()
    {
        stage = "original TCP continuous run and moving frame profile";
        var start = State.User!.Location;
        Point[] route = [];
        foreach (var direction in Enum.GetValues<MirDirection>())
        {
            var candidate = Client.World.PathTo(Functions.PointMove(start, direction, 24));
            if (candidate.Length == 25 && candidate.Zip(candidate.Skip(1), Main.Direction).Distinct().Count() == 1) { route = candidate; break; }
        }
        if (route.Length == 0) throw new Exception("隔离地图没有24格直线路径供跑步测量。");
        var steps = new List<(int Distance, long Interval)>(); var previousLocation = start; var previousPacket = Environment.TickCount64;
        void Received(Packet packet)
        {
            if (packet is not S.UserLocation location) return;
            var now = Environment.TickCount64; var distance = Functions.MaxDistance(previousLocation, location.Location);
            steps.Add((distance, now - previousPacket)); previousPacket = now; previousLocation = location.Location;
        }
        Client.PacketReceived += Received;
        var intervals = new List<double>(); var draws = new List<double>();
        var bytes = GC.GetTotalAllocatedBytes(); var previous = System.Diagnostics.Stopwatch.GetTimestamp();
        var timer = Environment.TickCount64; var pixelErrors = 0; var stanceGaps = 0;
        var scale = GetViewport().GetStretchTransform().Scale;
        var playerArt = OriginalPlayerArt.Resolve(State.Me!.Class, State.Me.Gender, State.Me.Armour, State.Me.Hair, State.Me.Weapon, MirAction.Standing);
        var standing = Client.Assets.Frames(playerArt.Body)!.Frames[MirAction.Standing];
        try
        {
            Client.SetDestination(route[^1], true);
            while (State.User.Location != route[^1])
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                intervals.Add(System.Diagnostics.Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now;
                draws.Add(Client.World.LastDrawMilliseconds);
                var pixel = Client.World.Screen(start) * scale;
                if (pixel.DistanceTo(pixel.Round()) > .02f) pixelErrors++;
                var me = State.Me!; var elapsed = Environment.TickCount64 - me.MotionTime;
                if (me.Action is MirAction.Walking or MirAction.Running && elapsed > 605 && elapsed < me.MovementDuration(600) - 2 &&
                    Client.World.ActorFrame(me, Environment.TickCount64, out _) == standing) stanceGaps++;
                if (Environment.TickCount64 - timer > 30000) throw new Exception("24格跑步30秒内没有完成。");
            }
        }
        finally { Client.PacketReceived -= Received; }
        intervals.Sort(); draws.Sort();
        GD.Print($"PROFILE movement: 24cells={Environment.TickCount64-timer}ms, steps={string.Join(',', steps.Select(step => $"{step.Distance}:{step.Interval}"))}, frame p50/p95/p99={intervals[intervals.Count/2]:F2}/{intervals[(int)(intervals.Count*.95)]:F2}/{intervals[(int)(intervals.Count*.99)]:F2}ms, world CPU p95={draws[(int)(draws.Count*.95)]:F2}ms, allocation={(GC.GetTotalAllocatedBytes()-bytes)/1048576d:F2}MiB/{intervals.Count}frames.");
        if (steps.Count(step => step.Distance == 2) < 10 || steps.Any(step => step.Distance == 0) || pixelErrors != 0 || stanceGaps != 0)
            throw new Exception($"跑步回执/物理像素/接续动画异常：pixelErrors={pixelErrors}, stanceGaps={stanceGaps}。");
        GD.Print($"PASS movement: sustained two-cell runs, scale={scale}, physical pixel alignment and no idle-frame gaps.");
        Client.SetDestination(start, true); await Until(() => State.User.Location == start);
        if (OS.GetCmdlineUserArgs().Contains("--verify-smooth")) await VerifyRunContinuation(route);
    }

    private async Task VerifyRunContinuation(Point[] route)
    {
        stage = "run continuation after an isolated render scheduling delay";
        var previous = State.User!.Location; var steps = new List<int>(); var ack = 0L;
        void Received(Packet packet)
        {
            if (packet is not S.UserLocation location) return;
            steps.Add(Functions.MaxDistance(previous, location.Location)); previous = location.Location; ack = Environment.TickCount64;
        }
        Client.PacketReceived += Received;
        try
        {
            Client.SetDestination(route[5], true);
            await Until(() => steps.Count > 0);
            await Until(() => Environment.TickCount64 - ack >= 520);
            // 仅隔离测试人为延迟一帧，复现旧700ms起跑窗口；不计入上方性能采样。
            OS.DelayMsec(250);
            await Until(() => steps.Count >= 2);
            if (steps[1] != 2) throw new Exception($"短暂帧延迟后退回了走路：{string.Join(',', steps)}。");
            await Until(() => State.User.Location == route[5]);
        }
        finally { Client.PacketReceived -= Received; }
        Client.SetDestination(route[0], true); await Until(() => State.User.Location == route[0]);
        GD.Print("PASS run continuation: one injected 250ms render delay, next original server acknowledgement still advances two cells without rejection.");
    }
}
