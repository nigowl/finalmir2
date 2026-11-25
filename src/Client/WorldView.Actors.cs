using Client.MirObjects;
using Godot;
using Point = System.Drawing.Point;
using GColor = Godot.Color;

namespace Crystal.Native;

public partial class WorldView
{
    private Actor? drawnPlayer;
    private SpriteImage? drawnPlayerBody;
    internal Vector2 Visual(Actor actor, long? timestamp = null)
    {
        var duration = actor.Kind is ObjectType.Monster or ObjectType.Merchant
            ? Assets.Frames(ActorLibrary(actor))?.Frames.GetValueOrDefault(actor.Action)?.Duration ?? Actor.MoveDuration : Actor.MoveDuration;
        var point = actor.PositionAt(timestamp ?? Environment.TickCount64, duration);
        return new Vector2(point.X, point.Y);
    }

    private static PlayerArt PlayerArt(Actor actor, MirAction action) => OriginalPlayerArt.Resolve(actor.Class, actor.Gender, actor.Armour, actor.Hair, actor.Weapon, action);
    private static string ActorLibrary(Actor actor, MirAction? action = null) => actor.Kind == ObjectType.Player ? PlayerArt(actor, action ?? actor.Action).Body : actor.Kind == ObjectType.Merchant
        ? actor.Image >= 1000 ? $"Flag/{actor.Image - 1000:00}" : $"NPC/{actor.Image:00}"
        : actor.IsShinsu && action == MirAction.Show ? "Monster/079"
        : actor.Image >= 10000 ? $"Pet/{actor.Image - 10000:00}" : actor.Image is >= 950 and < 960 ? $"Gate/{actor.Image - 950:00}"
        : actor.Image is >= 940 and < 950 ? $"Siege/{actor.Image - 940:00}" : $"Monster/{actor.Image:000}";

    internal int ActionDuration(Actor actor)
    {
        if (!actor.Casting && actor.ActionSpell == Spell.DoubleSlash && actor.Action == MirAction.Attack1) return actor.PlayerTiming.Scale(552);
        var frames = Assets.Frames(ActorLibrary(actor))?.Frames;
        var duration = frames?.GetValueOrDefault(OriginalActorFrames.Resolve(frames, actor.Action))?.Duration ?? Actor.MoveDuration;
        if (actor.Action is MirAction.Walking or MirAction.Running or MirAction.Pushed) return actor.MovementDuration(duration);
        return actor.Kind == ObjectType.Player ? actor.PlayerTiming.Scale(duration) : duration;
    }

    internal Vector2 NameAnchor(Actor actor, long now)
    {
        var position = Screen(Visual(actor, now));
        if (actor.Kind is ObjectType.Player or ObjectType.Merchant or ObjectType.Monster)
        {
            return position + (NameBody(actor) is { } body ? body.Offset + body.BodyCentre : new Vector2(24, -24));
        }
        return position + new Vector2(24, actor.Kind == ObjectType.Item ? -4 : -46);
    }

    internal SpriteImage? NameBody(Actor actor)
    {
        // 用固定朝向的站立帧取身体中心，当前动作或朝向只影响造型。
        var library = ActorLibrary(actor, MirAction.Standing);
        if (Assets.Frames(library)?.Frames.GetValueOrDefault(MirAction.Standing) is not { } frame) return null;
        var direction = actor.Kind == ObjectType.Merchant ? 0 : (int)MirDirection.Down;
        var offset = actor.Kind == ObjectType.Player ? PlayerArt(actor, MirAction.Standing).BodyOffset : 0;
        return Assets.Sprite(library, frame.Start + direction * frame.OffSet + offset);
    }

