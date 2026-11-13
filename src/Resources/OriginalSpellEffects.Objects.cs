namespace Client.MirObjects;

public static partial class OriginalSpellEffects
{
    public static System.Drawing.Color PoisonColour(PoisonType poison) => poison switch
    {
        _ when poison.HasFlag(PoisonType.DelayedExplosion) => System.Drawing.Color.Orange,
        _ when (poison & (PoisonType.Paralysis | PoisonType.LRParalysis)) != 0 => System.Drawing.Color.Gray,
        _ when poison.HasFlag(PoisonType.Frozen) => System.Drawing.Color.Blue,
        _ when poison.HasFlag(PoisonType.Blindness) => System.Drawing.Color.MediumVioletRed,
        _ when (poison & (PoisonType.Stun | PoisonType.Dazed)) != 0 => System.Drawing.Color.Yellow,
        _ when poison.HasFlag(PoisonType.Slow) => System.Drawing.Color.Purple,
        _ when poison.HasFlag(PoisonType.Bleeding) => System.Drawing.Color.DarkRed,
        _ when poison.HasFlag(PoisonType.Red) => System.Drawing.Color.Red,
        _ when poison.HasFlag(PoisonType.Green) => System.Drawing.Color.Green,
        _ => System.Drawing.Color.White
    };

    public static (SpellEffectClip Clip, bool Repeat)? Ground(Spell spell, MirDirection direction, bool param) => spell switch
    {
        Spell.TrapHexagon => (new("Magic", 1390, 10, 1000), true),
        Spell.FireWall => (new("Magic", 1630, 6, 720), true),
        Spell.PoisonCloud => (new("Magic2", 1650, 20, 2400), true),
        Spell.Blizzard => (new("Magic2", 1550, 30, 3000, Y: -20), false),
        Spell.MeteorStrike => (new("Magic2", 1610, 30, 3000, Y: -20), false),
        Spell.Reincarnation => (new("Magic2", 1680, 10, 1000), true),
        Spell.ExplosiveTrap => (new("Magic3", param ? 1570 : 1560, param ? 9 : 10, param ? 900 : 1000), !param),
        Spell.Trap or Spell.Portal => (new("Magic2", 2360, 8, 800), true),
        Spell.HealingCircle => (new("Magic3", 630, 11, 880), true),
        Spell.Rubble when direction != 0 => (new("Effect", 64 + Math.Min(4, (int)direction - 1), 1, 10000, false), true),
        Spell.DigOutZombie => (new($"Monster/{(int)Monster.DigOutZombie:000}", 304 + (int)direction, 1, 1000, false), true),
        Spell.DigOutArmadillo => (new($"Monster/{(int)Monster.Armadillo:000}", 472 + (int)direction, 1, 1000, false), true),
        Spell.MapLightning => (new("Dragon", 400, 5, 600), false),
        Spell.MapLava => (new("Dragon", 440, 20, 1600, false), false),
        Spell.MapQuake1 => (new($"Monster/{(int)Monster.HellLord:000}", 27, 12, 1200, false), false),
        Spell.MapQuake2 => (new($"Monster/{(int)Monster.HellLord:000}", 39, 13, 1300, false), false),
        Spell.GeneralMeowMeowThunder => (new($"Monster/{(int)Monster.GeneralMeowMeow:000}", 522, 7, 700), false),
        Spell.StoneGolemQuake => (new($"Monster/{(int)Monster.StoneGolem:000}", 368 + (int)direction * 8, 8, 800, false), false),
        Spell.EarthGolemPile => (new($"Monster/{(int)Monster.EarthGolem:000}", 441, 8, 800, false), false),
        Spell.TreeQueenMassRoots => (new($"Monster/{(int)Monster.TreeQueen:000}", 82, 15, 1500, false), false),
        Spell.TreeQueenGroundRoots => (new($"Monster/{(int)Monster.TreeQueen:000}", 48, 9, 900, false), false),
        Spell.TreeQueenRoot => (new($"Monster/{(int)Monster.TreeQueen:000}", 111, 15, 1500, false), false),
        Spell.TucsonGeneralRock => (new($"Monster/{(int)Monster.TucsonGeneral:000}", 572, 20, 2000), false),
        Spell.FlyingStatueIceTornado => (new($"Monster/{(int)Monster.FlyingStatue:000}", 314, 20, 2000), false),
        Spell.DarkOmaKingNuke => (new($"Monster/{(int)Monster.DarkOmaKing:000}", 1630 + (int)direction * 9, 9, 900), false),
        Spell.HornedSorcererDustTornado => (new($"Monster/{(int)Monster.HornedSorceror:000}", 634, 10, 1000), true),
        Spell.HornedCommanderRockFall => (new($"Monster/{(int)Monster.HornedCommander:000}", 1066, 12, 1200), true),
        Spell.HornedCommanderRockSpike => (new($"Monster/{(int)Monster.HornedCommander:000}", 1190, 9, 900, false), true),
        _ => null
    };

