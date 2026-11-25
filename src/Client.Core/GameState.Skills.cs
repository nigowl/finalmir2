namespace Crystal.Native;

public sealed partial class GameState
{
    // 本次会话中手动关闭的原技能开关，防止职业辅助立即重新开启。
    public readonly HashSet<Spell> DisabledSkills = new();
    public decimal WearWeight => EquipmentWeight(false);
    public decimal HandWeight => EquipmentWeight(true);

    private decimal EquipmentWeight(bool hand)
    {
        if (User == null) return 0;
        return Math.Min(int.MaxValue, User.Equipment.Where(item => item?.Info != null)
            .SelectMany(item => new[] { item }.Concat(item.Slots ?? []))
            .Where(item => item?.Info != null && (item.Info.Type is ItemType.Weapon or ItemType.Torch) == hand)
            .Sum(item => item.Weight));
    }
}

public static partial class ClientSkillRules
{
    public static bool NeedsClearShot(Spell spell) => spell is Spell.FireBall or Spell.GreatFireBall or Spell.FrostCrunch or Spell.SoulFireBall or Spell.CatTongue or
        Spell.ElementalShot or Spell.StraightShot or Spell.DoubleShot or Spell.DelayedExplosion or Spell.BindingShot or Spell.VampireShot or Spell.PoisonShot or
        Spell.CrippleShot or Spell.NapalmShot or Spell.FireBounce or Spell.MeteorShower;

    public static bool TargetsGround(Spell spell) => spell is Spell.Teleport or Spell.Blink or Spell.FireBang or Spell.IceStorm or
        Spell.MassHiding or Spell.SoulShield or Spell.BlessedArmour or Spell.FireWall or Spell.MassHealing or Spell.StormEscape or
        Spell.PoisonCloud or Spell.Blizzard or Spell.MeteorStrike or Spell.TrapHexagon or Spell.Curse or Spell.Plague or Spell.SummonVampire or
        Spell.SummonToad or Spell.SummonSnakes or Spell.Stonetrap or Spell.HealingCircle or Spell.Portal;

    public static bool RequiresTarget(Spell spell) => spell is Spell.ElectricShock or Spell.Poisoning or Spell.ThunderBolt or Spell.FlameDisruptor or
        Spell.TurnUndead or Spell.Vampirism or Spell.Revelation or Spell.Entrapment or Spell.PetEnhancer or Spell.Reincarnation or
        Spell.Hallucination or Spell.Trap or Spell.CatTongue or Spell.BindingShot or Spell.VampireShot or Spell.PoisonShot or Spell.CrippleShot or
        Spell.NapalmShot or Spell.MeteorShower or Spell.FireBounce or Spell.StraightShot or Spell.DoubleShot or Spell.DelayedExplosion or
        Spell.ElementalShot or Spell.DarkBody;

    public static bool CastBlocked(PoisonType poison) => (poison & (PoisonType.Stun | PoisonType.Dazed | PoisonType.Paralysis | PoisonType.Frozen)) != 0;

    public static (uint Target, System.Drawing.Point Location)? Aim(Spell spell, Actor caster, Actor? selected, System.Drawing.Point mouse, Actor? hovered = null)
    {
        if (hovered is { Hidden: false, Kind: ObjectType.Player or ObjectType.Monster } && hovered.Dead == (spell == Spell.Reincarnation)) selected = hovered;
        var directional = spell is Spell.FireBall or Spell.GreatFireBall or Spell.FrostCrunch or Spell.SoulFireBall or
            Spell.HellFire or Spell.Lightning or Spell.IceThrust or Spell.ShoulderDash or Spell.FlashDash or Spell.BackStep or
            Spell.ExplosiveTrap or Spell.BladeAvalanche or Spell.CrescentSlash or Spell.SlashingBurst or Spell.HeavenlySword or Spell.PoisonSword;
        if ((TargetsGround(spell) || RequiresTarget(spell) || directional || spell is Spell.Healing or Spell.Purification or Spell.EnergyShield or Spell.UltimateEnhancer) &&
            selected is { Kind: ObjectType.Player or ObjectType.Monster } && (!selected.Hidden || selected.Id == caster.Id) &&
            selected.Dead == (spell == Spell.Reincarnation) &&
            (spell != Spell.Healing || selected.Kind == ObjectType.Player))
            return (selected.Id, selected.Location);
        if (RequiresTarget(spell)) return null;
        if (spell is Spell.Healing or Spell.Purification or Spell.EnergyShield or Spell.UltimateEnhancer) return (caster.Id, caster.Location);
        if (TargetsGround(spell) || directional)
            return (0, mouse);
        return (0, caster.Location);
    }

    public static bool IsImplemented(Spell spell) => Enum.IsDefined(spell) && (byte)spell is > 0 and < 200 && spell != Spell.FastMove;
    public static MirAction Animation(Spell spell) => spell switch
    {
        Spell.BladeAvalanche or Spell.CrescentSlash => MirAction.Attack3,
        Spell.SlashingBurst or Spell.CounterAttack or Spell.PoisonSword => MirAction.Attack1,
        Spell.HeavenlySword => MirAction.Attack2,
        Spell.ShoulderDash or Spell.FlashDash => MirAction.Running,
        Spell.BackStep => MirAction.Jump,
        Spell.ExplosiveTrap => MirAction.Harvest,
        Spell.StraightShot or Spell.DoubleShot or Spell.DelayedExplosion or Spell.ElementalShot or Spell.BindingShot
            or Spell.VampireShot or Spell.PoisonShot or Spell.CrippleShot or Spell.NapalmShot or Spell.SummonVampire
            or Spell.SummonToad or Spell.SummonSnakes or Spell.Stonetrap => MirAction.AttackRange2,
        _ => MirAction.Spell
    };
    public static bool IsPassive(Spell spell) => spell is Spell.Fencing or Spell.SpiritSword or Spell.Slaying or Spell.FatalSword
        or Spell.MPEater or Spell.Hemorrhage or Spell.Focus or Spell.Meditation;
    public static bool IsToggle(Spell spell) => spell is Spell.Thrusting or Spell.HalfMoon or Spell.CrossHalfMoon or Spell.DoubleSlash;
    public static bool IsTrigger(Spell spell) => spell is Spell.TwinDrakeBlade or Spell.FlamingSword or Spell.CounterAttack;
    public static string ExperienceText(ClientMagic magic) => magic.Level >= 3 ? $"{magic.Level}级 · 已满级（熟练度 {magic.Experience}）" :
        $"{magic.Level}级 · 熟练度 {magic.Experience}/{(magic.Level == 0 ? magic.Need1 : magic.Level == 1 ? magic.Need2 : magic.Need3)}";
}
