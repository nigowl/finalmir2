using System.Globalization;
using Mir2.Management;

namespace Server.ConsoleHost;

public sealed partial class SystemSettings
{
    private static ServerSettings WithBalance(ServerSettings s)
    {
        s.PlayerPhysicalDamageRate = Settings.PlayerPhysicalDamageRate;
        s.PlayerMagicDamageRate = Settings.PlayerMagicDamageRate;
        s.PlayerPvpDamageRate = Settings.PlayerPvpDamageRate;
        s.MonsterDamageRate = Settings.MonsterDamageRate;
        s.PetDamageRate = Settings.PetDamageRate;
        s.PoisonDamageRate = Settings.PoisonDamageRate;
        s.MonsterHpRate = Settings.MonsterHpRate;
        s.MonsterDefenceRate = Settings.MonsterDefenceRate;
        s.MonsterAttackSpeedRate = Settings.MonsterAttackSpeedRate;
        s.MonsterMoveSpeedRate = Settings.MonsterMoveSpeedRate;
        s.SkillCooldownRate = Settings.SkillCooldownRate;
        s.PoisonDurationRate = Settings.PoisonDurationRate;
        s.ControlDurationRate = Settings.ControlDurationRate;
        s.SkillBuffDurationRate = Settings.SkillBuffDurationRate;
        s.PlayerAttackSpeedBonus = Settings.PlayerAttackSpeedBonus;
        s.MagicResistWeight = Settings.MagicResistWeight;
        s.PoisonResistWeight = Settings.PoisonResistWeight;
        s.CriticalRateWeight = Settings.CriticalRateWeight;
        s.CriticalDamageWeight = Settings.CriticalDamageWeight;
        s.FreezingAttackWeight = Settings.FreezingAttackWeight;
        s.PoisonAttackWeight = Settings.PoisonAttackWeight;
        s.HealthRegenWeight = Settings.HealthRegenWeight;
        s.ManaRegenWeight = Settings.ManaRegenWeight;
        s.MaxLuck = Settings.MaxLuck;
        s.PvpCanResistMagic = Settings.PvpCanResistMagic;
        s.PvpCanResistPoison = Settings.PvpCanResistPoison;
        s.PvpCanFreeze = Settings.PvpCanFreeze;
        s.DropGold = Settings.DropGold;
        s.MaxDropGold = Settings.MaxDropGold;
        s.DropRange = Settings.DropRange;
        s.DropStackSize = Settings.DropStackSize;
        s.MaxBossTames = Settings.MaxBossTames;
        s.PetSave = Settings.PetSave;
        return s;
    }

    private static void ApplyBalance(ServerSettings s)
    {
        Settings.PlayerPhysicalDamageRate = s.PlayerPhysicalDamageRate;
        Settings.PlayerMagicDamageRate = s.PlayerMagicDamageRate;
        Settings.PlayerPvpDamageRate = s.PlayerPvpDamageRate;
        Settings.MonsterDamageRate = s.MonsterDamageRate;
        Settings.PetDamageRate = s.PetDamageRate;
        Settings.PoisonDamageRate = s.PoisonDamageRate;
        Settings.MonsterHpRate = s.MonsterHpRate;
        Settings.MonsterDefenceRate = s.MonsterDefenceRate;
        Settings.MonsterAttackSpeedRate = s.MonsterAttackSpeedRate;
        Settings.MonsterMoveSpeedRate = s.MonsterMoveSpeedRate;
        Settings.SkillCooldownRate = s.SkillCooldownRate;
        Settings.PoisonDurationRate = s.PoisonDurationRate;
        Settings.ControlDurationRate = s.ControlDurationRate;
        Settings.SkillBuffDurationRate = s.SkillBuffDurationRate;
        Settings.PlayerAttackSpeedBonus = s.PlayerAttackSpeedBonus;
        Settings.MagicResistWeight = (byte)s.MagicResistWeight;
        Settings.PoisonResistWeight = (byte)s.PoisonResistWeight;
        Settings.CriticalRateWeight = (byte)s.CriticalRateWeight;
        Settings.CriticalDamageWeight = (byte)s.CriticalDamageWeight;
        Settings.FreezingAttackWeight = (byte)s.FreezingAttackWeight;
        Settings.PoisonAttackWeight = (byte)s.PoisonAttackWeight;
        Settings.HealthRegenWeight = (byte)s.HealthRegenWeight;
        Settings.ManaRegenWeight = (byte)s.ManaRegenWeight;
        Settings.MaxLuck = (byte)s.MaxLuck;
        Settings.PvpCanResistMagic = s.PvpCanResistMagic;
        Settings.PvpCanResistPoison = s.PvpCanResistPoison;
        Settings.PvpCanFreeze = s.PvpCanFreeze;
        Settings.DropGold = s.DropGold;
        Settings.MaxDropGold = s.MaxDropGold;
        Settings.DropRange = s.DropRange;
        Settings.DropStackSize = s.DropStackSize;
        Settings.MaxBossTames = s.MaxBossTames;
        Settings.PetSave = s.PetSave;
    }