    public bool PlayerAnimationReady(MirDirection direction, MirAction action)
    {
        var actor = State.Me;
        if (actor == null || Assets.Frames(ActorLibrary(actor, action))?.Frames.TryGetValue(OriginalPlayerArt.Animation(actor.Class, actor.Weapon, action), out var frame) != true) return false;
        var art = PlayerArt(actor, action);
        var ready = Assets.PrepareAnimation(art.Body, frame, direction, art.BodyOffset);
        ready &= Assets.PrepareAnimation(art.Hair, frame, direction, art.BodyOffset);
        if (art.Weapon.Length > 0) ready &= Assets.PrepareAnimation(art.Weapon, frame, direction, art.WeaponOffset);
        if (art.SecondWeapon.Length > 0) ready &= Assets.PrepareAnimation(art.SecondWeapon, frame, direction, art.WeaponOffset);
        return ready;
    }

    internal bool MeleeReady(Spell spell, byte level, MirDirection direction)
    {
        var ready = PlayerAnimationReady(direction, MirAction.Attack1);
        ready &= PlayerAnimationReady(direction, MirAction.Stance);
        var clip = OriginalSpellEffects.Melee(spell, level, direction) ??
            (spell == Spell.None && CombatPresentation.HasBasicSword(State, State.Me) ? OriginalSpellEffects.BasicSword(direction) : null);
        if (clip is { } melee) ready &= PrepareClip(melee);
        if (spell == Spell.DoubleSlash)
        {
            ready &= PlayerAnimationReady(direction, MirAction.Attack4);
            if (OriginalSpellEffects.Melee(spell, level, direction, true) is { } second) ready &= PrepareClip(second);
        }
        return ready;
    }

    internal Client.MirObjects.Frame? ActorFrame(Actor actor, long now, out int index)
    {
        index = 0;
        var renderAction = RenderAction(actor, now);
        if (actor.IsShinsu && renderAction == MirAction.Dead) return null;
        var frames = Assets.Frames(ActorLibrary(actor, renderAction));
        if (frames == null) return null;
        var action = actor.IsShinsu ? renderAction : actor.Dead ? MirAction.Die : actor.Action;
        if (actor.Kind == ObjectType.Player) action = OriginalPlayerArt.Animation(actor.Class, actor.Weapon, action);
        else action = OriginalActorFrames.Resolve(frames.Frames, action);
        var elapsed = Math.Max(0, now - actor.ActionTime);
        if (actor.Kind == ObjectType.Player && action is not (MirAction.Walking or MirAction.Running or MirAction.WalkingBow or MirAction.RunningBow)) elapsed = elapsed * actor.PlayerTiming.ActionSpeedPercent / 100;
        if (!actor.Casting && actor.ActionSpell == Spell.DoubleSlash && action == MirAction.Attack1)
        {
            if (elapsed >= 552) action = MirAction.Standing;
            else { action = elapsed >= 276 ? MirAction.Attack4 : MirAction.Attack1; elapsed = elapsed % 276 * 100 / 46; }
        }
        if (frames.Frames.TryGetValue(action, out var active))
        {
            var stepping = action is MirAction.Walking or MirAction.Running or MirAction.WalkingBow or MirAction.RunningBow;
            var duration = stepping ? actor.MovementDuration(actor.Kind == ObjectType.Player ? Actor.MoveDuration : active.Duration) : active.Duration;
            if (elapsed >= duration)
                action = actor.Dead ? MirAction.Dead : IdleAction(actor, now);
            else if (stepping)
                elapsed = actor.Kind == ObjectType.Player ? actor.MovementAnimationElapsed(now, active.Duration) % active.Duration : elapsed * active.Duration / duration;
        }
        if (!frames.Frames.TryGetValue(action, out var frame)) { action = MirAction.Standing; frames.Frames.TryGetValue(action, out frame); }
        if (action == MirAction.Standing)
            frame = actor.Kind == ObjectType.Player ? frame : OriginalActorFrames.Idle(frames.Frames, actor.Id, actor.Kind == ObjectType.Merchant, now, out elapsed);
        else if (action == MirAction.Stance) elapsed = 0;
        if (frame == null) return null;
        index = frame.Start + (int)actor.Direction * frame.OffSet + frame.IndexAt(action == MirAction.Standing && actor.Kind == ObjectType.Player ? now * actor.PlayerTiming.ActionSpeedPercent / 100 : elapsed, action == MirAction.Standing);
        return frame;
    }

