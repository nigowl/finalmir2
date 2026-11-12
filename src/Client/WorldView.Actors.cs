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

    private static string ActorLibrary(Actor actor) => actor.Kind == ObjectType.Player ? $"CArmour/{actor.Armour:00}" : actor.Kind == ObjectType.Merchant
        ? actor.Image >= 1000 ? $"Flag/{actor.Image - 1000:00}" : $"NPC/{actor.Image:00}"
        : actor.Image >= 10000 ? $"Pet/{actor.Image - 10000:00}" : actor.Image is >= 950 and < 960 ? $"Gate/{actor.Image - 950:00}"
        : actor.Image is >= 940 and < 950 ? $"Siege/{actor.Image - 940:00}" : $"Monster/{actor.Image:000}";

    internal int ActionDuration(Actor actor) => Assets.Frames(ActorLibrary(actor))?.Frames.GetValueOrDefault(actor.Action)?.Duration ?? Actor.MoveDuration;

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
        if (actor == null || Assets.Frames(ActorLibrary(actor))?.Frames.TryGetValue(action, out var frame) != true) return false;
        var bodyOffset = actor.Gender == MirGender.Female ? 808 : 0;
        var ready = Assets.PrepareAnimation(ActorLibrary(actor), frame, direction, bodyOffset);
        ready &= Assets.PrepareAnimation($"CHair/{actor.Hair:00}", frame, direction, bodyOffset);
        if (actor.Weapon >= 0) ready &= Assets.PrepareAnimation($"CWeapon/{actor.Weapon:00}", frame, direction, actor.Gender == MirGender.Female ? 416 : 0);
        return ready;
    }

    private Client.MirObjects.Frame? ActorFrame(Actor actor, long now, out int index)
    {
        index = 0;
        var frames = Assets.Frames(ActorLibrary(actor));
        if (frames == null) return null;
        var action = actor.Dead ? MirAction.Die : actor.Action;
        var elapsed = Math.Max(0, now - actor.ActionTime);
        if (frames.Frames.TryGetValue(action, out var active))
        {
            var duration = action is MirAction.Walking or MirAction.Running ? actor.MovementDuration(active.Duration) : active.Duration;
            if (elapsed >= duration) action = actor.Dead ? MirAction.Dead : MirAction.Standing;
            else if (action is MirAction.Walking or MirAction.Running) elapsed = elapsed * active.Duration / duration;
        }
        if (!frames.Frames.TryGetValue(action, out var frame) && !frames.Frames.TryGetValue(MirAction.Standing, out frame)) return null;
        index = frame.Start + (int)actor.Direction * frame.OffSet + frame.IndexAt(action == MirAction.Standing ? now : elapsed, action == MirAction.Standing);
        return frame;
    }

    internal SpriteImage? ActorBody(Actor actor, long now) => ActorFrame(actor, now, out var index) == null ? null :
        Assets.Sprite(ActorLibrary(actor), index + (actor.Kind == ObjectType.Player && actor.Gender == MirGender.Female ? 808 : 0));

    private void DrawActor(Actor actor, long now)
    {
        if (actor.Hidden) return;
        var position = Screen(Visual(actor, now));
        if (position.X < -250 || position.X > 1300 || position.Y < -250 || position.Y > 900) return;
        if (actor.Kind == ObjectType.Item)
        {
            var item = Assets.Sprite("DNItems", actor.Image);
            if (item != null) DrawTexture(item.Texture, position + (new Vector2(48, 32) - item.Size) / 2);
            return;
        }
        var library = ActorLibrary(actor);
        var frame = ActorFrame(actor, now, out var index);
        if (frame == null) return;
        Assets.PrepareAnimation(library, frame, actor.Direction, actor.Kind == ObjectType.Player && actor.Gender == MirGender.Female ? 808 : 0);
        if (actor.Kind == ObjectType.Player)
        {
            var weaponBehind = actor.Direction is MirDirection.Left or MirDirection.Up or MirDirection.UpLeft or MirDirection.DownLeft;
            if (weaponBehind && actor.Weapon >= 0) DrawSprite($"CWeapon/{actor.Weapon:00}", index + (actor.Gender == MirGender.Female ? 416 : 0), position);
            DrawSprite(library, index + (actor.Gender == MirGender.Female ? 808 : 0), position);
            DrawSprite($"CHair/{actor.Hair:00}", index + (actor.Gender == MirGender.Female ? 808 : 0), position);
            if (!weaponBehind && actor.Weapon >= 0) DrawSprite($"CWeapon/{actor.Weapon:00}", index + (actor.Gender == MirGender.Female ? 416 : 0), position);
        }
        else DrawSprite(library, index, position);
        if (actor.Kind == ObjectType.Merchant && State.QuestsAt(actor.Id).FirstOrDefault() is { } quest)
        {
            var progress = State.Quests.GetValueOrDefault(quest.Index);
            var marker = Assets.Sprite("Prguse", 981 + (int)quest.GetQuestIcon(progress != null, progress?.Completed == true) * 2 + (int)(now / 350 % 2));
            var body = Assets.Sprite(library, frame.Start);
            if (marker != null && body != null) DrawTexture(marker.Texture, position + body.Offset + new Vector2(body.Size.X / 2 - 28, -40));
        }
    }
    private void DrawSprite(string library, int index, Vector2 position)
    { var image = Assets.Sprite(library, index); if (image != null) DrawTexture(image.Texture, position + image.Offset); }

}
