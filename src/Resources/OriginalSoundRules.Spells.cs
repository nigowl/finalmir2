namespace Client.MirObjects;

public static partial class OriginalSoundRules
{
    public static int MeleeSound(Spell spell, MirGender gender, bool second = false) => spell switch
    {
        Spell.DoubleSlash => 20920 + (second ? 1 : 0),
        Spell.Slaying => 20020 + (gender == MirGender.Female ? 1 : 0),
        Spell.FlamingSword => 20081,
        Spell.Thrusting or Spell.HalfMoon or Spell.CrossHalfMoon or Spell.TwinDrakeBlade => 20000 + (int)spell * 10,
        _ => 0
    };

    public static int GroundSound(Spell spell, bool triggered) => spell switch
    {
        Spell.ExplosiveTrap when triggered => 21245,
        Spell.MapLightning => 8301, Spell.MapLava => 8302,
        Spell.MapQuake1 or Spell.MapQuake2 or Spell.StoneGolemQuake => 8304,
        Spell.GeneralMeowMeowThunder => 8321, Spell.EarthGolemPile => 8331,
        Spell.TreeQueenMassRoots => 8341, Spell.TreeQueenGroundRoots => 8342, Spell.TreeQueenRoot => 8343,
        Spell.FlyingStatueIceTornado => 8303, Spell.DarkOmaKingNuke => (int)Monster.DarkOmaKing * 10 + 9,
        Spell.HornedSorcererDustTornado => 8306, Spell.HornedCommanderRockFall => 8456,
        Spell.HornedCommanderRockSpike => 8457,
        _ => 0
    };

    public static int CastingSound(Spell spell, MirGender gender) => spell switch
    {
        Spell.None or Spell.Focus or Spell.ThunderBolt or Spell.FlameDisruptor or Spell.PoisonShot or Spell.Reincarnation
            or Spell.Fencing or Spell.SpiritSword or Spell.FatalSword or Spell.MPEater => 0,
        Spell.FireBounce or Spell.MeteorShower => 20340,
        Spell.LionRoar or Spell.BattleCry or Spell.CrescentSlash or Spell.FlashDash => 20000 + (int)spell * 10 + (gender == MirGender.Female ? 1 : 0),
        Spell.CounterAttack => 20145,
        Spell.FireBall or Spell.Healing or Spell.Repulsion or Spell.ElectricShock or
        Spell.Poisoning or Spell.GreatFireBall or Spell.HellFire or Spell.SoulFireBall or
        Spell.SummonSkeleton or Spell.StormEscape or Spell.Teleport or Spell.Blink or
        Spell.Hiding or Spell.Haste or Spell.Fury or Spell.ImmortalSkin or
        Spell.FireBang or Spell.FireWall or Spell.HealingCircle or Spell.MoonMist or
        Spell.TrapHexagon or Spell.EnergyRepulsor or Spell.FireBurst or Spell.SummonShinsu or
        Spell.UltimateEnhancer or Spell.FrostCrunch or Spell.Purification or Spell.FlameField or
        Spell.Trap or Spell.MoonLight or Spell.SwiftFeet or Spell.LightBody or
        Spell.PoisonSword or Spell.DarkBody or Spell.ThunderStorm or Spell.MassHealing or
        Spell.IceStorm or Spell.MagicShield or Spell.TurnUndead or Spell.MagicBooster or
        Spell.PetEnhancer or Spell.Revelation or Spell.ProtectionField or Spell.Rage or
        Spell.Vampirism or Spell.TwinDrakeBlade or Spell.Entrapment or Spell.BladeAvalanche or
        Spell.SlashingBurst or Spell.Mirroring or Spell.Blizzard or Spell.MeteorStrike or
        Spell.HeavenlySword or Spell.ElementalBarrier or Spell.OneWithNature => 20000 + (int)spell * 10,
        _ => 0
    };
    public static int MissileSound(Spell spell, bool archer = false) => spell switch
    {
        Spell.FireBounce or Spell.MeteorShower => 20341,
        Spell.MassHiding or Spell.SoulShield or Spell.BlessedArmour or Spell.CatTongue => 20000 + (int)spell * 10,
        Spell.Plague or Spell.Curse or Spell.Hallucination or Spell.PoisonCloud or Spell.ElementalShot => 0,
        Spell.CrippleShot => 21360,
        Spell.None or Spell.Focus or Spell.DoubleShot or Spell.DelayedExplosion or Spell.BindingShot or Spell.SummonVampire
            or Spell.SummonToad or Spell.SummonSnakes or Spell.Stonetrap or Spell.VampireShot or Spell.PoisonShot => 21210,
        _ => 20000 + (int)spell * 10 + (archer ? 0 : 1)
    };
    public static int ImpactSound(Spell spell) => spell switch
    {
        Spell.FireBounce or Spell.MeteorShower => 20342,
        Spell.None or Spell.Focus or Spell.DoubleShot or Spell.DelayedExplosion => 21212,
        Spell.BindingShot or Spell.SummonVampire or Spell.SummonToad or Spell.SummonSnakes or Spell.Stonetrap => 20000 + (int)spell * 10 + 7,
        Spell.CrippleShot => 21365,
        Spell.Plague => 20823,
        Spell.ThunderBolt or Spell.Lightning or Spell.FlameDisruptor or Spell.IceThrust or Spell.SummonHolyDeva
            or Spell.Curse or Spell.Hallucination or Spell.PoisonCloud => 20000 + (int)spell * 10,
        Spell.MassHiding or Spell.SoulShield or Spell.BlessedArmour or Spell.CatTongue => 20000 + (int)spell * 10 + 1,
        Spell.Purification => 0,
        Spell.Healing or Spell.ElectricShock or Spell.Poisoning or Spell.HellFire or Spell.FireBang or Spell.IceStorm
            or Spell.MassHealing or Spell.HealingCircle or Spell.TurnUndead or Spell.Vampirism or Spell.Revelation => 20000 + (int)spell * 10 + 1,
        _ => 20000 + (int)spell * 10 + 2
    };
    public static int ObjectSound(SpellEffect effect) => effect switch
    {
        SpellEffect.FatalSword or SpellEffect.MPEater or SpellEffect.FurbolgWarriorCritical => 20910,
        SpellEffect.Teleport or SpellEffect.StormEscape => 10110,
        SpellEffect.Healing => 20611,
        SpellEffect.Hemorrhage => 21040,
        SpellEffect.ElementalBarrierDown => 21315,
        SpellEffect.GreatFoxSpirit => (int)Monster.GreatFoxSpirit * 10 + 5,
        SpellEffect.AwakeningSuccess => 50002,
        SpellEffect.AwakeningFail => 50003,
        SpellEffect.AwakeningHit => 50001,
        SpellEffect.AwakeningMiss => 50000,
        SpellEffect.TurtleKing => 20351,
        _ => 0
    };
}
