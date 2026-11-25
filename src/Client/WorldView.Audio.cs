using Client.MirObjects;

namespace Crystal.Native;

public partial class WorldView
{
    internal void QueueSecondSlash(Actor actor) => AddSound(20921, actor.Location, Environment.TickCount64 + actor.PlayerTiming.Scale(276));

    internal void QueueMonsterDeath(Actor actor) => AddSound(OriginalSoundRules.Dead((Monster)actor.Image), actor.Location, actor.ActionTime + ActionDuration(actor));

    internal void QueueMonsterFootsteps(Actor actor)
    {
        AddSound(OriginalSoundRules.MonsterStep((Monster)actor.Image, false), actor.Location, actor.MotionTime + 100);
        AddSound(OriginalSoundRules.MonsterStep((Monster)actor.Image, true), actor.Location, actor.MotionTime + 400);
    }

    internal void QueueMonsterSounds(Actor actor)
    {
        var image = (Monster)actor.Image;
        var frames = Assets.Frames(ActorLibrary(actor))?.Frames;
        var animation = frames?.GetValueOrDefault(OriginalActorFrames.Resolve(frames, actor.Action));
        var interval = animation?.Interval ?? 100;
        var count = Math.Clamp(animation?.Count ?? 8, 1, 120);
        for (var frame = 1; frame < count; frame++)
            AddSound(OriginalSoundRules.AttackFrameSound(image, actor.Action, frame), actor.Location, actor.ActionTime + frame * interval);
        AddSound(OriginalSoundRules.AttackFrameSound(image, actor.Action, -1), actor.Location, actor.ActionTime + count * interval);
        if (image == Monster.DarkOmaKing && actor.Action == MirAction.Attack2)
        {
            AddSound(actor.Image * 10 + 6, actor.Location, actor.ActionTime + interval + 800);
            AddSound(actor.Image * 10 + 6, actor.Location, actor.ActionTime + interval + 1500);
        }
        if (image is Monster.HornedCommander or Monster.HornedSorceror && actor.Action is MirAction.Attack3 or MirAction.Attack4)
        {
            var sound = image == Monster.HornedSorceror ? actor.Image * 10 + 7 : actor.Action == MirAction.Attack3 ? 8451 : 8453;
            for (var loop = 0; loop < Math.Min((int)actor.SpellLevel, 16); loop++)
                AddSound(sound, actor.Location, actor.ActionTime + interval + loop * 5 * interval);
        }
    }
}
