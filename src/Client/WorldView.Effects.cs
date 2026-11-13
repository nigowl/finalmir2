using Client.MirObjects;
using Godot;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class WorldView
{
    private sealed class VisualEffect
    {
        public SpellEffectClip Clip;
        public uint Owner, Target;
        public Point Source, Destination;
        public long Start, Added = Environment.TickCount64;
        public int RepeatFor, Sound;
        public bool Ready, Played;
        public SpellEffect Tag;
        public SpellMissile? Missile;
        public Action? Completed;
    }
    private readonly List<VisualEffect> effects = new();
    private readonly Queue<(string Library, int Index)> effectWarmup = new();
    internal event Action<int>? SoundRequested;
    internal event Action<string, int>? EffectFrameDrawn;
    internal int EffectCount => effects.Count;
    internal int EffectFramesDrawn { get; private set; }
    private void ClearEffects() { effects.Clear(); damage.Clear(); effectWarmup.Clear(); }

    private bool PrepareClip(SpellEffectClip clip)
    {
        var ready = true;
        for (var i = 0; i < clip.Count; i++) ready &= Assets.PrepareSprite(clip.Library, clip.Index + i);
        return ready;
    }

    private void AddEffect(SpellEffectClip clip, uint owner, Point location, long start, int repeatFor = 0,
        SpellEffect tag = SpellEffect.None, int sound = 0)
    {
        if (effects.Count >= 512) return;
        PrepareClip(clip);
        effects.Add(new VisualEffect { Clip = clip, Owner = owner, Source = location, Start = start, RepeatFor = repeatFor, Tag = tag, Sound = sound });
    }

    private void AddMissile(Spell spell, uint caster, uint target, Point source, Point destination, long start)
    {
        if (effects.Count >= 512 || OriginalSpellEffects.Missile(spell) is not { } missile) return;
        var direction = Direction16(source, destination);
        var clip = missile.Flight with { Index = missile.Flight.Index + direction * missile.DirectionStride };
        PrepareClip(clip);
        if (missile.Impact is { } impact) PrepareClip(impact);
        var effect = new VisualEffect { Clip = clip, Target = missile.GroundImpact ? 0 : target, Source = source,
            Destination = destination, Start = start, Missile = missile, Sound = MissileSound(spell) };
        effect.Completed = () =>
        {
            var point = State.Actors.GetValueOrDefault(effect.Target)?.Location ?? destination;
            if (missile.Impact is { } hit && (missile.GroundImpact || State.Actors.GetValueOrDefault(target)?.Dead == false))
                AddEffect(hit, missile.GroundImpact ? 0 : target, point, Environment.TickCount64, sound: ImpactSound(spell));
            if (spell == Spell.VampireShot) AddEffect(new("Magic3", 2100, 8, 1000), caster, source, Environment.TickCount64);
        };
        effects.Add(effect);
    }

    private void ProcessEffects(long now)
    {
        for (var i = 0; i < 8 && effectWarmup.Count > 0; i++)
        {
            var frame = effectWarmup.Dequeue();
            if (!Assets.PrepareSprite(frame.Library, frame.Index)) effectWarmup.Enqueue(frame);
        }
        foreach (var effect in effects.ToArray())
        {
            if (effect.Owner != 0 && (!State.Actors.TryGetValue(effect.Owner, out var owner) || owner.Hidden && owner.Id != State.User?.ObjectID || owner.Dead)) { effects.Remove(effect); continue; }
            if (!effect.Ready)
            {
                effect.Ready = PrepareClip(effect.Clip);
                if (!effect.Ready && now - effect.Added > 10000) { effects.Remove(effect); Assets.Errors.Enqueue($"技能特效加载超时：{effect.Clip.Library}/{effect.Clip.Index}"); }
                if (!effect.Ready || now < effect.Start) continue;
                effect.Start = Math.Max(effect.Start, now);
            }
            if (now < effect.Start) continue;
            if (!effect.Played) { effect.Played = true; if (effect.Sound > 0) SoundRequested?.Invoke(effect.Sound); }
            if (effect.Missile != null && State.Actors.TryGetValue(effect.Target, out var target)) effect.Destination = target.Location;
            var duration = effect.Missile != null ? OriginalSpellEffects.FlightDuration(effect.Source, effect.Destination) : effect.RepeatFor != 0 ? effect.RepeatFor : effect.Clip.Duration;
            if (duration < 0 || now - effect.Start < duration) continue;
            effects.Remove(effect); effect.Completed?.Invoke();
        }
    }

    private void DrawSpells(long now)
    {
        EffectFramesDrawn = 0;
        foreach (var effect in effects)
        {
            if (!effect.Ready || now < effect.Start) continue;
            var elapsed = now - effect.Start;
            var position = State.Actors.TryGetValue(effect.Owner, out var actor) ? Screen(Visual(actor, now)) : Screen(effect.Source);
            if (effect.Missile is { } missile)
            {
                var duration = OriginalSpellEffects.FlightDuration(effect.Source, effect.Destination);
                var target = State.Actors.TryGetValue(effect.Target, out var targetActor) ? Screen(Visual(targetActor, now)) : Screen(effect.Destination);
                position = position.Lerp(target, Math.Clamp(elapsed / (float)duration, 0, 1));
                DrawClip(effect.Clip, (int)(elapsed / missile.Interval % effect.Clip.Count) + effect.Clip.Index, position);
            }
            else DrawClip(effect.Clip, effect.Clip.FrameAt(elapsed, effect.RepeatFor != 0), position);
        }
    }

    private void DrawClip(SpellEffectClip clip, int frame, Vector2 position)
    {
        if (frame < 0 || Assets.Sprite(clip.Library, frame) is not { } image) return;
        position = (position + image.Offset + new Vector2(clip.X, clip.Y)).Round();
        if (clip.Blend) glow.Add((image, position, clip.Opacity));
        else DrawTexture(image.Texture, position, new Color(1, 1, 1, clip.Opacity));
        EffectFramesDrawn++;
        EffectFrameDrawn?.Invoke(clip.Library, frame);
    }

    private static int Direction16(Point source, Point target)
    {
        var angle = Math.Atan2(target.X - source.X, source.Y - target.Y);
        return ((int)Math.Round(angle * 8 / Math.PI) + 16) % 16;
    }
}