    private MirAction RenderAction(Actor actor, long now)
    {
        var action = actor.Dead ? MirAction.Die : actor.Action;
        if (actor.IsShinsu)
        {
            if (actor.Dead && actor.Action != MirAction.Die) return MirAction.Dead;
            if (action is MirAction.Die or MirAction.Show or MirAction.Hide or MirAction.Appear)
            {
                var clipDuration = Assets.Frames(ActorLibrary(actor, action))?.Frames.GetValueOrDefault(action)?.Duration ?? 1000;
                if (now - actor.ActionTime >= clipDuration) return actor.Dead ? MirAction.Dead : MirAction.Standing;
            }
            return action;
        }
        if (actor.Kind != ObjectType.Player) return action;
        var duration = !actor.Casting && actor.ActionSpell == Spell.DoubleSlash && action == MirAction.Attack1 ? 552 : FrameSet.Player.GetValueOrDefault(action)?.Duration ?? 600;
        if (action is MirAction.Walking or MirAction.Running or MirAction.Pushed) duration = actor.MovementDuration(Actor.MoveDuration);
        else duration = actor.PlayerTiming.Scale(duration);
        return now - actor.ActionTime < duration ? action : actor.Dead ? MirAction.Dead : IdleAction(actor, now);
    }

    private static MirAction IdleAction(Actor actor, long now) => actor.Kind == ObjectType.Player && !actor.Dead && !OriginalPlayerArt.HasBow(actor.Class, actor.Weapon) &&
        actor.Action is MirAction.Attack1 or MirAction.Attack2 or MirAction.Attack3 or MirAction.Attack4 or MirAction.AttackRange1 or MirAction.Spell && now - actor.ActionTime < 2500 ? MirAction.Stance : MirAction.Standing;

    internal void WarmMovement(IReadOnlyList<Point> route, bool run)
    {
        if (State.User == null) return;
        var index = 0;
        while (index < route.Count && route[index] != State.User.Location) index++;
        MirDirection? previous = null;
        for (var next = index + 1; next < Math.Min(route.Count, index + 6); next++)
        {
            var direction = Functions.DirectionFromPoint(route[next - 1], route[next]);
            if (direction == previous) continue;
            PlayerAnimationReady(direction, MirAction.Walking);
            if (run) PlayerAnimationReady(direction, MirAction.Running);
            previous = direction;
        }
    }

    internal SpriteImage? ActorBody(Actor actor, long now)
    {
        if (actor.IsShinsu) PrepareShinsu(actor, now);
        var frame = ActorFrame(actor, now, out var index);
        if (frame == null) return null;
        var library = ActorLibrary(actor, RenderAction(actor, now));
        if (actor.Kind != ObjectType.Merchant)
            return Assets.Sprite(library, index + (actor.Kind == ObjectType.Player ? PlayerArt(actor, RenderAction(actor, now)).BodyOffset : 0));
        var frames = Assets.Frames(library)!.Frames;
        frames.TryGetValue(MirAction.Standing, out var standing);
        frames.TryGetValue(MirAction.Harvest, out var gesture);
        // 商人会随机转向；预备三个方向的完整两组待机，切动作时不能临时缺图。
        for (var direction = 0; direction < 3; direction++)
        {
            if (standing != null) Assets.PrepareAnimation(library, standing, (MirDirection)direction);
            if (gesture != null) Assets.PrepareAnimation(library, gesture, (MirDirection)direction);
        }
        var image = Assets.Sprite(library, index);
        if (image != null) return image;
        // 个别素材有空帧，或新方向仍在下载；沿用同组最近可用造型，不空画一帧。
        for (var pose = 0; pose < 2; pose++)
        {
            var clip = pose == 0 ? frame : standing;
            if (clip == null || clip.Count <= 0) continue;
            var at = Math.Clamp(index - frame.Start - (int)actor.Direction * frame.OffSet, 0, clip.Count - 1);
            for (var turn = 0; turn < 3; turn++)
            for (var back = 0; back < clip.Count; back++)
                if (Assets.CachedSprite(library, clip.Start + ((int)actor.Direction + turn) % 3 * clip.OffSet + (at - back + clip.Count) % clip.Count) is { } cached) return cached;
        }
        return null;
    }

