#nullable enable
namespace Mir2.Management;

public sealed partial record ServerSettings
{
    public float PlayerPhysicalDamageRate { get; set; } = 1f;
    public float PlayerMagicDamageRate { get; set; } = 1f;
    public float PlayerPvpDamageRate { get; set; } = 1f;
    public float MonsterDamageRate { get; set; } = 1f;
    public float PetDamageRate { get; set; } = 1f;
    public float PoisonDamageRate { get; set; } = 1f;
    public float MonsterHpRate { get; set; } = 1f;
    public float MonsterDefenceRate { get; set; } = 1f;
    public float MonsterAttackSpeedRate { get; set; } = 1f;
    public float MonsterMoveSpeedRate { get; set; } = 1f;
    public float SkillCooldownRate { get; set; } = 1f;
    public float PoisonDurationRate { get; set; } = 1f;
    public float ControlDurationRate { get; set; } = 1f;
    public float SkillBuffDurationRate { get; set; } = 1f;
    public int PlayerMoveMilliseconds { get; set; } = PlayerActionTiming.DefaultMoveMilliseconds;
    public int PlayerActionSpeedPercent { get; set; } = PlayerActionTiming.DefaultActionSpeedPercent;
    public int PlayerAttackSpeedBonus { get; set; } = 0;
    public int MagicResistWeight { get; set; } = 10;
    public int PoisonResistWeight { get; set; } = 10;
    public int CriticalRateWeight { get; set; } = 5;
    public int CriticalDamageWeight { get; set; } = 50;
    public int FreezingAttackWeight { get; set; } = 10;
    public int PoisonAttackWeight { get; set; } = 10;
    public int HealthRegenWeight { get; set; } = 10;
    public int ManaRegenWeight { get; set; } = 10;
    public int MaxLuck { get; set; } = 10;
    public bool PvpCanResistMagic { get; set; }
    public bool PvpCanResistPoison { get; set; }
    public bool PvpCanFreeze { get; set; }
    public bool DropGold { get; set; } = true;
    public uint MaxDropGold { get; set; } = 2000;
    public int DropRange { get; set; } = 4;
    public int DropStackSize { get; set; } = 5;
    public int MaxBossTames { get; set; } = 1;
    public bool PetSave { get; set; }
}

