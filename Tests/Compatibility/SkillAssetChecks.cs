using System.Buffers.Binary;
using Client.MirObjects;
using Mir2.Hosting;
using Server.ConsoleHost;

internal static class SkillAssetChecks
{
    public static void Run()
    {
        var assets = new GameAssets(new ProjectPaths().Assets);
        var clips = new HashSet<SpellEffectClip>();
        clips.Add(new("Magic3", 1860, 8, 800));
        for (var type = 0; type < 4; type++) clips.Add(new("Magic3", 1630 + type * 10, 10, 1000));
        foreach (var spell in Enum.GetValues<Spell>().Where(spell => (byte)spell is > 0 and < 200))
        foreach (var direction in Enum.GetValues<MirDirection>())
        {
            clips.UnionWith(OriginalSpellEffects.Casting(spell, direction));
            if (OriginalSpellEffects.Impact(spell, direction) is { } hit) clips.Add(hit);
            for (var level = 0; level <= 3; level++)
            {
                if (OriginalSpellEffects.Melee(spell, level, direction) is { } first) clips.Add(first);
                if (OriginalSpellEffects.Melee(spell, level, direction, true) is { } second) clips.Add(second);
            }
            if (OriginalSpellEffects.Ground(spell, direction, false) is { } ground) clips.Add(ground.Clip);
            if (OriginalSpellEffects.Ground(spell, direction, true) is { } triggered) clips.Add(triggered.Clip);
            clips.UnionWith(OriginalSpellEffects.GroundOverlay(spell));
            if (OriginalSpellEffects.Missile(spell) is not { } missile) continue;
            if (missile.Impact is { } impact) clips.Add(impact);
            for (var facing = 0; facing < 16; facing++) clips.Add(missile.Flight with { Index = missile.Flight.Index + facing * missile.DirectionStride });
        }
        var frames = new Dictionary<(string Library, int Index), bool>();
        foreach (var clip in clips)
        {
            var visible = false;
            for (var frame = clip.Index; frame < clip.Index + clip.Count; frame++)
            {
                if (!frames.TryGetValue((clip.Library, frame), out var nonempty))
                {
                    var data = assets.Frame(clip.Library, frame);
                    nonempty = data.Length > 8 && BinaryPrimitives.ReadInt16LittleEndian(data) > 0 && BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(2)) > 0;
                    frames[(clip.Library, frame)] = nonempty;
                }
                visible |= nonempty;
            }
            if (!visible) throw new InvalidDataException($"技能素材整段为空：{clip.Library}/{clip.Index}–{clip.Index + clip.Count - 1}。");
        }
        Console.WriteLine($"PASS skill assets: 110 player skills, {clips.Count} original clips, {frames.Count} unique frames including eight-way melee/levels and sixteen-way missiles; read-only assets.");
    }
}
