namespace Client.MirObjects;

public readonly record struct SpellEffectClip(string Library, int Index, int Count, int Duration,
    bool Blend = true, int X = 0, int Y = 0, float Opacity = 1)
{
    public int FrameAt(long elapsed, bool repeat = false)
    {
        if (elapsed < 0 || Count <= 0 || Duration <= 0 || !repeat && elapsed >= Duration) return -1;
        return Index + (int)((repeat ? elapsed % Duration : elapsed) * Count / Duration);
    }
}

public readonly record struct SpellMissile(SpellEffectClip Flight, SpellEffectClip? Impact, int Interval = 30,
    int DirectionStride = 10, bool GroundImpact = false);

// 编号、帧数和时长来自原 PlayerObject/SpellObject，不从技能名称猜测素材。
public static partial class OriginalSpellEffects
{
    // 按要求给基础剑术普攻增加淡剑气，只复用原低阶剑光，不改变技能编号和伤害。
    public static SpellEffectClip BasicSword(MirDirection direction) => Melee(Spell.Slaying, 0, direction)!.Value with { Opacity = .38f };

    public static SpellEffectClip? Melee(Spell spell, int level, MirDirection direction, bool second = false)
    {
        SpellEffectClip? clip = spell switch
        {
            Spell.Slaying => new("Magic", 1820 + (int)direction * 10 + Math.Clamp(level, 0, 3) * 90, 6, 600),
            Spell.Thrusting => new("Magic", 2190 + (int)direction * 10 + Math.Clamp(level, 0, 3) * 90, 6, 600),
            Spell.HalfMoon => new("Magic", 2560 + (int)direction * 10 + Math.Clamp(level, 0, 3) * 90, 6, 600),
            Spell.FlamingSword => new("Magic", 3480 + (int)direction * 10, 6, 600),
            Spell.CrossHalfMoon => new("Magic2", 40 + (int)direction * 10, 6, 600),
            Spell.TwinDrakeBlade => new("Magic2", 220 + (second ? 6 : 0) + (int)direction * 20, 6, 600),
            Spell.DoubleSlash => new("Magic2", (second ? 2050 : 1960) + (int)direction * 10, 6, 276),
            _ => null
        };
        return clip is { } effect ? effect with { Opacity = .7f } : null;
    }

