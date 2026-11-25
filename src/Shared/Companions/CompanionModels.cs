#nullable enable
namespace Mir2.Companions;

public sealed record Personality(int Diligence = 50, int Aggression = 30, int Risk = 45, int Social = 50,
    int Helpfulness = 50, int Exploration = 50, int Frugality = 50, int Collection = 50, int Persistence = 50, int Talkative = 50);
public sealed record PersonalityPreset(string Key, int Revision, string Name, string Description, Personality Traits, bool BuiltIn = true, bool Archived = false);
public sealed record OnlineWindow(int Days = 127, int StartMinute = 1140, int EndMinute = 1380, bool AllDay = false);
public sealed record CompanionSchedule
{
    public string TimeZone { get; set; } = "Asia/Shanghai";
    public OnlineWindow[] Windows { get; set; } = [new()];
    public int Attendance { get; set; } = 100;
    public int JitterMinutes { get; set; } = 10;
    public int SessionMinutes { get; set; } = 60;
    public int RestMinutes { get; set; } = 20;
    public int DailyMinutes { get; set; } = 120;
}
public sealed record CompanionProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Tags { get; set; } = "";
    public int Class { get; set; }
    public int Gender { get; set; }
    public string PresetKey { get; set; } = "balanced";
    public int PresetRevision { get; set; } = 1;
    public Personality Traits { get; set; } = new();
    public string SpeechStyle { get; set; } = "随和";
    public bool Scheduled { get; set; }
    public bool Enabled { get; set; }
    public CompanionSchedule Schedule { get; set; } = new();
    public bool AcceptGroup { get; set; } = true;
    public bool Chat { get; set; } = true;
    public string PkMode { get; set; } = "avoid";
    public CompanionGuildPreferences Guilds { get; set; } = new();
    public int LlmMode { get; set; }
    public Guid LlmModelId { get; set; }
    public int DeathLimit { get; set; } = 10;
    public string Goal { get; set; } = "均衡发展";
    public string Version { get; set; } = "";
}
public sealed record CompanionSettings
{
    public bool Enabled { get; set; }
    public int MaxOnline { get; set; } = 1;
    public int ReservePlayers { get; set; } = 5;
    public bool AllowPk { get; set; }
    public bool SmartScheduling { get; set; } = true;
    public bool SmartRecovery { get; set; }
    public CompanionArrivalSettings Arrivals { get; set; } = new();
    public string LlmEndpoint { get; set; } = "";
    public string LlmModel { get; set; } = "";
    public int DailyLlmCalls { get; set; } = 100;
    public bool HasLlmKey { get; set; }
    public CompanionLlmModel[] Models { get; set; } = [];
    public Guid DefaultModelId { get; set; }
    public string[] Nicknames { get; set; } = CompanionNames.All.ToArray();
    public string Version { get; set; } = "";
}
public sealed record CompanionArrivalSettings
{
    public bool Enabled { get; set; }
    public int MinMinutes { get; set; } = 30;
    public int MaxMinutes { get; set; } = 120;
    public int MaxProfiles { get; set; } = 100;
    public CompanionSchedule Schedule { get; set; } = new()
    { Windows = [new(AllDay:true)], JitterMinutes = 0, SessionMinutes = 120, DailyMinutes = 240 };
}
public sealed record CompanionArrivalState(DateTimeOffset? NextAt = null, DateTimeOffset? LastAt = null,
    Guid? LastProfileId = null, int Created = 0, string Result = "尚未触发");
public sealed record CompanionArrivalStatus(CompanionArrivalState State, string Status, int Profiles);
public sealed record CompanionDispatchStatus(int Waiting, int Resting, int Stopping);
public sealed record CompanionSessionPlan(int OnlineMinutes, int RestMinutes);
public sealed record CompanionLlmModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool HasKey { get; set; }
}
public sealed record CompanionModelKeyEdit(Guid Id, string? Key = null, bool Clear = false);
public sealed record CompanionSettingsEdit(CompanionSettings Settings, string? LlmKey = null, bool ClearLlmKey = false, CompanionModelKeyEdit[]? ModelKeys = null);
public sealed record CompanionLlmTest(CompanionLlmModel Model, string? Key = null, bool ClearKey = false);
public sealed record CompanionLlmTestResult(bool Success, string Message, long ElapsedMilliseconds);
public sealed record CompanionBatchPlan(int Count,string[] Nicknames,CompanionProfile Template,bool RandomClass=true,bool RandomGender=true,bool RandomPersonality=true);
public sealed record CompanionBatchPreview(CompanionProfile[] Profiles,string[] Excluded,int Available);
public sealed record CompanionBatchCreate(CompanionProfile[] Profiles,string[] Nicknames,string SettingsVersion);
public sealed record CompanionCommand(string Action, string Version);
public sealed record CompanionRow(CompanionProfile Profile, int AccountId, int CharacterId, int Level, string Map, string State,
    string Reason, int TodaySeconds, DateTimeOffset? NextOnline, string LastError, int? X = null, int? Y = null,
    DateTimeOffset? SessionUntil = null, DateTimeOffset? RestUntil = null, int? QueuePosition = null, string LastSessionReason = "");