    private int warmedShinsuDirections;
    private readonly long[] shinsuWarmRetry = new long[8];

    private void PrepareShinsu(Actor actor, long now)
    {
        var direction = (int)actor.Direction;
        if ((warmedShinsuDirections & (1 << direction)) != 0 || now < shinsuWarmRetry[direction]) return;
        shinsuWarmRetry[direction] = now + 100;
        var ready = true;
        // 先准备当前朝向的两种造型，短暂变身或死亡时不会临时向服务端逐帧取图。
        foreach (var (library, actions) in ShinsuFrames)
        {
            if (Assets.Frames(library)?.Frames is not { } frames) { ready = false; continue; }
            foreach (var action in actions)
                if (frames.TryGetValue(action, out var frame)) ready &= Assets.PrepareAnimation(library, frame, actor.Direction);
        }
        ready &= PrepareClip(OriginalSpellEffects.ShinsuBreath(actor.Direction, 100));
        if (ready) warmedShinsuDirections |= 1 << direction;
    }

    private static readonly (string Library, MirAction[] Actions)[] ShinsuFrames =
    [
        ("Monster/079", [MirAction.Standing, MirAction.Appear, MirAction.Show, MirAction.Hide, MirAction.Die]),
        ("Monster/080", [MirAction.Standing, MirAction.Attack1, MirAction.Die]),
    ];

    private void DrawActor(Actor actor, long now)
    {
        if (actor.Hidden && actor.Id != State.User?.ObjectID) return;
        var position = Screen(Visual(actor, now));
        if (position.X < -250 || position.X > 1300 || position.Y < -250 || position.Y > 900) return;
        if (actor.Kind == ObjectType.Item)
        {
            var item = Assets.Sprite("DNItems", actor.Image);
            if (item != null) DrawTexture(item.Texture, position + (new Vector2(48, 32) - item.Size) / 2);
            return;
        }
        if (actor.Kind == ObjectType.Spell)
        {
            if (OriginalSpellEffects.Ground(actor.ActionSpell, actor.Direction, actor.SpellParam) is { } ground)
                DrawClip(ground.Clip, ground.Clip.FrameAt(now - actor.ActionTime, ground.Repeat), position);
            return;
        }
        if (actor.IsShinsu) PrepareShinsu(actor, now);
        var library = ActorLibrary(actor, RenderAction(actor, now));
        var frame = ActorFrame(actor, now, out var index);
        if (frame == null) return;
        var originalTint = OriginalSpellEffects.PoisonColour(actor.Poison);
        var tint = GColor.Color8(originalTint.R, originalTint.G, originalTint.B);
        if (actor.Kind == ObjectType.Player)
        {
            var art = PlayerArt(actor, RenderAction(actor, now));
            Assets.PrepareAnimation(library, frame, actor.Direction, art.BodyOffset);
            var opacity = actor.Hidden ? .5f : 1f;
            var body = Assets.Sprite(library, index + art.BodyOffset);
            if (actor == State.Me)
            {
                if (actor != drawnPlayer) { drawnPlayer = actor; drawnPlayerBody = null; }
                if (body != null) drawnPlayerBody = body;
                else if ((drawnPlayerBody ?? NameBody(actor)) is { } previous)
                {
                    DrawTexture(previous.Texture, position + previous.Offset, new GColor(tint, opacity));
                    DrawPlayerEffects(actor, now, position);
                    return;
                }
            }
            var weaponBehind = actor.Direction is MirDirection.Left or MirDirection.Up or MirDirection.UpLeft or MirDirection.DownLeft;
            if (weaponBehind && actor.Weapon >= 0) DrawSprite(art.Weapon, index + art.WeaponOffset, position, opacity, tint);
            if (!weaponBehind && art.SecondWeapon.Length > 0) DrawSprite(art.SecondWeapon, index + art.WeaponOffset, position, opacity, tint);
            if (body != null) DrawTexture(body.Texture, position + body.Offset, new GColor(tint, opacity));
            DrawSprite(art.Hair, index + art.BodyOffset, position, opacity, tint);
            if (!weaponBehind && actor.Weapon >= 0) DrawSprite(art.Weapon, index + art.WeaponOffset, position, opacity, tint);
            if (weaponBehind && art.SecondWeapon.Length > 0) DrawSprite(art.SecondWeapon, index + art.WeaponOffset, position, opacity, tint);
            DrawPlayerEffects(actor, now, position);
        }
        else if (actor.Kind == ObjectType.Merchant)
        { if (ActorBody(actor, now) is { } body) DrawTexture(body.Texture, position + body.Offset, tint); }
        else { Assets.PrepareAnimation(library, frame, actor.Direction); DrawSprite(library, index, position, 1, tint); }
        if (actor.Kind == ObjectType.Merchant && State.QuestsAt(actor.Id).FirstOrDefault() is { } quest)
        {
            var progress = State.Quests.GetValueOrDefault(quest.Index);
            var marker = Assets.Sprite("Prguse", 981 + (int)quest.GetQuestIcon(progress != null, progress?.Completed == true) * 2 + (int)(now / 350 % 2));
            var body = Assets.Sprite(library, frame.Start);
            if (marker != null && body != null) DrawTexture(marker.Texture, position + body.Offset + new Vector2(body.Size.X / 2 - 28, -40));
        }
    }

