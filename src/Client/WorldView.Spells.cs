using Client.MirObjects;
using Point = System.Drawing.Point;
using S = ServerPackets;

namespace Crystal.Native;

public partial class WorldView
{
    public void ShowSpell(Spell spell, uint caster, uint target, Point destination, bool cast = true, byte level = 0, IEnumerable<uint>? secondary = null)
    {
        if (!State.Actors.TryGetValue(caster, out var actor)) return;
        var start = Environment.TickCount64;
        var clips = OriginalSpellEffects.Casting(spell, actor.Direction);
        for (var i = 0; i < clips.Length; i++)
            AddEffect(clips[i], spell is Spell.FlameField or Spell.ThunderStorm or Spell.MoonMist or Spell.OneWithNature ? 0 : caster,
                actor.Location, start + (spell == Spell.SlashingBurst ? 300 : 0), sound: i == 0 ? CastingSound(spell, actor.Gender) : 0);
        if (clips.Length == 0 && CastingSound(spell, actor.Gender) is > 0 and var sound) SoundRequested?.Invoke(sound);
        if (!cast) return;
        var launch = start + ActionDuration(actor);
        if (OriginalSpellEffects.Missile(spell) != null)
        {
            if (ClientSkillRules.Animation(spell) == MirAction.AttackRange2) launch = start + (spell is Spell.DoubleShot or Spell.DelayedExplosion ? 500 : 700);
            if (actor.Action == MirAction.AttackRange1) launch = start + 500;
            AddMissile(spell, caster, target, actor.Location, destination, launch);
            if (spell == Spell.Focus) AddEffect(new("Magic3", 2730, 10, 800), caster, actor.Location, start + 600);
            if (spell == Spell.DoubleShot) AddMissile(spell, caster, target, actor.Location, destination, launch + 200);
            if (spell == Spell.MeteorShower)
                foreach (var id in (secondary ?? []).Distinct().Where(id => id != target).Take(128))
                    if (State.Actors.TryGetValue(id, out var extra)) AddMissile(spell, caster, id, actor.Location, extra.Location, launch);
        }
        else if (spell == Spell.HellFire)
        {
            foreach (var offset in level == 3 ? new[] { 0, 1, 7 } : new[] { 0 })
            for (var step = 1; step <= 4; step++)
            {
                var cell = Functions.PointMove(actor.Location, (MirDirection)(((int)actor.Direction + offset) % 8), step);
                if (Map != null && (cell.X < 0 || cell.Y < 0 || cell.X >= Map.Width || cell.Y >= Map.Height)) break;
                AddEffect(new("Magic", 930, 6, 500, Opacity: .7f), 0, cell, launch + (step - 1) * 50, sound: step == 1 && offset == 0 ? ImpactSound(spell) : 0);
            }
        }
        else if (OriginalSpellEffects.Impact(spell, actor.Direction) is { } hit)
        {
            var ground = spell is Spell.FireBang or Spell.MassHealing or Spell.HealingCircle or Spell.IceStorm;
            var owner = spell is Spell.Lightning or Spell.SummonHolyDeva ? caster : ground ? 0 : target;
            var location = spell == Spell.IceThrust ? Functions.PointMove(actor.Location, actor.Direction, 1) : destination;
            AddEffect(hit, owner, location, launch, sound: ImpactSound(spell));
            if (spell == Spell.Vampirism && target != 0) AddEffect(new("Magic2", 1090, 10, 500), caster, actor.Location, launch + 1000);
        }
        if (spell == Spell.UltimateEnhancer && target != caster && target != 0)
            AddEffect(new("Magic2", 160, 15, 1000), target, destination, launch);
    }

