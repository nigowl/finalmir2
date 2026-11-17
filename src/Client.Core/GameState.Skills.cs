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
    public static int GlobalDelay(Spell spell) => spell switch
    {
        Spell.FlashDash => 250, Spell.CounterAttack => 100, Spell.DoubleShot => 500,
        Spell.ShoulderDash or Spell.BackStep or Spell.FlameField => 2500,
        Spell.HeavenlySword => 1200,
        Spell.StraightShot or Spell.DelayedExplosion or Spell.ExplosiveTrap or Spell.ElementalShot
            or Spell.BladeAvalanche or Spell.SlashingBurst or Spell.PoisonSword or Spell.CrescentSlash => 1500,
        Spell.BindingShot or Spell.VampireShot or Spell.PoisonShot or Spell.CrippleShot or Spell.NapalmShot
            or Spell.SummonVampire or Spell.SummonToad or Spell.SummonSnakes or Spell.Stonetrap => 1000,
        _ => 1800
    };
    public static bool IsPassive(Spell spell) => spell is Spell.Fencing or Spell.SpiritSword or Spell.Slaying or Spell.FatalSword
        or Spell.MPEater or Spell.Hemorrhage or Spell.Focus or Spell.Meditation;
    public static bool IsToggle(Spell spell) => spell is Spell.Thrusting or Spell.HalfMoon or Spell.CrossHalfMoon or Spell.DoubleSlash;
    public static bool IsTrigger(Spell spell) => spell is Spell.TwinDrakeBlade or Spell.FlamingSword or Spell.CounterAttack;
    public static string ExperienceText(ClientMagic magic) => magic.Level >= 3 ? $"{magic.Level}级 · 已满级（熟练度 {magic.Experience}）" :
        $"{magic.Level}级 · 熟练度 {magic.Experience}/{(magic.Level == 0 ? magic.Need1 : magic.Level == 1 ? magic.Need2 : magic.Need3)}";
}
