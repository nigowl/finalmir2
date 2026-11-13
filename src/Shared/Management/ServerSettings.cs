#nullable enable
namespace Mir2.Management;

public sealed partial record ServerSettings
{
    public int MaxUser { get; set; }
    public int MaxIP { get; set; }
    public bool AllowNewAccount { get; set; }
    public bool AllowChangePassword { get; set; }
    public bool AllowLogin { get; set; }
    public bool AllowNewCharacter { get; set; }
    public bool AllowDeleteCharacter { get; set; }
    public bool RequireStoragePassword { get; set; }
    public float ExpRate { get; set; }
    public float DropRate { get; set; }
    public int SaveDelay { get; set; }
    public int ItemTimeOut { get; set; }
    public int PlayerDiedItemTimeOut { get; set; }
    public int PKDelay { get; set; }
    public bool SafeZoneBorder { get; set; }
    public bool SafeZoneHealing { get; set; }
    public bool ExpMobLevelDifference { get; set; }
    public bool MonsterRecallEnabled { get; set; }
    public int MonsterRecallRange { get; set; }
    public int MonsterRecallCooldown { get; set; }
}
public sealed record ServerSettingsDocument(ServerSettings Values, string Version, int Connections, int Players, bool RequiresRestart = false);
public sealed record ServerSettingsEdit(ServerSettings Values, string Version);
