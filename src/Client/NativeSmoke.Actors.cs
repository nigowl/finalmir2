using Client.MirObjects;
using Godot;
using Point = System.Drawing.Point;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyActorsOnly()
    {
        stage = "focused actor animation/render measurement";
        await EnterUiFixture();
        if (OS.GetCmdlineUserArgs().Contains("--verify-actors")) await VerifyGuardAnimations();
        await ProfileActors();
        await Client.Quit();
    }

    private async Task VerifyGuardAnimations()
    {
        const uint id = 0x70001000;
        var drawn = new HashSet<(string Library, int Frame)>();
        void Drawn(string library, int frame) => drawn.Add((library, frame));
        Client.World.EffectFrameDrawn += Drawn;
        try
        {
            foreach (var image in new[] { Monster.ArcherGuard, Monster.ArcherGuard2, Monster.ArcherGuard3 })
            {
                stage = $"guard {image} complete original attack and arrow";
                var guard = new Actor { Id = id, Kind = ObjectType.Monster, Image = (int)image, Direction = MirDirection.Down,
                    Name = "弓箭守卫动作检查", Location = new Point(State.User!.Location.X - 2, State.User.Location.Y - 2) };
                State.Actors[id] = guard;
                await Until(() => WarmActor(guard));
                var frames = Client.Assets.Frames($"Monster/{(int)image:000}")!.Frames;
                var attack = frames[OriginalActorFrames.Resolve(frames, MirAction.AttackRange1)];
                var packet = new S.ObjectRangeAttack { ObjectID = id, Location = guard.Location, Direction = guard.Direction,
                    TargetID = State.User.ObjectID, Target = State.User.Location };
                drawn.Clear(); State.Apply(packet); Client.World.SpellPacket(packet);
                var seen = new HashSet<int>();
                var started = guard.ActionTime;
                while (Environment.TickCount64 - started < attack.Duration)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    if (Client.World.ActorFrame(guard, Environment.TickCount64, out var index) == attack) seen.Add(index);
                }
                await Until(() => drawn.Any(pair => pair.Library == (image == Monster.ArcherGuard3 ? "Monster/378" : "Monster/139")));
                if (seen.Count != attack.Count || Client.World.ActionDuration(guard) != attack.Duration)
                    throw new Exception($"{image}原攻击动作缺帧：{seen.Count}/{attack.Count}。");
                GD.Print($"PASS native {image}: {seen.Count} attack frames, {attack.Duration}ms and original directional arrow drawn.");
                await Screenshot("guard-" + image);
            }
        }
        finally { State.Actors.Remove(id); Client.World.EffectFrameDrawn -= Drawn; }
    }

    private async Task ProfileActors()
    {
        var added = new List<uint>();
        try
        {
            // 仅注入本地渲染对象，不向服务端生成怪物、修改世界或发动战斗。
            for (var i = 0; i < 80; i++)
            {
                var id = 0x70000000u + (uint)i; var merchant = i % 2 == 0;
                State.Actors.Add(id, new Actor { Id = id, Kind = merchant ? ObjectType.Merchant : ObjectType.Monster,
                    Image = merchant ? i % 3 : (int)Monster.ArcherGuard, Name = merchant ? "渲染NPC" : "弓箭守卫",
                    Direction = (MirDirection)(i % 3), Location = new Point(State.User!.Location.X + i % 12 - 6, State.User.Location.Y + i / 12 - 3) });
                added.Add(id);
            }
            await Until(() => added.All(id => WarmActor(State.Actors[id])) && Client.Assets.PendingDownloads == 0 && Client.World.PrefetchQueueCount == 0);
            var intervals = new List<double>(); var draws = new List<double>();
            var disk = Client.Assets.CachedFiles; var downloads = Client.Assets.Downloads;
            var bytes = GC.GetTotalAllocatedBytes(); var previous = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var sample = 0; sample < 240; sample++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                intervals.Add(System.Diagnostics.Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); draws.Add(Client.World.LastDrawMilliseconds); previous = now;
            }
            intervals.Sort(); draws.Sort();
            GD.Print($"PROFILE actors={State.Actors.Count}, fps={1000 / intervals.Average():F1}, frame p50/p95/p99={intervals[120]:F2}/{intervals[228]:F2}/{intervals[237]:F2}ms, world CPU p50/p95={draws[120]:F2}/{draws[228]:F2}ms, allocation={(GC.GetTotalAllocatedBytes()-bytes)/1048576d:F2}MiB/240frames, disk={Client.Assets.CachedFiles-disk}, downloads={Client.Assets.Downloads-downloads}.");
            await Screenshot("actors-profile");
        }
        finally { foreach (var id in added) State.Actors.Remove(id); }
    }

    private bool WarmActor(Actor actor)
    {
        var library = actor.Kind == ObjectType.Merchant ? $"NPC/{actor.Image:00}" : $"Monster/{actor.Image:000}";
        var frames = Client.Assets.Frames(library);
        if (frames == null) return false;
        var ready = true;
        foreach (var action in new[] { MirAction.Standing, MirAction.Harvest, MirAction.Attack1, MirAction.AttackRange1 })
            if (frames.Frames.TryGetValue(action, out var frame)) ready &= Client.Assets.PrepareAnimation(library, frame, actor.Direction);
        return ready;
    }
}
