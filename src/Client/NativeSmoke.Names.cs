using Godot;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyActorNameCentres(Actor npc, Actor monster)
    {
        stage = "player/NPC/monster body-centred names";
        var pixels = new byte[12 * 8 * 4];
        for (var y = 1; y <= 4; y++)
        for (var x = 1; x <= 4; x++) { var p = (y * 12 + x) * 4; pixels[p] = pixels[p + 3] = 255; }
        for (var y = 2; y < 8; y++)
        for (var x = 6; x < 12; x++) pixels[(y * 12 + x) * 4 + 3] = 255;
        pixels[^4] = 255; pixels[^1] = 127;
        if (ResourceCache.MeasureBodyCentre(pixels, 12, 8) != new Vector2(2.5f, 2.5f) || ResourceCache.MeasureBodyCentre(new byte[16], 2, 2) != Vector2.One)
            throw new Exception("Body name centres included opaque black shadows/translucent padding or lost the empty-frame fallback.");
        foreach (var actor in new[] { State.Me!, npc, monster })
        {
            await Until(() => Client.World.ActorBody(actor, Environment.TickCount64) != null);
            var timestamp = Environment.TickCount64;
            var body = Client.World.ActorBody(actor, timestamp)!;
            var motion = ((Client.World.Visual(actor, timestamp) - new Vector2(actor.Location.X, actor.Location.Y)) * new Vector2(48, 32)).Round();
            var rect = new Rect2(Client.World.Screen(actor.Location) + motion + body.Offset, body.Size);
            var anchor = Client.World.NameAnchor(actor, timestamp);
            if (anchor.DistanceTo(rect.Position + body.BodyCentre) > 1 || !rect.HasPoint(anchor))
                throw new Exception($"{actor.Kind} name did not use its original sprite's body centre: {anchor}, {rect}.");
        }
        var probe = new Actor { Kind = monster.Kind, Image = monster.Image, Location = monster.Location };
        var now = Environment.TickCount64;
        probe.Move(new Point(probe.Location.X + 1, probe.Location.Y), MirDirection.Right, MirAction.Walking, now);
        var duration = Client.World.ActionDuration(probe);
        await Until(() => new[] { now, now + duration / 2, now + duration }.All(time => Client.World.ActorBody(probe, time) != null));
        Vector2 Origin(long time) { var body = Client.World.ActorBody(probe, time)!; return Client.World.NameAnchor(probe, time) - body.Offset - body.BodyCentre; }
        var first = Origin(now); var middle = Origin(now + duration / 2); var end = Origin(now + duration);
        if ((middle - first).DistanceTo(new Vector2(24, 0)) > 1 || (end - middle).DistanceTo(new Vector2(24, 0)) > 1)
            throw new Exception("Monster name jumped to its destination instead of following its interpolated body.");
        var names = Client.World.ShowMonsterNames; Client.World.ShowMonsterNames = true;
        await Screenshot("body-centred-names"); Client.World.ShowMonsterNames = names;
        GD.Print("PASS native names: player/NPC/monster body centres ignore shadows/padding and follow continuous movement; ground labels keep their anchors.");
    }
}
