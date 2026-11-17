#nullable enable
namespace Client.MirObjects;

public static class OriginalActorFrames
{
    public static MirAction Resolve(FrameSet frames, MirAction action)
    {
        if (action == MirAction.Pushed) action = MirAction.Walking;
        if (frames.ContainsKey(action)) return action;
        return action switch
        {
            MirAction.AttackRange1 or MirAction.Special => MirAction.Attack1,
            MirAction.AttackRange2 => MirAction.Attack2,
            MirAction.AttackRange3 => MirAction.Attack3,
            MirAction.Skeleton => MirAction.Dead,
            _ => action
        };
    }

    public static Frame? Idle(FrameSet frames, uint id, bool npc, long now, out long elapsed)
    {
        elapsed = now;
        if (!frames.TryGetValue(MirAction.Standing, out var standing)) return null;
        var phase = unchecked(id * 2654435761u);
        if (!npc || !frames.TryGetValue(MirAction.Harvest, out var gesture))
        {
            elapsed = (now + phase) % standing.Duration;
            return standing;
        }
        // 原NPC会在两组待机间切换；按对象分散相位，不让整屏商人一起抬手。
        elapsed = (now + phase) % ((long)standing.Duration + gesture.Duration);
        if (elapsed < standing.Duration) return standing;
        elapsed -= standing.Duration;
        return gesture;
    }
}