    public static SpellEffectClip[] Casting(Spell spell, MirDirection direction) => spell switch
    {
        Spell.FireBall => [new("Magic", 0, 10, 600)],
        Spell.Healing => [new("Magic", 200, 10, 600)],
        Spell.Repulsion => [new("Magic", 900, 6, 600)],
        Spell.ElectricShock => [new("Magic", 1560, 10, 600)],
        Spell.Poisoning => [new("Magic", 600, 10, 600)],
        Spell.GreatFireBall or Spell.FireBounce or Spell.MeteorShower => [new("Magic", 400, 10, 600)],
        Spell.HellFire => [new("Magic", 920, 10, 600)],
        Spell.ThunderBolt => [new("Magic2", 20, 3, 300)],
        Spell.SummonSkeleton => [new("Magic", 1500, 10, 600)],
        Spell.StormEscape => [new("Magic3", 590, 10, 600)],
        Spell.Teleport or Spell.Blink => [new("Magic", 1590, 10, 600)],
        Spell.Hiding => [new("Magic", 1520, 10, 600)],
        Spell.Haste => [new("Magic2", 2140 + (int)direction * 10, 6, 600)],
        Spell.Fury => [new("Magic3", 200, 8, 800), new("Magic3", 187, 10, 1000)],
        Spell.ImmortalSkin => [new("Magic3", 550, 17, 2400), new("Magic3", 570, 5, 600)],
        Spell.FireBang => [new("Magic", 1650, 10, 600)],
        Spell.FireWall => [new("Magic", 1620, 10, 600)],
        Spell.HealingCircle => [new("Magic3", 620, 10, 600)],
        Spell.MoonMist => [new("Magic3", 680, 25, 1800)],
        Spell.TrapHexagon => [new("Magic", 1380, 10, 600)],
        Spell.EnergyRepulsor => [new("Magic2", 190, 6, 600)],
        Spell.FireBurst => [new("Magic2", 2320, 10, 600)],
        Spell.FlameDisruptor => [new("Magic2", 130, 6, 600)],
        Spell.SummonShinsu => [new("Magic2", 0, 10, 600)],
        Spell.UltimateEnhancer => [new("Magic2", 160, 15, 1000)],
        Spell.FrostCrunch => [new("Magic2", 400, 10, 600)],
        Spell.Purification => [new("Magic2", 600, 10, 600)],
        Spell.FlameField => [new("Magic2", 910, 23, 1800)],
        Spell.Trap => [new("Magic2", 2340, 11, 1100)],
        Spell.MoonLight => [new("Magic2", 2380, 10, 600)],
        Spell.SwiftFeet => [new("Magic2", 2440, 16, 1600)],
        Spell.LightBody => [new("Magic2", 2470, 10, 600)],
        Spell.PoisonSword => [new("Magic2", 2490 + (int)direction * 10, 10, 1100)],
        Spell.DarkBody => [new("Magic2", 2580, 10, 1000)],
        Spell.ThunderStorm => [new("Magic", 1680, 10, 600)],
        Spell.MassHealing => [new("Magic", 1790, 10, 600)],
        Spell.IceStorm => [new("Magic", 3840, 10, 600)],
        Spell.MagicShield => [new("Magic", 3880, 10, 600)],
        Spell.TurnUndead => [new("Magic", 3920, 10, 600)],
        Spell.MagicBooster => [new("Magic3", 80, 9, 900)],
        Spell.PetEnhancer => [new("Magic3", 200, 8, 800)],
        Spell.Revelation => [new("Magic", 3960, 20, 1200)],
        Spell.ProtectionField => [new("Magic2", 1520, 10, 600)],
        Spell.Rage => [new("Magic2", 1510, 10, 600)],
        Spell.Vampirism => [new("Magic2", 1040, 7, 600)],
        Spell.LionRoar or Spell.BattleCry => [new("Magic2", 710, 20, 1200)],
        Spell.TwinDrakeBlade => [new("Magic2", 210, 6, 500)],
        Spell.Entrapment => [new("Magic2", 990, 10, 600)],
        Spell.BladeAvalanche => [new("Magic2", 740 + (int)direction * 20, 15, 1500)],
        Spell.SlashingBurst => [new("Magic2", 1700 + (int)direction * 10, 9, 900)],
        Spell.CounterAttack => [new("Magic", 3480 + (int)direction * 10, 10, 1000), new("Magic3", 140, 2, 200)],
        Spell.CrescentSlash => [new("Magic2", 2620 + (int)direction * 20, 20, 2000)],
        Spell.Mirroring => [new("Magic2", 650, 10, 600)],
        Spell.Blizzard => [new("Magic2", 1540, 8, 600)],
        Spell.MeteorStrike => [new("Magic2", 1590, 10, 600)],
        Spell.HeavenlySword => [new("Magic2", 2230 + (int)direction * 10, 8, 800)],
        Spell.ElementalBarrier => [new("Magic3", 1880, 8, 600)],
        Spell.PoisonShot => [new("Magic3", 2300, 8, 1000)],
        Spell.OneWithNature => [new("Magic3", 2710, 8, 1200)],
        _ => []
    };

