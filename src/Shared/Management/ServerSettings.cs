#nullable enable
namespace Mir2.Management;

public sealed partial record ServerSettings
{
    public int MaxUser { get; set; } = 50;
    public int MaxIP { get; set; } = 5;
    public bool AllowNewAccount { get; set; } = true;
    public bool AllowChangePassword { get; set; } = true;
    public bool AllowLogin { get; set; } = true;
    public bool AllowNewCharacter { get; set; } = true;
    public bool AllowDeleteCharacter { get; set; } = true;
    public bool AllowCreateWarrior { get; set; } = true;
    public bool AllowCreateWizard { get; set; } = true;
    public bool AllowCreateTaoist { get; set; } = true;
    public bool AllowCreateAssassin { get; set; } = true;
    public bool AllowCreateArcher { get; set; } = true;
    public bool RequireStoragePassword { get; set; } = true;
    public float PublicShopPriceRate { get; set; } = 1.5f;
    public float ExpRate { get; set; } = 1f;
    public float DropRate { get; set; } = 1f;
    public int SaveDelay { get; set; } = 5;
    public int ItemTimeOut { get; set; } = 30;
    public int PlayerDiedItemTimeOut { get; set; } = 120;
    public int PKDelay { get; set; } = 12;
    public bool SafeZoneBorder { get; set; }
    public bool SafeZoneHealing { get; set; }
    public bool ExpMobLevelDifference { get; set; } = true;
    public bool AutoLearnSkills { get; set; }
    public Spell[] AutoLearnWarriorSkills { get; set; } = [];
    public Spell[] AutoLearnWizardSkills { get; set; } = [];
    public Spell[] AutoLearnTaoistSkills { get; set; } = [];
    public Spell[] AutoLearnAssassinSkills { get; set; } = [];
    public Spell[] AutoLearnArcherSkills { get; set; } = [];
    public bool MonsterRecallEnabled { get; set; } = true;
    public int MonsterRecallRange { get; set; } = 12;
    public int MonsterRecallCooldown { get; set; } = 5000;
}
public sealed record ServerSettingsDocument(ServerSettings Values, string Version, int Connections, int Players, bool RequiresRestart = false)
{
    public AutoLearnSkillOption[] SkillOptions { get; init; } = [];
}
public sealed record AutoLearnSkillOption(MirClass Class, Spell Spell, string Name, int Level);
public sealed record ServerSettingsEdit(ServerSettings Values, string Version);
