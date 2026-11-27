#nullable enable
namespace Mir2.Management;

public sealed record AdminCommand(string Confirmation, string Version = "");
public sealed record ManagedItem(string Grid, int Slot, string UniqueId, int ItemId, string Name, int Count, int Durability, int MaxDurability);
public sealed record AccountRow(int Id, string AccountID, bool Online, bool IsGM, bool Banned, int Characters, uint Gold, uint Credit);
public sealed record AccountDetails(int Id, string AccountID, bool Online, bool IsGM, bool Banned, string BanReason, DateTime? BanUntil,
    bool RequirePasswordChange, uint Gold, uint Credit, DateTime Created, DateTime LastLogin, string LastIP, CharacterRow[] Characters, ManagedItem[] Storage, string Version);
public sealed record AccountCreate(string AccountID, string Password);
public sealed record AccountEdit(int Id, uint Gold, uint Credit, bool IsGM, bool Banned, string BanReason, DateTime? BanUntil, bool RequirePasswordChange, string Version, string Confirmation);
public sealed record PasswordReset(string Password, bool RequireChange, string Version, string Confirmation);
public sealed record CharacterRow(int Id, string Name, int AccountId, string AccountName, string Class, int Level, bool Online, bool Deleted, bool Banned, int MapId, int X, int Y);
public sealed record CharacterDetails(int Id, string Name, int AccountId, string AccountName, string Class, string Gender, int Level, long Experience, int PkPoints, int Hp, int Mp,
    bool Online, bool Deleted, bool Banned, string BanReason, DateTime? BanUntil, bool Muted, DateTime? MuteUntil, int MapId, int X, int Y, int BindMapId, int BindX, int BindY,
    int GuildId, string GuildName, ManagedItem[] Inventory, string[] Spells, int[] ActiveQuests, int[] CompletedQuests, string Version)
{
    public uint Gold { get; init; }
    public string[] ActiveQuestNames { get; init; } = [];
    public string[] CompletedQuestNames { get; init; } = [];
}
public sealed record CharacterCreate(int AccountId, string Name, int Class, int Gender);
public sealed record CharacterEdit(int Id, int Level, long Experience, int PkPoints, bool Banned, string BanReason, DateTime? BanUntil, bool Muted, DateTime? MuteUntil, string Version, string Confirmation)
{
    public uint? Gold { get; init; }
}
public sealed record CharacterTravel(int MapId, int X, int Y, bool SetBind, string Confirmation);
public sealed record ItemGrant(int ItemId, int Count, string Version, string Confirmation);
public sealed record GuildRow(int Id, string Name, int Level, uint Gold, int Members, int Online, string Leader);
public sealed record GuildMemberRow(int CharacterId, string Name, int Rank, string RankName, bool Online);
public sealed record GuildRankRow(int Id, string Name, int Permissions);
public sealed record GuildDetails(int Id, string Name, int Level, long Experience, uint Gold, int SparePoints, string Notice, int Capacity, GuildMemberRow[] Members, GuildRankRow[] Ranks, ManagedItem[] Storage, string Version);
public sealed record GuildCreate(string Name, int LeaderId);
public sealed record GuildEdit(int Id, string Name, int Level, long Experience, uint Gold, int SparePoints, string Notice, string Version, string Confirmation);
public sealed record GuildMemberEdit(int CharacterId, string Operation, string Version, string Confirmation, int RankId = 1);
public sealed record GuildRankEdit(int RankId, string Name, int Permissions, string Version, string Confirmation);
public sealed record SpawnDefinition(int Id, int MapId, string MapName, int MonsterId, string MonsterName, int X, int Y, int Count, int Spread,
    int DelayMinutes, int RandomDelayMinutes, int Direction, string Route, bool SaveTime, int Ticks, int Alive, long SecondsUntilSpawn, string Version);
public sealed record SpawnMutation(SpawnDefinition Value, string Confirmation);
public sealed record NpcMutation(NpcDefinition Value, string Confirmation);
public sealed record MapPoint(int X, int Y);
public sealed record MapOverview(int Id, string Title, string FileName, int Width, int Height, string ImageVersion, string ImageSource, string Walkable,
    SpawnDefinition[] Spawns, NpcDefinition[] Npcs, string SpawnVersion);
public sealed record SpawnBatch(int MapId, SpawnDefinition[] Values, string Version, string Confirmation);
public sealed record MapChoice(int Id, string Code, string Name, int Width, int Height, int Monsters, long PlannedMonsters, int Spawns, int Npcs);
