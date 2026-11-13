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
    public int PlayerAttackSpeedBonus { get; set; } = 0;
    public int MagicResistWeight { get; set; }
    public int PoisonResistWeight { get; set; }
    public int CriticalRateWeight { get; set; }
    public int CriticalDamageWeight { get; set; }
    public int FreezingAttackWeight { get; set; }
    public int PoisonAttackWeight { get; set; }
    public int HealthRegenWeight { get; set; }
    public int ManaRegenWeight { get; set; }
    public int MaxLuck { get; set; }
    public bool PvpCanResistMagic { get; set; }
    public bool PvpCanResistPoison { get; set; }
    public bool PvpCanFreeze { get; set; }
    public bool DropGold { get; set; }
    public uint MaxDropGold { get; set; }
    public int DropRange { get; set; }
    public int DropStackSize { get; set; }
    public int MaxBossTames { get; set; }
    public bool PetSave { get; set; }
}

