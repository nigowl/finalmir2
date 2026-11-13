using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyPatrol()
    {
        stage = "original guard patrol, remote step cadence and continuous animation";
        await Until(() => State.Actors.Values.Any(actor => actor.Name == "NativePatrol"));
        // PNG截图会阻塞主线程；先排空期间积压的包，再开始测量完整收包间隔。
        await Until(() => Client.Connection.Received.IsEmpty);
        var guard = State.Actors.Values.Single(actor => actor.Name == "NativePatrol");
        var intervals = new List<long>(); long previousPacket = 0;
        void Observe(Packet packet)
        {
            if (packet is not S.ObjectWalk walk || walk.ObjectID != guard.Id) return;
            var now = Environment.TickCount64;
            if (previousPacket > 0) intervals.Add(now - previousPacket);
            previousPacket = now;
        }
        Client.PacketReceived += Observe;
        var previous = Client.World.Visual(guard); var previousTime = Environment.TickCount64; var movingSamples = 0;
        try
        {
            await Until(() =>
            {
                var now = Environment.TickCount64; var point = Client.World.Visual(guard);
                var duration = guard.MovementDuration(600); var elapsed = now - guard.MotionTime;
                if (intervals.Count > 0 && now - previousTime < 100 && point.DistanceTo(previous) > .25f)
                    throw new Exception($"A patrol packet snapped the guard: {previous} -> {point}.");
                if (intervals.Count > 0 && elapsed > 650 && elapsed < duration - 50)
                {
                    if (point == new Vector2(guard.Location.X, guard.Location.Y)) throw new Exception("Patrol stopped at the endpoint before its next server step.");
                    movingSamples++;
                }
                previous = point; previousTime = now;
                return intervals.Count >= 7;
            });
            if (movingSamples < 20 || intervals.Any(interval => interval < 900 || interval > 1400))
                throw new Exception($"Patrol did not follow its isolated 1000ms route: samples={movingSamples}, intervals={string.Join(',', intervals)}.");
            await Screenshot("patrol");
            GD.Print($"PASS native patrol: original guard art, {intervals.Count} steps, intervals={string.Join(',', intervals)}ms, {movingSamples} continuous late-step samples and no position reset.");
        }
        finally { Client.PacketReceived -= Observe; }
    }
}