    private void DrawPlayerEffects(Actor actor, long now, Vector2 position)
    {
        var elapsed = (now - actor.ActionTime) * actor.PlayerTiming.ActionSpeedPercent / 100;
        if (!actor.Dead && !actor.Casting && actor.Action is MirAction.Attack1 or MirAction.Attack4)
        {
            var attack = OriginalSpellEffects.Melee(actor.ActionSpell, actor.SpellLevel, actor.Direction, actor.Action == MirAction.Attack4 || elapsed >= 276 && actor.ActionSpell == Spell.DoubleSlash) ??
                (actor.ActionSpell == Spell.None && CombatPresentation.HasBasicSword(State, actor) ? OriginalSpellEffects.BasicSword(actor.Direction) : null);
            if (attack is { } melee && (actor.ActionSpell != Spell.DoubleSlash || elapsed < 552))
                DrawClip(melee, melee.FrameAt(actor.ActionSpell == Spell.DoubleSlash ? elapsed % 276 : elapsed), position);
        }
        if (actor.Dead) return;
        if (actor.MagicShield) DrawClip(new("Magic", 3890, 3, 600), 3890 + (int)(now / 200 % 3), position);
        if (actor.ElementalBarrier) DrawClip(new("Magic3", 1890, 10, 2000), 1890 + (int)(now / 200 % 10), position);
        if (actor.Concentrating && !actor.ConcentrationInterrupted) DrawClip(new("Magic3", 1860, 8, 800), 1860 + (int)(now / 100 % 8), position);
        foreach (var type in actor.Elements.Keys)
        {
            var index = 1630 + ((int)type - 1) * 10;
            DrawClip(new("Magic3", index, 10, 1000), index + (int)(now / 100 % 10), position);
        }
    }
    private void DrawSprite(string library, int index, Vector2 position, float opacity = 1, GColor? tint = null)
    { var image = Assets.Sprite(library, index); var colour = tint ?? Colors.White; colour.A = opacity; if (image != null) DrawTexture(image.Texture, position + image.Offset, colour); }

}