    public static SpellEffectClip[] GroundOverlay(Spell spell) => spell switch
    {
        Spell.MeteorStrike => [new("Magic2", 1600, 10, 800)],
        Spell.MapLava => [new("Dragon", 470, 10, 800)],
        Spell.TreeQueenMassRoots => [new($"Monster/{(int)Monster.TreeQueen:000}", 97, 14, 1400)],
        Spell.TreeQueenGroundRoots => [new($"Monster/{(int)Monster.TreeQueen:000}", 57, 9, 900)],
        Spell.TucsonGeneralRock => [new($"Monster/{(int)Monster.TucsonGeneral:000}", 552, 20, 2000, false)],
        Spell.HornedCommanderRockSpike => [new($"Monster/{(int)Monster.HornedCommander:000}", 1199, 9, 900)],
        _ => []
    };

    public static SpellEffectClip[] Object(SpellEffect effect, MirDirection direction, uint variant = 0) => effect switch
    {
        SpellEffect.FatalSword => [new("Magic2", 1940, 4, 400)],
        SpellEffect.Teleport => [new("Magic", 1600, 10, 600)],
        SpellEffect.StormEscape => [new("Magic3", 610, 10, 600)],
        SpellEffect.Healing => [new("Magic", 370, 10, 800)],
        SpellEffect.RedMoonEvil => [new($"Monster/{(int)Monster.RedMoonEvil:000}", 32, 6, 400, false)],
        SpellEffect.TwinDrakeBlade => [new("Magic2", 380, 6, 800)],
        SpellEffect.MPEater => [new("Magic2", 2400, 9, 900)],
        SpellEffect.Bleeding => [new("Magic3", 60, 3, 400)],
        SpellEffect.Hemorrhage => [new("Magic3", 0, 4, 400), new("Magic3", 28, 6, 600), new("Magic3", 46, 8, 800)],
        SpellEffect.MagicShieldUp => [new("Magic", 3890, 3, 600)],
        SpellEffect.GreatFoxSpirit => [new($"Monster/{(int)Monster.GreatFoxSpirit:000}", 375 + (int)(variant % 3) * 20, 20, 1400)],
        SpellEffect.Entrapment => [new("Magic2", 1010, 10, 1500), new("Magic2", 1020, 8, 1200)],
        SpellEffect.Reflect => [new("Effect", 580, 10, 70)],
        SpellEffect.ElementalBarrierUp => [new("Magic3", 1890, 10, 2000)],
        SpellEffect.ElementalBarrierDown => [new("Magic3", 1910, 7, 1400)],
        SpellEffect.DelayedExplosion => [new("Magic3", 1590 + (int)Math.Min(variant, 2) * 10, 8, 1200)],
        SpellEffect.AwakeningSuccess => [new("Magic3", 900, 16, 1600), new("Magic3", 840, 16, 1600, false)],
        SpellEffect.AwakeningFail => [new("Magic3", 920, 9, 900), new("Magic3", 860, 9, 900, false)],
        SpellEffect.AwakeningHit => [new("Magic3", 880, 5, 500), new("Magic3", 820, 5, 500, false)],
        SpellEffect.AwakeningMiss => [new("Magic3", 890, 5, 500), new("Magic3", 830, 5, 500, false)],
        SpellEffect.TurtleKing => [new($"Monster/{(int)Monster.TurtleKing:000}", variant % 2 == 0 ? 922 : 934, 12, 1200)],
        SpellEffect.Behemoth => [new($"Monster/{(int)Monster.Behemoth:000}", 788, 10, 1500), new($"Monster/{(int)Monster.Behemoth:000}", 778, 10, 1500, false)],
        SpellEffect.Stunned => [new($"Monster/{(int)Monster.StoningStatue:000}", 632, 10, 1000)],
        SpellEffect.IcePillar => [new($"Monster/{(int)Monster.IcePillar:000}", 18, 8, 800)],
        SpellEffect.KingGuard => [new($"Monster/{(int)Monster.KingGuard:000}", variant == 0 ? 753 : 763, 10, 1000, false)],
        SpellEffect.FlamingMutantWeb => [new($"Monster/{(int)Monster.FlamingMutant:000}", 330, 10, 1000)],
        SpellEffect.DeathCrawlerBreath => [new($"Monster/{(int)Monster.DeathCrawler:000}", 272 + (int)direction * 4, 4, 400)],
        SpellEffect.FurbolgWarriorCritical => [new($"Monster/{(int)Monster.FurbolgWarrior:000}", 400, 6, 600)],
        SpellEffect.MoonMist => [new("Magic3", 705, 10, 800)],
        _ => []
    };
}
