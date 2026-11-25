using System.Drawing;

namespace Crystal.Native;

public static class ClientMovement
{
    public static MirDirection? KeyboardDirection(bool up, bool down, bool left, bool right)
    {
        var x = (right ? 1 : 0) - (left ? 1 : 0);
        var y = (down ? 1 : 0) - (up ? 1 : 0);
        return x == 0 && y == 0 ? null : Functions.DirectionFromPoint(Point.Empty, new Point(x, y));
    }

    public static bool CanRun(GameState state, bool requested, long lastMoved, long now)
    {
        if (!requested || state.InTrapRock || state.User == null || state.Me is not { Dead: false } me ||
            (me.Poison & (PoisonType.Paralysis | PoisonType.LRParalysis | PoisonType.Frozen)) != 0 ||
            state.InventoryWeight > state.CalculateStats()[Stat.BagWeight] || Sneaking(state) && !Active(state, BuffType.SwiftFeet)) return false;
        if (Active(state, BuffType.Transform) && state.UserBuffs[BuffType.Transform].Values is { Length: > 0 }) return true;
        foreach (var item in state.User.Equipment)
        {
            if (item?.Info == null || item.CurrentDura == 0 && item.Info.Durability > 0 || item.Info.IsFishingRod) continue;
            var info = item.Info.ClassBased || item.Info.LevelBased
                ? Functions.GetRealItem(item.Info, state.User.Level, state.User.Class, state.Items.Values.ToList()) : item.Info;
            if (info.CanFastRun) return true;
        }
        // 普通角色仍需原服确认的一步起跑；不能省掉起步而让服务端反复拒绝。
        return lastMoved > 0 && now >= lastMoved && now - lastMoved < me.RunContinuationWindow;
    }

    public static int RunDistance(GameState state) => state.Me?.RidingMount == true || Active(state, BuffType.SwiftFeet) && !Sneaking(state) ? 3 : 2;

    public static int StepIndex(IReadOnlyList<Point> route, int distance)
    {
        if (route.Count <= 1) return 0;
        if (distance < 2 || route.Count <= distance) return 1;
        var direction = Functions.DirectionFromPoint(route[0], route[1]);
        for (var step = 1; step <= distance; step++)
            if (route[step] != Functions.PointMove(route[0], direction, step)) return 1;
        return distance;
    }

    private static bool Active(GameState state, BuffType type) => state.UserBuffs.TryGetValue(type, out var buff) && !buff.Paused;
    private static bool Sneaking(GameState state) => Active(state, BuffType.MoonLight) || Active(state, BuffType.DarkBody);
}
