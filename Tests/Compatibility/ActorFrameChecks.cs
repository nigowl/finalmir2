using Client.MirObjects;

internal static class ActorFrameChecks
{
    public static void Run(Action<bool, string> check)
    {
        var frames = new FrameSet { [MirAction.Standing] = new(0, 4, 0, 500), [MirAction.Attack1] = new(104, 6, 3, 100),
            [MirAction.Walking] = new(32, 6, 0, 100), [MirAction.Dead] = new(153, 1, 0, 1000) };
        check(OriginalActorFrames.Resolve(frames, MirAction.AttackRange1) == MirAction.Attack1, "弓箭守卫缺远程帧时回退待机而非攻击");
        check(OriginalActorFrames.Resolve(frames, MirAction.Pushed) == MirAction.Walking && OriginalActorFrames.Resolve(frames, MirAction.Skeleton) == MirAction.Dead, "原怪物动作别名丢失");
        frames[MirAction.AttackRange1] = new(200, 10, 3, 100);
        check(OriginalActorFrames.Resolve(frames, MirAction.AttackRange1) == MirAction.AttackRange1, "有独立远程动作时被通用攻击覆盖");
        var phases = new HashSet<int>();
        for (uint id = 1; id <= 40; id++)
        {
            var frame = OriginalActorFrames.Idle(frames, id, false, 10000, out var elapsed)!;
            phases.Add(frame.IndexAt(elapsed));
            check(frame.Count == 4 && frame.Interval == 500, "为了提高帧率改变了原待机帧数或速度");
        }
        check(phases.Count == 4, "怪物待机仍全部同步");
        frames[MirAction.Harvest] = new(12, 10, 0, 200);
        var gestures = new HashSet<int>();
        for (var now = 0; now < 4000; now += 100)
        {
            var frame = OriginalActorFrames.Idle(frames, 10, true, now, out var elapsed)!;
            gestures.Add(frame.Start + frame.IndexAt(elapsed));
        }
        check(gestures.Count == 14 && gestures.Contains(0) && gestures.Contains(21), "NPC没有完整使用原两组待机动作");
        foreach (var image in new[] { Monster.ArcherGuard, Monster.ArcherGuard2, Monster.ArcherGuard3 })
        {
            var missile = OriginalSpellEffects.GuardMissile(image)!.Value;
            check(missile.Flight.Count == 3 && !missile.Flight.Blend && missile.Interval == 30 && missile.DirectionStride == (image == Monster.ArcherGuard3 ? 3 : 9), "守卫箭矢未沿用原方向步长、帧数和混合模式");
        }
        check(OriginalSpellEffects.GuardMissile(Monster.Hen) == null, "普通怪物错误绘制弓箭");
    }
}