    internal void SpellPacket(Packet packet)
    {
        CombatTextPacket(packet);
        var now = Environment.TickCount64;
        switch (packet)
        {
            case S.SetConcentration p when p.Enabled && !p.Interrupted: SoundRequested?.Invoke(21290); break;
            case S.RangeAttack p when State.User != null: ShowSpell(p.Spell, State.User.ObjectID, p.TargetID, p.Target); break;
            case S.ObjectRangeAttack p when p.ObjectID != State.User?.ObjectID && State.Actors.GetValueOrDefault(p.ObjectID)?.Kind == ObjectType.Player:
                ShowSpell(p.Spell, p.ObjectID, p.TargetID, p.Target, level: p.Level); break;
            case S.ObjectRangeAttack p when State.Actors.TryGetValue(p.ObjectID, out var guard) && guard.Kind == ObjectType.Monster &&
                OriginalSpellEffects.GuardMissile((Monster)guard.Image) is { } arrow:
                var frames = Assets.Frames(ActorLibrary(guard))?.Frames;
                var frame = frames?.GetValueOrDefault(OriginalActorFrames.Resolve(frames, guard.Action));
                AddMissile(Spell.None, guard.Id, p.TargetID, guard.Location, p.Target, now + (frame?.Interval ?? 100) * 4, arrow); break;
            case S.UserInformation: WarmSkills(); break;
            case S.NewMagic when State.User != null: WarmSkills(); break;
            case S.ObjectProjectile p when State.Actors.TryGetValue(p.Source, out var source) && State.Actors.TryGetValue(p.Destination, out var target):
                AddMissile(p.Spell, source.Id, target.Id, source.Location, target.Location, now); break;
            case S.ObjectSpell p when OriginalSpellEffects.Ground(p.Spell, p.Direction, p.Param) is { } ground:
                PrepareClip(ground.Clip);
                foreach (var clip in OriginalSpellEffects.GroundOverlay(p.Spell))
                    AddEffect(clip, p.ObjectID, p.Location, now, p.Spell == Spell.MeteorStrike ? 3000 : 0);
                if (p.Spell == Spell.ExplosiveTrap && p.Param) SoundRequested?.Invoke(21245);
                break;
            case S.ObjectEffect p when State.Actors.TryGetValue(p.ObjectID, out var actor):
                if (p.Effect == SpellEffect.MagicShieldDown) break;
                if (p.Effect is SpellEffect.MagicShieldUp or SpellEffect.ElementalBarrierUp) break;
                if (p.Effect == SpellEffect.DelayedExplosion)
                {
                    var existing = effects.FirstOrDefault(effect => effect.Owner == p.ObjectID && effect.Tag == p.Effect);
                    if (existing != null && existing.Clip.Index >= 1590 + Math.Min(p.EffectType, 2) * 10) break;
                    effects.RemoveAll(effect => effect.Owner == p.ObjectID && effect.Tag == p.Effect);
                }
                var clips = OriginalSpellEffects.Object(p.Effect, actor.Direction, p.EffectType);
                for (var i = 0; i < clips.Length; i++)
                    AddEffect(clips[i], p.ObjectID, actor.Location, now + p.DelayTime,
                        p.Effect == SpellEffect.DelayedExplosion && p.EffectType < 2 ? -1 : p.Time > 0 && p.Effect is SpellEffect.Stunned or SpellEffect.FlamingMutantWeb ? (int)Math.Min(p.Time, int.MaxValue) : 0,
                        p.Effect, i == 0 ? ObjectSound(p.Effect) : 0);
                if (p.Effect == SpellEffect.MPEater && State.Actors.TryGetValue(p.EffectType, out var other))
                    AddEffect(new("Magic2", 2411, 19, 1900), other.Id, other.Location, now);
                break;
            case S.RemoveDelayedExplosion p: effects.RemoveAll(effect => effect.Owner == p.ObjectID && effect.Tag == SpellEffect.DelayedExplosion); break;
            case S.MapEffect p when p.Effect is SpellEffect.Mine or SpellEffect.Tester:
                AddEffect(p.Effect == SpellEffect.Mine ? new("Effect", p.Value * 8, 3, 240) : new("Effect", 328, 10, 500), 0, p.Location, now,
                    sound: p.Effect == SpellEffect.Mine ? 10091 : 0); break;
            case S.Disconnect or S.LogOutSuccess or S.ReturnToLogin: ClearEffects(); break;
        }
    }

    internal void WarmSkills()
    {
        effectWarmup.Clear();
        var known = new HashSet<(string Library, int Index)>();
        foreach (var magic in State.User?.Magics ?? [])
        foreach (var direction in Enum.GetValues<MirDirection>())
        {
            var clips = OriginalSpellEffects.Casting(magic.Spell, direction).AsEnumerable();
            if (OriginalSpellEffects.Melee(magic.Spell, magic.Level, direction) is { } melee) clips = clips.Append(melee);
            if (magic.Spell == Spell.Fencing) clips = clips.Append(OriginalSpellEffects.BasicSword(direction));
            if (OriginalSpellEffects.Impact(magic.Spell, direction) is { } impact) clips = clips.Append(impact);
            if (OriginalSpellEffects.Missile(magic.Spell) is { Impact: { } hit }) clips = clips.Append(hit);
            foreach (var clip in clips)
            for (var frame = 0; frame < clip.Count && effectWarmup.Count < 2048; frame++)
                if (known.Add((clip.Library, clip.Index + frame))) effectWarmup.Enqueue((clip.Library, clip.Index + frame));
        }
    }

    private static int CastingSound(Spell spell, MirGender gender) => spell switch
    {
        Spell.None or Spell.Focus or Spell.ThunderBolt or Spell.FlameDisruptor or Spell.PoisonShot or Spell.Reincarnation => 0,
        Spell.FireBounce or Spell.MeteorShower => 20340,
        Spell.LionRoar or Spell.BattleCry or Spell.CrescentSlash or Spell.FlashDash => 20000 + (int)spell * 10 + (gender == MirGender.Female ? 1 : 0),
        Spell.CounterAttack => 20145,
        _ => 20000 + (int)spell * 10
    };
    private static int MissileSound(Spell spell) => spell switch
    {
        Spell.FireBounce or Spell.MeteorShower => 20341,
        Spell.None or Spell.Focus or Spell.DoubleShot or Spell.DelayedExplosion or Spell.BindingShot or Spell.SummonVampire
            or Spell.SummonToad or Spell.SummonSnakes or Spell.Stonetrap or Spell.VampireShot or Spell.PoisonShot => 21210,
        _ => 20000 + (int)spell * 10 + (ClientSkillRules.Animation(spell) == MirAction.AttackRange2 ? 0 : 1)
    };
    private static int ImpactSound(Spell spell) => spell switch
    {
        Spell.FireBounce or Spell.MeteorShower => 20342,
        Spell.ThunderBolt or Spell.Lightning or Spell.FlameDisruptor => 20000 + (int)spell * 10,
        Spell.Healing or Spell.ElectricShock or Spell.Poisoning or Spell.HellFire or Spell.FireBang or Spell.IceStorm
            or Spell.MassHealing or Spell.HealingCircle or Spell.TurnUndead or Spell.Vampirism or Spell.Revelation => 20000 + (int)spell * 10 + 1,
        _ => 20000 + (int)spell * 10 + 2
    };
    private static int ObjectSound(SpellEffect effect) => effect switch
    {
        SpellEffect.FatalSword or SpellEffect.MPEater or SpellEffect.FurbolgWarriorCritical => 20910,
        SpellEffect.Teleport or SpellEffect.StormEscape => 10110,
        SpellEffect.Healing => 20611,
        SpellEffect.Hemorrhage => 21040,
        SpellEffect.ElementalBarrierDown => 21315,
        _ => 0
    };
}
