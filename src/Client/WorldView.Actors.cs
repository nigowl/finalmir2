using Client.MirObjects;
using Godot;
using Point = System.Drawing.Point;
using GColor = Godot.Color;

namespace Crystal.Native;

public partial class WorldView
{
    internal Vector2 Visual(Actor actor, long? timestamp = null)
    {
        var duration = actor.Kind == ObjectType.Player ? Actor.MoveDuration
            : Assets.Frames(ActorLibrary(actor))?.Frames.GetValueOrDefault(actor.Action)?.Duration ?? Actor.MoveDuration;
        var point = actor.PositionAt(timestamp ?? Environment.TickCount64, duration);
        return new Vector2(point.X, point.Y);
    }

    private static PlayerArt PlayerArt(Actor actor, MirAction action) => OriginalPlayerArt.Resolve(actor.Class, actor.Gender, actor.Armour, actor.Hair, actor.Weapon, action);
    private static string ActorLibrary(Actor actor, MirAction? action = null) => actor.Kind == ObjectType.Player ? PlayerArt(actor, action ?? actor.Action).Body : actor.Kind == ObjectType.Merchant
        ? actor.Image >= 1000 ? $"Flag/{actor.Image - 1000:00}" : $"NPC/{actor.Image:00}"
        : actor.Image >= 10000 ? $"Pet/{actor.Image - 10000:00}" : actor.Image is >= 950 and < 960 ? $"Gate/{actor.Image - 950:00}"
        : actor.Image is >= 940 and < 950 ? $"Siege/{actor.Image - 940:00}" : $"Monster/{actor.Image:000}";

    internal int ActionDuration(Actor actor) => !actor.Casting && actor.ActionSpell == Spell.DoubleSlash && actor.Action == MirAction.Attack1 ? 552 :
        Assets.Frames(ActorLibrary(actor))?.Frames.GetValueOrDefault(actor.Action)?.Duration ?? Actor.MoveDuration;

    internal Vector2 NameAnchor(Actor actor, long now)
    {
        var position = Screen(Visual(actor, now));
        if (actor.Kind is ObjectType.Player or ObjectType.Merchant or ObjectType.Monster)
        {
            var body = ActorBody(actor, now);
            return position + (body == null ? new Vector2(24, -24) : body.Offset + body.BodyCentre);
        }
        return position + new Vector2(24, actor.Kind == ObjectType.Item ? -4 : -46);
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
        if (OriginalSpellEffects.Melee(spell, level, direction) is { } clip) ready &= PrepareClip(clip);
        if (spell == Spell.DoubleSlash)
        {
            ready &= PlayerAnimationReady(direction, MirAction.Attack4);
            if (OriginalSpellEffects.Melee(spell, level, direction, true) is { } second) ready &= PrepareClip(second);
        }
        return ready;
    }

    private Client.MirObjects.Frame? ActorFrame(Actor actor, long now, out int index)
    {
        index = 0;
        var frames = Assets.Frames(ActorLibrary(actor, RenderAction(actor, now)));
        if (frames == null) return null;
        var action = actor.Dead ? MirAction.Die : actor.Action;
        if (actor.Kind == ObjectType.Player) action = OriginalPlayerArt.Animation(actor.Class, actor.Weapon, action);
        var elapsed = Math.Max(0, now - actor.ActionTime);
        if (!actor.Casting && actor.ActionSpell == Spell.DoubleSlash && action == MirAction.Attack1)
        {
            if (elapsed >= 552) action = MirAction.Standing;
            else { action = elapsed >= 276 ? MirAction.Attack4 : MirAction.Attack1; elapsed = elapsed % 276 * 100 / 46; }
        }
        if (frames.Frames.TryGetValue(action, out var active))
        {
            var duration = action is MirAction.Walking or MirAction.Running or MirAction.WalkingBow or MirAction.RunningBow ? actor.MovementDuration(active.Duration) : active.Duration;
            if (elapsed >= duration) action = actor.Dead ? MirAction.Dead : MirAction.Standing;
            else if (action is MirAction.Walking or MirAction.Running or MirAction.WalkingBow or MirAction.RunningBow) elapsed = elapsed * active.Duration / duration;
        }
        if (!frames.Frames.TryGetValue(action, out var frame) && !frames.Frames.TryGetValue(MirAction.Standing, out frame)) return null;
        index = frame.Start + (int)actor.Direction * frame.OffSet + frame.IndexAt(action == MirAction.Standing ? now : elapsed, action == MirAction.Standing);
        return frame;
    }

    private MirAction RenderAction(Actor actor, long now)
    {
        var action = actor.Dead ? MirAction.Die : actor.Action;
        if (actor.Kind != ObjectType.Player) return action;
        var duration = !actor.Casting && actor.ActionSpell == Spell.DoubleSlash && action == MirAction.Attack1 ? 552 : FrameSet.Player.GetValueOrDefault(action)?.Duration ?? 600;
        return now - actor.ActionTime < duration ? action : actor.Dead ? MirAction.Dead : MirAction.Standing;
    }

    internal SpriteImage? ActorBody(Actor actor, long now) => ActorFrame(actor, now, out var index) == null ? null :
        Assets.Sprite(ActorLibrary(actor, RenderAction(actor, now)), index + (actor.Kind == ObjectType.Player ? PlayerArt(actor, RenderAction(actor, now)).BodyOffset : 0));

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
        var library = ActorLibrary(actor, RenderAction(actor, now));
        var frame = ActorFrame(actor, now, out var index);
        if (frame == null) return;
        var art = PlayerArt(actor, RenderAction(actor, now));
        var originalTint = OriginalSpellEffects.PoisonColour(actor.Poison);
        var tint = GColor.Color8(originalTint.R, originalTint.G, originalTint.B);
        Assets.PrepareAnimation(library, frame, actor.Direction, actor.Kind == ObjectType.Player ? art.BodyOffset : 0);
        if (actor.Kind == ObjectType.Player)
        {
            var opacity = actor.Hidden ? .5f : 1f;
            var weaponBehind = actor.Direction is MirDirection.Left or MirDirection.Up or MirDirection.UpLeft or MirDirection.DownLeft;
            if (weaponBehind && actor.Weapon >= 0) DrawSprite(art.Weapon, index + art.WeaponOffset, position, opacity, tint);
            if (!weaponBehind && art.SecondWeapon.Length > 0) DrawSprite(art.SecondWeapon, index + art.WeaponOffset, position, opacity, tint);
            DrawSprite(library, index + art.BodyOffset, position, opacity, tint);
            DrawSprite(art.Hair, index + art.BodyOffset, position, opacity, tint);
            if (!weaponBehind && actor.Weapon >= 0) DrawSprite(art.Weapon, index + art.WeaponOffset, position, opacity, tint);
            if (weaponBehind && art.SecondWeapon.Length > 0) DrawSprite(art.SecondWeapon, index + art.WeaponOffset, position, opacity, tint);
            DrawPlayerEffects(actor, now, position);
        }
        else DrawSprite(library, index, position, 1, tint);
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
        var elapsed = now - actor.ActionTime;
        if (!actor.Dead && !actor.Casting && actor.Action is MirAction.Attack1 or MirAction.Attack4 &&
            OriginalSpellEffects.Melee(actor.ActionSpell, actor.SpellLevel, actor.Direction, actor.Action == MirAction.Attack4 || elapsed >= 276 && actor.ActionSpell == Spell.DoubleSlash) is { } melee)
        {
            if (actor.ActionSpell != Spell.DoubleSlash || elapsed < 552)
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