    public static SpellMissile? Missile(Spell spell) => spell switch
    {
        Spell.FireBall => new(new("Magic", 10, 6, 180), new("Magic", 170, 10, 600)),
        Spell.GreatFireBall or Spell.FireBounce or Spell.MeteorShower => new(new("Magic", 410, 6, 180), new("Magic", 570, 10, 600)),
        Spell.SoulFireBall => new(new("Magic", 1160, 3, 90), new("Magic", 1360, 10, 600)),
        Spell.FrostCrunch => new(new("Magic2", 410, 4, 120), new("Magic2", 570, 8, 600)),
        Spell.CatTongue => new(new("Magic3", 260, 6, 180), new("Magic3", 420, 8, 800)),
        Spell.MassHiding => new(new("Magic", 1160, 3, 90), new("Magic", 1540, 10, 800), GroundImpact: true),
        Spell.SoulShield => new(new("Magic", 1160, 3, 90), new("Magic", 1320, 15, 1200), GroundImpact: true),
        Spell.BlessedArmour => new(new("Magic", 1160, 3, 90), new("Magic", 1340, 15, 1200), GroundImpact: true),
        Spell.Curse => new(new("Magic", 1160, 3, 90), new("Magic2", 950, 24, 2000), GroundImpact: true),
        Spell.Hallucination => new(new("Magic", 1160, 3, 144), new("Magic2", 1110, 10, 1000), 48),
        Spell.Plague => new(new("Magic", 1160, 3, 90), new("Magic3", 110, 10, 1200), GroundImpact: true),
        Spell.PoisonCloud => new(new("Magic", 1160, 3, 90), null, GroundImpact: true),
        Spell.StraightShot => new(new("Magic3", 1210, 5, 150), new("Magic3", 1370, 7, 600)),
        Spell.None or Spell.Focus or Spell.DoubleShot or Spell.DelayedExplosion => new(new("Magic3", 1030, 5, 150), null),
        Spell.ElementalShot => new(new("Magic3", 1690, 6, 180), null),
        Spell.BindingShot or Spell.SummonVampire or Spell.SummonToad or Spell.SummonSnakes => new(new("Magic3", 2750, 5, 50), null, 10),
        Spell.Stonetrap => new(new("Magic3", 2750, 5, 100), null, 20, GroundImpact: true),
        Spell.VampireShot => new(new("Magic3", 1930, 5, 50), new("Magic3", 2090, 6, 1000), 10),
        Spell.PoisonShot => new(new("Magic3", 2130, 5, 50), new("Magic3", 2290, 6, 1000), 10),
        Spell.CrippleShot => new(new("Magic3", 2330, 5, 50), new("Magic3", 2490, 7, 1000), 10),
        Spell.NapalmShot => new(new("Magic3", 2530, 6, 300), new("Magic3", 2690, 10, 1000), 50, GroundImpact: true),
        _ => null
    };

    public static SpellEffectClip? Impact(Spell spell, MirDirection direction) => spell switch
    {
        Spell.Healing => new("Magic", 370, 10, 800),
        Spell.ElectricShock => new("Magic", 1570, 10, 1000),
        Spell.Poisoning => new("Magic", 770, 10, 1000),
        Spell.ThunderBolt => new("Magic2", 10, 5, 400),
        Spell.FireBang => new("Magic", 1660, 10, 1000),
        Spell.HealingCircle => new("Magic3", 620, 10, 1200),
        Spell.MassHealing => new("Magic", 1800, 10, 1000),
        Spell.IceStorm => new("Magic", 3850, 20, 1300),
        Spell.TurnUndead => new("Magic", 3930, 15, 1000),
        Spell.IceThrust => new("Magic2", 1790 + (int)direction * 10, 10, 1000),
        Spell.Revelation => new("Magic", 3990, 10, 1000),
        Spell.FlameDisruptor => new("Magic2", 140, 10, 600),
        Spell.Purification => new("Magic2", 620, 10, 800),
        Spell.Lightning => new("Magic", 970 + (int)direction * 20, 6, 600),
        Spell.Vampirism => new("Magic2", 1060, 20, 1000),
        Spell.SummonHolyDeva => new("Magic", 1500, 10, 600),
        _ => null
    };

    public static int FlightDuration(System.Drawing.Point source, System.Drawing.Point target) =>
        Math.Max(50, Math.Max(Math.Abs(target.X - source.X), Math.Abs(target.Y - source.Y)) * 50);
}
