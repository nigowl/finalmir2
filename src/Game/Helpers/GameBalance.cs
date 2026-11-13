using Server.MirObjects;

namespace Server.Helpers;

public static class GameBalance
{
    public static int Scale(int value, float rate) => (int)Math.Clamp(Duration(value, rate), int.MinValue, int.MaxValue);
    public static long Duration(long value, float rate)
    {
        if (value <= 0 || rate == 1) return value;
        return (long)Math.Clamp(decimal.Round(value * (decimal)rate, 0, MidpointRounding.AwayFromZero), 1, long.MaxValue);
    }
    public static ushort Interval(ushort value, float rate) => rate == 1 || value == 0 ? value : (ushort)Math.Clamp(Math.Round(value / rate), 200, ushort.MaxValue);
    public static int HumanDamage(MapObject target, int damage, DefenceType defence)
    {
        var rate = defence is DefenceType.MAC or DefenceType.MACAgility or DefenceType.Repulsion ? Settings.PlayerMagicDamageRate : Settings.PlayerPhysicalDamageRate;
        damage = Scale(damage, rate);
        return target is HumanObject ? Scale(damage, Settings.PlayerPvpDamageRate) : damage;
    }
    public static int MonsterDamage(MonsterObject attacker, int damage) => Scale(damage, attacker.Master is HumanObject ? Settings.PetDamageRate : Settings.MonsterDamageRate);

    public static Poison Poison(Poison source)
    {
        var damagePoison = source.PType is PoisonType.Green or PoisonType.Red or PoisonType.Bleeding;
        var control = source.PType is PoisonType.Paralysis or PoisonType.LRParalysis or PoisonType.Frozen or PoisonType.Slow
            or PoisonType.Stun or PoisonType.Dazed or PoisonType.Blindness;
        var durationRate = damagePoison ? Settings.PoisonDurationRate : control ? Settings.ControlDurationRate : 1;
        var damageRate = source.PType is PoisonType.Green or PoisonType.Bleeding ? Settings.PoisonDamageRate : 1;
        if (durationRate == 1 && damageRate == 1) return source;
        // 同一毒对象可能被多个目标复用，不把倍率写回调用方后重复放大。
        return new Poison { Owner = source.Owner, PType = source.PType, Value = Scale(source.Value, damageRate),
            Duration = Duration(source.Duration, durationRate), Time = source.Time, TickTime = source.TickTime, TickSpeed = source.TickSpeed };
    }

    public static int BuffDuration(BuffType type, int duration, Stats stats)
    {
        if (type <= BuffType.None || type > BuffType.ElementalBarrier) return duration;
        // 盾受击时传入的是已缩短的剩余时间，不能再次乘初始持续时间倍率。
        if (type is BuffType.MagicShield or BuffType.ElementalBarrier && stats == null) return duration;
        return Scale(duration, Settings.SkillBuffDurationRate);
    }

    public static void MonsterStats(Stats stats)
    {
        stats[Stat.HP] = Scale(stats[Stat.HP], Settings.MonsterHpRate);
        foreach (var type in new[] { Stat.MinAC, Stat.MaxAC, Stat.MinMAC, Stat.MaxMAC }) stats[type] = Scale(stats[type], Settings.MonsterDefenceRate);
    }

    public static BaseStats ClientStats(BaseStats source)
    {
        if (Settings.PlayerAttackSpeedBonus == 0) return source;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) source.Save(writer);
        stream.Position = 0; var result = new BaseStats(new BinaryReader(stream));
        var speed = result.Stats.FirstOrDefault(stat => stat.Type == Stat.AttackSpeed);
        if (speed == null) { speed = new BaseStat(Stat.AttackSpeed); result.Stats.Add(speed); }
        speed.Base += Settings.PlayerAttackSpeedBonus;
        return result;
    }
}
