using System.IO;

namespace Server;

public static partial class Settings
{
    public static float PlayerPhysicalDamageRate = 1f;
    public static float PlayerMagicDamageRate = 1f;
    public static float PlayerPvpDamageRate = 1f;
    public static float MonsterDamageRate = 1f;
    public static float PetDamageRate = 1f;
    public static float PoisonDamageRate = 1f;
    public static float MonsterHpRate = 1f;
    public static float MonsterDefenceRate = 1f;
    public static float MonsterAttackSpeedRate = 1f;
    public static float MonsterMoveSpeedRate = 1f;
    public static float SkillCooldownRate = 1f;
    public static float PoisonDurationRate = 1f;
    public static float ControlDurationRate = 1f;
    public static float SkillBuffDurationRate = 1f;
    public static int PlayerAttackSpeedBonus = 0;

    private static void LoadBalance()
    {
        PlayerPhysicalDamageRate = BalanceRate("PlayerPhysicalDamageRate", PlayerPhysicalDamageRate, 0.01f, 100f);
        PlayerMagicDamageRate = BalanceRate("PlayerMagicDamageRate", PlayerMagicDamageRate, 0.01f, 100f);
        PlayerPvpDamageRate = BalanceRate("PlayerPvpDamageRate", PlayerPvpDamageRate, 0.01f, 100f);
        MonsterDamageRate = BalanceRate("MonsterDamageRate", MonsterDamageRate, 0.01f, 100f);
        PetDamageRate = BalanceRate("PetDamageRate", PetDamageRate, 0.01f, 100f);
        PoisonDamageRate = BalanceRate("PoisonDamageRate", PoisonDamageRate, 0.01f, 100f);
        MonsterHpRate = BalanceRate("MonsterHpRate", MonsterHpRate, 0.01f, 100f);
        MonsterDefenceRate = BalanceRate("MonsterDefenceRate", MonsterDefenceRate, 0.01f, 100f);
        MonsterAttackSpeedRate = BalanceRate("MonsterAttackSpeedRate", MonsterAttackSpeedRate, 0.1f, 5f);
        MonsterMoveSpeedRate = BalanceRate("MonsterMoveSpeedRate", MonsterMoveSpeedRate, 0.1f, 5f);
        SkillCooldownRate = BalanceRate("SkillCooldownRate", SkillCooldownRate, 0.1f, 10f);
        PoisonDurationRate = BalanceRate("PoisonDurationRate", PoisonDurationRate, 0.1f, 10f);
        ControlDurationRate = BalanceRate("ControlDurationRate", ControlDurationRate, 0.1f, 10f);
        SkillBuffDurationRate = BalanceRate("SkillBuffDurationRate", SkillBuffDurationRate, 0.1f, 10f);
        PlayerAttackSpeedBonus = Reader.ReadInt32("Balance", "PlayerAttackSpeedBonus", PlayerAttackSpeedBonus);
        if (PlayerAttackSpeedBonus is < -20 or > 30) throw new InvalidDataException("玩家基础攻速修正须为-20–30。");
        DropRange = Math.Clamp(Reader.ReadInt32("Game", "DropRange", DropRange), 1, 20);
        DropStackSize = Math.Clamp(Reader.ReadInt32("Game", "DropStackSize", DropStackSize), 1, 100);
    }

    private static float BalanceRate(string key, float fallback, float minimum, float maximum)
    {
        var value = Reader.ReadSingle("Balance", key, fallback);
        if (!float.IsFinite(value) || value < minimum || value > maximum) throw new InvalidDataException($"Balance/{key}须为{minimum}–{maximum}。");
        return value;
    }

    private static void SaveBalance()
    {
        Reader.Write("Balance", "PlayerPhysicalDamageRate", PlayerPhysicalDamageRate);
        Reader.Write("Balance", "PlayerMagicDamageRate", PlayerMagicDamageRate);
        Reader.Write("Balance", "PlayerPvpDamageRate", PlayerPvpDamageRate);
        Reader.Write("Balance", "MonsterDamageRate", MonsterDamageRate);
        Reader.Write("Balance", "PetDamageRate", PetDamageRate);
        Reader.Write("Balance", "PoisonDamageRate", PoisonDamageRate);
        Reader.Write("Balance", "MonsterHpRate", MonsterHpRate);
        Reader.Write("Balance", "MonsterDefenceRate", MonsterDefenceRate);
        Reader.Write("Balance", "MonsterAttackSpeedRate", MonsterAttackSpeedRate);
        Reader.Write("Balance", "MonsterMoveSpeedRate", MonsterMoveSpeedRate);
        Reader.Write("Balance", "SkillCooldownRate", SkillCooldownRate);
        Reader.Write("Balance", "PoisonDurationRate", PoisonDurationRate);
        Reader.Write("Balance", "ControlDurationRate", ControlDurationRate);
        Reader.Write("Balance", "SkillBuffDurationRate", SkillBuffDurationRate);
        Reader.Write("Balance", "PlayerAttackSpeedBonus", PlayerAttackSpeedBonus);
        Reader.Write("Game", "DropRange", DropRange); Reader.Write("Game", "DropStackSize", DropStackSize);
    }
}

