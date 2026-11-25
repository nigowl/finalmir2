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
            await Until(() => Client.World.NameBody(actor) != null);
            var timestamp = Environment.TickCount64;
            var body = Client.World.NameBody(actor)!;
            var probe = new Actor { Kind = actor.Kind, Image = actor.Image, Class = actor.Class, Gender = actor.Gender,
                Armour = actor.Armour, Weapon = actor.Weapon, Location = actor.Location };
            var expected = Client.World.Screen(probe.Location) + body.Offset + body.BodyCentre;
            foreach (var action in new[] { MirAction.Standing, MirAction.Attack1, MirAction.Harvest, MirAction.Spell })
            {
                probe.Move(probe.Location, MirDirection.Up, action, timestamp);
                for (var elapsed = 0; elapsed < 1600; elapsed += 50)
                    if (Client.World.NameAnchor(probe, timestamp + elapsed) != expected)
                        throw new Exception($"{probe.Kind} {action} 名称随静止动画或朝向变化。");
            }
        }
        var moving = new Actor { Kind = monster.Kind, Image = monster.Image, Location = monster.Location };
        var now = Environment.TickCount64;
        moving.Move(new Point(moving.Location.X + 1, moving.Location.Y), MirDirection.Right, MirAction.Walking, now);
        var duration = Client.World.ActionDuration(moving);
        var first = Client.World.NameAnchor(moving, now);
        var middle = Client.World.NameAnchor(moving, now + duration / 2);
        var end = Client.World.NameAnchor(moving, now + duration);
        if ((middle - first).DistanceTo(new Vector2(24, 0)) > 1 || (end - middle).DistanceTo(new Vector2(24, 0)) > 1)
            throw new Exception("Monster name jumped to its destination instead of following its interpolated body.");
        var names = Client.World.ShowMonsterNames; Client.World.ShowMonsterNames = true;
        await Screenshot("body-centred-names"); Client.World.ShowMonsterNames = names;
        GD.Print("PASS native names: player/NPC/monster body centres ignore shadows/padding and follow continuous movement; ground labels keep their anchors.");
    }
}