    private static void ValidateBalance(ServerSettings s)
    {
        if (!float.IsFinite(s.PlayerPhysicalDamageRate) || s.PlayerPhysicalDamageRate < 0.01f || s.PlayerPhysicalDamageRate > 100f) throw new InvalidDataException("玩家物理伤害倍率须为0.01–100。");
        if (!float.IsFinite(s.PlayerMagicDamageRate) || s.PlayerMagicDamageRate < 0.01f || s.PlayerMagicDamageRate > 100f) throw new InvalidDataException("玩家法术伤害倍率须为0.01–100。");
        if (!float.IsFinite(s.PlayerPvpDamageRate) || s.PlayerPvpDamageRate < 0.01f || s.PlayerPvpDamageRate > 100f) throw new InvalidDataException("玩家对战额外伤害倍率须为0.01–100。");
        if (!float.IsFinite(s.MonsterDamageRate) || s.MonsterDamageRate < 0.01f || s.MonsterDamageRate > 100f) throw new InvalidDataException("怪物伤害倍率须为0.01–100。");
        if (!float.IsFinite(s.PetDamageRate) || s.PetDamageRate < 0.01f || s.PetDamageRate > 100f) throw new InvalidDataException("战宠伤害倍率须为0.01–100。");
        if (!float.IsFinite(s.PoisonDamageRate) || s.PoisonDamageRate < 0.01f || s.PoisonDamageRate > 100f) throw new InvalidDataException("中毒持续伤害倍率须为0.01–100。");
        if (!float.IsFinite(s.MonsterHpRate) || s.MonsterHpRate < 0.01f || s.MonsterHpRate > 100f) throw new InvalidDataException("怪物生命倍率须为0.01–100。");
        if (!float.IsFinite(s.MonsterDefenceRate) || s.MonsterDefenceRate < 0.01f || s.MonsterDefenceRate > 100f) throw new InvalidDataException("怪物防御倍率须为0.01–100。");
        if (!float.IsFinite(s.MonsterAttackSpeedRate) || s.MonsterAttackSpeedRate < 0.1f || s.MonsterAttackSpeedRate > 5f) throw new InvalidDataException("怪物攻击速度倍率须为0.1–5。");
        if (!float.IsFinite(s.MonsterMoveSpeedRate) || s.MonsterMoveSpeedRate < 0.1f || s.MonsterMoveSpeedRate > 5f) throw new InvalidDataException("怪物移动速度倍率须为0.1–5。");
        if (!float.IsFinite(s.SkillCooldownRate) || s.SkillCooldownRate < 0.1f || s.SkillCooldownRate > 10f) throw new InvalidDataException("技能冷却时间倍率须为0.1–10。");
        if (!float.IsFinite(s.PoisonDurationRate) || s.PoisonDurationRate < 0.1f || s.PoisonDurationRate > 10f) throw new InvalidDataException("中毒持续时间倍率须为0.1–10。");
        if (!float.IsFinite(s.ControlDurationRate) || s.ControlDurationRate < 0.1f || s.ControlDurationRate > 10f) throw new InvalidDataException("控制状态时间倍率须为0.1–10。");
        if (!float.IsFinite(s.SkillBuffDurationRate) || s.SkillBuffDurationRate < 0.1f || s.SkillBuffDurationRate > 10f) throw new InvalidDataException("技能加成时间倍率须为0.1–10。");
        if (s.PlayerAttackSpeedBonus < -20 || s.PlayerAttackSpeedBonus > 30) throw new InvalidDataException("玩家基础攻速修正须为-20–30。");
        if (s.MagicResistWeight < 1 || s.MagicResistWeight > 255) throw new InvalidDataException("魔法抗性判定基数须为1–255。");
        if (s.PoisonResistWeight < 1 || s.PoisonResistWeight > 255) throw new InvalidDataException("毒抗判定基数须为1–255。");
        if (s.CriticalRateWeight < 1 || s.CriticalRateWeight > 255) throw new InvalidDataException("暴击率系数须为1–255。");
        if (s.CriticalDamageWeight < 1 || s.CriticalDamageWeight > 255) throw new InvalidDataException("暴击伤害除数须为1–255。");
        if (s.FreezingAttackWeight < 1 || s.FreezingAttackWeight > 255) throw new InvalidDataException("冰冻判定基数须为1–255。");
        if (s.PoisonAttackWeight < 1 || s.PoisonAttackWeight > 255) throw new InvalidDataException("毒素判定基数须为1–255。");
        if (s.HealthRegenWeight < 1 || s.HealthRegenWeight > 255) throw new InvalidDataException("生命恢复属性除数须为1–255。");
        if (s.ManaRegenWeight < 1 || s.ManaRegenWeight > 255) throw new InvalidDataException("魔力恢复属性除数须为1–255。");
        if (s.MaxLuck < 1 || s.MaxLuck > 255) throw new InvalidDataException("幸运判定基数须为1–255。");
        if (s.MaxDropGold < 1u || s.MaxDropGold > 100000000u) throw new InvalidDataException("单次金币掉落上限须为1–100000000。");
        if (s.DropRange < 1 || s.DropRange > 20) throw new InvalidDataException("掉落搜索范围须为1–20。");
        if (s.DropStackSize < 1 || s.DropStackSize > 100) throw new InvalidDataException("同格掉落堆数须为1–100。");
        if (s.MaxBossTames < 0 || s.MaxBossTames > 10) throw new InvalidDataException("可驯服首领上限须为0–10。");
    }

