#nullable enable
namespace Mir2.Management;

public sealed record PlayerStatus(string Name, string Class, int Level, string Map, int X, int Y, string GroupLeader = "", int GroupSize = 0, string TradePartner = "", bool TradeLocked = false, string GuildName = "", string GuildRank = "");
public sealed record WorldCounts(int Maps, int Items, int Monsters, int Npcs, int Quests, int Accounts);
public sealed record ServerStatus(bool Running, bool Ready, int ProcessId, double UptimeSeconds, double MemoryMiB, double CpuSeconds,
    long LoopMilliseconds, int Objects, WorldCounts Counts, PlayerStatus[] Players, string AssetVersion, int AssetFiles, long AssetBytes,
    string DatabasePath, DateTimeOffset? LastSaveUtc, DateTimeOffset SampledUtc);
public sealed record LogEntry(long Id, DateTimeOffset Time, string Category, string Message);
public sealed record AssetEntry(string Kind, string Path, long Size, string Sha256, DateTime LastWriteUtc);
public sealed record SpriteFrameInfo(int Width, int Height, int OffsetX, int OffsetY);
public sealed record SpriteAppearance(string Name, string Library, int Frame);
public sealed record AssetManifest(string Version, AssetEntry[] Files);
public sealed record ResourceListing(string Version, AssetEntry[] Files, string[] Staged, string[] Conflicts);
public sealed record TextDocument(string Path, string Content, string Version, bool RequiresRestart = true);
public sealed record TextEdit(string Path, string Content, string Version);
public sealed record PublishResult(string Version, int ChangedFiles, bool Restarted);
public sealed record ActionResult(string Message);
public sealed record CatalogRow(int Id, string Name, string Details);
public sealed record DefinitionEdit(string Kind, int Id, string Name, int Level, uint Experience, int Hp, int MinDc, int MaxDc, uint Price, int StackSize, string Version = "");
public sealed record DefinitionClone(string Kind, int SourceId, string Name, string Version);
public sealed record MapDefinition(int Id, string Title, string FileName, int Light, int Music, bool NoFight, bool NoDropPlayer, bool NoDropMonster, bool NoTeleport, string Version);
public sealed record NpcDefinition(int Id, string Name, string FileName, int MapId, int X, int Y, int Image, int Rate, string Version,
    bool TimeVisible = false, int StartMinute = 0, int EndMinute = 1, int MinLevel = 0, int MaxLevel = 0,
    string DayOfWeek = "", string ClassRequired = "", int FlagNeeded = 0, bool ShowOnBigMap = false, int BigMapIcon = 0, bool CanTeleportTo = false);
public sealed record QuestDefinition(int Id, string Name, string Group, string FileName, int MinLevel, int MaxLevel, int Prerequisite, int RequiredClass, int Type, int TimeLimitSeconds, string Version);
public sealed record MapInspection(string Name, int Width, int Height, long BlockedCells, string Sha256, MapAssetUsage[]? Maps = null);
public sealed record MapThumbnailInfo(string Revision, int Width, int Height);
public sealed record MapAssetUsage(int Id, string Code, string Name, int MiniMap, int BigMap, int Music, string Light, int Npcs, int Spawns);
public sealed record CatalogFigure(int Id, string Name, string Library, int Frame, Dictionary<string, string> Attributes);
public sealed record CatalogFigurePage(CatalogFigure[] Items, int Total);
public sealed record MonsterDropDocument(int Id, string Name, string Script, string[] SelectedScripts, string Content,
    string FinalContent, string Version, string PreviewVersion, string[] Scripts, int LoadedRules, int FinalRules);
public sealed record MonsterDropEdit(string[] Scripts, string Content, string Version, string PreviewVersion = "");
public sealed record MonsterDropPreview(string FinalContent, string Version, int Rules);