public sealed record CompanionPage(CompanionRow[] Items, int Total);
public sealed record CompanionCapability(string Name, string State, string Detail);
public sealed record CompanionOverview(string VersionName, string InstanceId, string Protocol, CompanionSettings Settings,
    PersonalityPreset[] Presets, CompanionCapability[] Capabilities, int Online, CompanionLimits? Limits = null, int EnabledProfiles = 0, string StartAllVersion = "", int[]? AvailableClasses = null,
    CompanionArrivalStatus? Arrivals = null, CompanionDispatchStatus? Dispatch = null);
public sealed record CompanionLimits(int MaxConnections, int MaxPerIp, int Connections, int SameIpConnections, int EffectiveMaxOnline);
public sealed record SchedulePreview(DateTimeOffset Start, DateTimeOffset End, bool Attending, string Occurrence);
public sealed record CompanionEvent(DateTimeOffset Time, string Kind, string State, string Reason, string Text);
public sealed record CompanionSkillState(Spell Spell, string Name, byte Level, ushort Experience, ushort RequiredExperience, bool Temporary);
public sealed record CompanionCombatState(Stats Stats, CompanionSkillState[] Skills);
public sealed record CompanionRuntimeState(uint Gold, int Hp, int MaxHp, int Mp, int MaxMp, long Experience, long MaxExperience, KnownGoal? Goal, string[] Tasks,
    decimal BagWeight = 0, int MaxBagWeight = 0, int EmptySlots = 0, string GuildName = "", string GuildGoal = "", CompanionCombatState? Combat = null);
public sealed record CompanionDetail(CompanionRow Row, SchedulePreview[] Schedule, CompanionEvent[] Events, CompanionRuntimeState? Runtime = null);
public sealed record CompanionQuestCheck(int Id, string Name, int MinLevel, int MaxLevel, int Prerequisite, string Classes,
    string AcceptNpc, string FinishNpc, string[] Targets, string[] Warnings);
public sealed record CompanionWorldReport(int MapCount, int MonsterCount, int QuestCount, CompanionQuestCheck[] Quests);

// 这些消息仅走宿主与子进程的私有管道；管理 DTO 不引用凭据。
public sealed record WorkerStart(CompanionProfile Profile, string Account, string Password, bool Registered, string Host, int Port,
    string Instance, string Run, string Protocol, string ResourceUrl, string AssetVersion, string CacheDirectory, int Seed, int Deaths = 0);
public sealed record WorkerCommand(string Kind, WorkerStart? Start = null, Guid Id = default, CompanionProfile? Profile = null, CompanionKnowledge? Knowledge = null,
    string Text = "", string Context = "", bool Allowed = false);
public sealed record WorkerEvent(string Kind, Guid Id, string State = "", string Reason = "", string Text = "", string Protocol = "", string Session = "", bool Recoverable = false);
public sealed record GoalSuggestion(string[] Candidates,string Observation);
public static class CompanionProtocol
{
    public const string Revision = "mir2-companion-15";
    public const int MaxMessage = 262144;
    public static string Fingerprint => Revision + ":" + typeof(Packet).Module.ModuleVersionId;
}
public sealed record KnownNpc(int DefinitionId, uint ObjectId, int MapId, int X, int Y, string Name, int[] QuestIds, int[]? TradeTypes = null);
public sealed record KnownQuest(int Id, uint Npc, uint FinishNpc, int[] MonsterIds, int[] ItemIds);
public sealed record KnownSpawn(int MapId, int MonsterId, int X, int Y, int Level, string Name, uint Experience);
public sealed record KnownExit(int MapId, int X, int Y, int DestinationMapId);
public sealed record KnownEnemy(uint ObjectId, int MonsterId, int Level, uint Experience, bool Suitable = true, bool Harvestable = false);
public sealed record KnownSale(ulong UniqueId, ushort Count);
public sealed record KnownStorage(ulong UniqueId, ushort Count, bool Take);
public sealed record KnownPartyPlan(string Invite, string[] Peers, string Leader, KnownGoal? Goal, uint[] Targets);
public sealed record KnownGoal(string Kind, int MapId, int X, int Y, string Reason, int QuestId = 0, uint NpcId = 0,
    int[]? MonsterIds = null, KnownExit? NextExit = null, string Key = "", KnownSale[]? SellItems = null, KnownStorage[]? StorageItems = null, int PublicItemId = 0);
public sealed record CompanionKnowledge(string Revision, int MapId, bool NoFight, bool NoDrug, bool NoEscape,
    KnownNpc[] Npcs, KnownQuest[] Quests, KnownSpawn[] Spawns, KnownExit[] Exits, bool AllowPk = false, bool InSafeZone = false,
    KnownGoal? Goal = null, KnownEnemy[]? Enemies = null, uint[]? LootTargets = null, bool NoTownTeleport = true, KnownGuildPlan? Guild = null,
    KnownPartyPlan? Party = null, bool NoRandom = true, bool TownOnRoute = false, bool NoHero = true);