    private static Dictionary<string, Dictionary<string, string>> WithBalanceEntries(Dictionary<string, Dictionary<string, string>> result, ServerSettings s)
    {
        if (!result.ContainsKey("Balance")) result["Balance"] = new();
        if (!result.ContainsKey("Items")) result["Items"] = new();
        if (!result.ContainsKey("DropGold")) result["DropGold"] = new();
        if (!result.ContainsKey("Game")) result["Game"] = new();
        result["Balance"]["PlayerPhysicalDamageRate"] = s.PlayerPhysicalDamageRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["PlayerMagicDamageRate"] = s.PlayerMagicDamageRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["PlayerPvpDamageRate"] = s.PlayerPvpDamageRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["MonsterDamageRate"] = s.MonsterDamageRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["PetDamageRate"] = s.PetDamageRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["PoisonDamageRate"] = s.PoisonDamageRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["MonsterHpRate"] = s.MonsterHpRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["MonsterDefenceRate"] = s.MonsterDefenceRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["MonsterAttackSpeedRate"] = s.MonsterAttackSpeedRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["MonsterMoveSpeedRate"] = s.MonsterMoveSpeedRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["SkillCooldownRate"] = s.SkillCooldownRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["PoisonDurationRate"] = s.PoisonDurationRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["ControlDurationRate"] = s.ControlDurationRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["SkillBuffDurationRate"] = s.SkillBuffDurationRate.ToString(CultureInfo.CurrentCulture);
        result["Balance"]["PlayerAttackSpeedBonus"] = s.PlayerAttackSpeedBonus.ToString();
        result["Items"]["MagicResistWeight"] = s.MagicResistWeight.ToString();
        result["Items"]["PoisonResistWeight"] = s.PoisonResistWeight.ToString();
        result["Items"]["CriticalRateWeight"] = s.CriticalRateWeight.ToString();
        result["Items"]["CriticalDamageWeight"] = s.CriticalDamageWeight.ToString();
        result["Items"]["FreezingAttackWeight"] = s.FreezingAttackWeight.ToString();
        result["Items"]["PoisonAttackWeight"] = s.PoisonAttackWeight.ToString();
        result["Items"]["HealthRegenWeight"] = s.HealthRegenWeight.ToString();
        result["Items"]["ManaRegenWeight"] = s.ManaRegenWeight.ToString();
        result["Items"]["MaxLuck"] = s.MaxLuck.ToString();
        result["Items"]["PvpCanResistMagic"] = s.PvpCanResistMagic.ToString();
        result["Items"]["PvpCanResistPoison"] = s.PvpCanResistPoison.ToString();
        result["Items"]["PvpCanFreeze"] = s.PvpCanFreeze.ToString();
        result["DropGold"]["DropGold"] = s.DropGold.ToString();
        result["DropGold"]["MaxDropGold"] = s.MaxDropGold.ToString();
        result["Game"]["DropRange"] = s.DropRange.ToString();
        result["Game"]["DropStackSize"] = s.DropStackSize.ToString();
        result["Game"]["MaxBossTames"] = s.MaxBossTames.ToString();
        result["Game"]["PetSave"] = s.PetSave.ToString();
        return result;
    }
}

