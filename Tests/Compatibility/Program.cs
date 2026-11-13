// macOS compatibility and persistence checks, 2026-11-11. GPL-3.0-only.
using System.Text.Json;
using Crystal.Native;
using Microsoft.Data.Sqlite;
using Server.ConsoleHost;
using C = ClientPackets;
using S = ServerPackets;
using Mir2.Hosting;
using Mir2.Management;

if (args.Contains("--fixture"))
{
    Fixture.Create(args[Array.IndexOf(args, "--fixture") + 1], args.Contains("--trade-fixture"), args.Contains("--guild-fixture"), args.Contains("--catalog-fixture"), args.Contains("--guided-companions"), args.Contains("--gm-fixture"), args.Contains("--skill-fixture"));
    return;
}
if (args.Contains("--admin-session")) { await AdminSession.Run(args); return; }
if (args.Contains("--skill-assets")) { SkillAssetChecks.Run(); return; }
if (args.Contains("--gm-checks")) { await GmCommandSession.Run(args); return; }
if (args.Contains("--companion-supplies")) { CompanionScenario.Prepare(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-supplies")+1])); return; }
if (args.Contains("--companion-stocked-sale")) { CompanionScenario.PrepareStockedSale(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-stocked-sale")+1])); return; }
if (args.Contains("--companion-meat")) { CompanionMeatScenario.Prepare(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-meat")+1])); return; }
if (args.Contains("--companion-exit")) { CompanionExitScenario.Prepare(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-exit")+1])); return; }
if (args.Contains("--companion-healing")) { CompanionScenario.PrepareHealing(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-healing")+1])); return; }
if (args.Contains("--companion-town")) { CompanionScenario.PrepareTown(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-town")+1]), args.Contains("--deny-town")); return; }
if (args.Contains("--companion-tree")) { CompanionCombatScenario.Prepare(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-tree")+1]),true); return; }
if (args.Contains("--companion-ranged")) { CompanionCombatScenario.Prepare(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-ranged")+1]),false,args.Contains("--empty-mana")); return; }
if (args.Contains("--companion-variant")) { CompanionScenario.Prepare(new ProjectPaths(), int.Parse(args[Array.IndexOf(args,"--companion-variant")+1]),true); return; }
if (args.Contains("--companion-social")) { await CompanionSocialSession.Run(args); return; }
if (args.Contains("--trade-recovery")) { await TradeRecoverySession.Run(args); return; }
if (args.Contains("--chinese-checks")) { ChineseTranslationChecks.Run(new ProjectPaths()); return; }

var checks = 0;
void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    checks++;
}
void Reject(Action action, string message)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or JsonException or KeyNotFoundException) { checks++; return; }
    throw new Exception(message);
}

var temporary = Path.Combine(Path.GetTempPath(), "mir2-test-" + Guid.NewGuid());
Directory.CreateDirectory(temporary);
try
{
    var iniPath = Path.Combine(temporary, "ini-read", "Configs", "test.ini");
    var ini = new InIReader(iniPath);
    Check(!Directory.Exists(Path.GetDirectoryName(iniPath)), "Reading INI created an empty configuration directory");
    ini.ReadInt32("Check", "Value", 42, false);
    Check(!Directory.Exists(Path.GetDirectoryName(iniPath)), "Read-only INI default created a directory");
    ini.Write("Check", "Value", 42);
    Check(File.Exists(iniPath) && new InIReader(iniPath).ReadInt32("Check", "Value", 0, false) == 42, "INI save failed to create its parent directory");
    var path = Path.Combine(temporary, "mir2.sqlite3");
    var source = Path.Combine(temporary, "seed");
    Directory.CreateDirectory(Path.Combine(source, "Guilds"));
    File.WriteAllBytes(Path.Combine(source, "Server.MirDB"), new byte[] { 1, 2, 3 });
    File.WriteAllBytes(Path.Combine(source, "Guilds", "0.mgd"), new byte[] { 8 });
    using (var legacy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
    {
        legacy.Open(); using var create = legacy.CreateCommand();
        create.CommandText = "CREATE TABLE admin_audit (id INTEGER PRIMARY KEY, reason TEXT); INSERT INTO admin_audit VALUES (1,'obsolete');";
        create.ExecuteNonQuery();
    }
    using (var storage = new SqliteDatabaseStorage(path))
    {
        storage.Import(source);
        using (var inspect = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            inspect.Open(); using var query = inspect.CreateCommand(); query.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='admin_audit'";
            Check(Convert.ToInt64(query.ExecuteScalar()) == 0, "Obsolete audit data was not removed on startup");
        }
        Check(storage.Contains("./Server.MirDB"), "World seed was not imported");
        storage.Write(".\\Server.MirDB", new byte[] { 4, 5, 6 });
        storage.Import(source);
        using var current = storage.Read("Server.MirDB");
        Check(current.ReadByte() == 4, "Seed import overwrote live data");
        storage.Remove("Guilds/0.mgd");
        storage.Import(source);
        Check(!storage.Contains("Guilds/0.mgd"), "Deleted guild was reimported");
        storage.Write("Guilds/1.mgd", new byte[] { 7 });
        Check(storage.List("./Guilds", "*.mgd").SequenceEqual(new[] { "Guilds/1.mgd" }), "Guild enumeration failed");
        storage.Transaction(() => { storage.Write("Guilds/1.mgd", [9]); storage.Write("players", [10]); });
        Check(storage.Read("Guilds/1.mgd").ReadByte() == 9 && storage.Read("players").ReadByte() == 10, "Multi-snapshot commit failed");
        try { storage.Transaction(() => { storage.Write("Guilds/1.mgd", [99]); storage.Remove("players"); throw new IOException("injected failure"); }); } catch (IOException) { }
        Check(storage.Read("Guilds/1.mgd").ReadByte() == 9 && storage.Contains("players"), "Failed transaction leaked player or guild changes");
        var fullBackup = Path.Combine(temporary, "backup", "mir2.sqlite3"); storage.Backup(fullBackup);
        using var snapshot = new SqliteDatabaseStorage(fullBackup);
        Check(snapshot.Read("Guilds/1.mgd").ReadByte() == 9 && snapshot.Read("players").ReadByte() == 10, "Full SQLite backup lost snapshots");
        storage.Write("players", [11]); Check(snapshot.Read("players").ReadByte() == 10, "Backup changed with the live database");
        using var header = new MemoryStream(); using (var writer = new BinaryWriter(header, System.Text.Encoding.UTF8, true)) { writer.Write(Server.MirEnvir.Envir.Version); writer.Write(0); }
        storage.Write(Server.MirEnvir.Envir.AccountPath, header.ToArray());
        var upgradeBackups = Path.Combine(temporary, "upgrades"); storage.BackupBeforePlayerUpgrade(upgradeBackups);
        var upgrade = Directory.GetFiles(upgradeBackups, "mir2.sqlite3", SearchOption.AllDirectories).Single();
        using var originalStorage = new SqliteDatabaseStorage(upgrade); using var originalHeader = new BinaryReader(originalStorage.Read(Server.MirEnvir.Envir.AccountPath));
        Check(originalHeader.ReadInt32() == Server.MirEnvir.Envir.Version && originalHeader.ReadInt32() == 0, "Upgrade backup did not preserve original player format");
        header.Position = 4; using (var writer = new BinaryWriter(header, System.Text.Encoding.UTF8, true)) writer.Write(Server.MirEnvir.Envir.CustomVersion);
        storage.Write(Server.MirEnvir.Envir.AccountPath, header.ToArray()); storage.BackupBeforePlayerUpgrade(upgradeBackups);
        Check(Directory.GetFiles(upgradeBackups, "mir2.sqlite3", SearchOption.AllDirectories).Length == 1, "Current player format repeatedly creates upgrade backups");
    }

    AdministrationChecks.Run(Check, temporary);
    CompanionChecks.Run(Check, temporary);
    SystemSettingsChecks.Run(Check, temporary);
    CompanionAllChecks.Run(Check, temporary);
    await CompanionLlmChecks.Run(Check);
    Check(DisplaySizing.Fit(1920, 1080, 1) == (1280, 960), "Normal DPI window changed unexpectedly");
    Check(DisplaySizing.Fit(3840, 2160, 2) == (2560, 1920), "Retina window did not follow the display scale");
    foreach (var (width, height, scale) in new[] { (1366, 768, 1f), (3024, 1890, 2f), (1920, 1080, 1.5f), (800, 600, 1f) })
    {
        var fit = DisplaySizing.Fit(width, height, scale);
        Check(fit.Width <= width * .9 && fit.Height <= height * .9 && fit.Width * 3 == fit.Height * 4, "Scaled window exceeds its usable screen or loses the logical canvas ratio");
    }
    RespawnLocationChecks.Run(Check);
    ClientAnimationChecks.Run(Check);
    ClientAutomationChecks.Run(Check);
    ClientSkillChecks.Run(Check);
    ClassSkillChecks.Run(Check);
    SkillEffectChecks.Run(Check);
    SkillDamageChecks.Run(Check);
    GameBalanceChecks.Run(Check, temporary);
    AttackModeChecks.Run(Check);
    CompanionPlanningChecks.Run(Check);
    CompanionPathChecks.Run(Check);
    CompanionSupplyChecks.Run(Check);
    CompanionHealingChecks.Run(Check);
    CompanionInventoryChecks.Run(Check);
    CompanionLootChecks.Run(Check);
    ClientItemChecks.Run(Check);
    ItemPresentationChecks.Run(Check);
    CommandChecks.Run(Check);
    NpcSessionChecks.Run(Check);
    await ConnectionChecks.Run(Check);
    await ServerConnectionChecks.Run(Check);
    GroupChecks.Run(Check);
    TradeChecks.Run(Check);
    GuildChecks.Run(Check);
    GuildStorageChecks.Run(Check, temporary);
    using (var storage = new SqliteDatabaseStorage(path))
    using (var saved = storage.Read("Server.MirDB"))
        Check(saved.ReadByte() == 4, "SQLite data did not survive reopening");
    using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM previous_snapshots WHERE name = 'Server.MirDB'";
        Check(((byte[])command.ExecuteScalar()!)[0] == 1, "Previous snapshot was not retained");
        command.CommandText = "PRAGMA integrity_check";
        Check((string)command.ExecuteScalar()! == "ok", "SQLite integrity check failed");
    }

    Packet.IsServer = false;
    var compressed = new S.NPCGoods { Rate = 1.25F };
    var bytes = compressed.GetPacketBytes().ToArray();
    var decoded = Packet.ReceivePacket(bytes, out var extra);
    Check(decoded is S.NPCGoods { Rate: 1.25F } && extra.Length == 0, "Compressed packet did not round trip");
    Check(Packet.ReceivePacket(bytes[..3], out _) == null, "Partial header was not buffered");
    Check(Packet.ReceivePacket(bytes[..^1], out _) == null, "Partial packet was not buffered");
    Reject(() => Packet.ReceivePacket(new byte[] { 3, 0, 0, 0 }, out _), "Malformed packet header accepted");

    var party = new GameState();
    party.Apply(new S.SwitchGroup { AllowGroup = true });
    party.Apply(new S.GroupInvite { Name = "Leader" });
    Check(party.AllowGroup && party.GroupInvitation == "Leader", "Native group invitation/mode were ignored");
    party.Apply(new S.AddMember { Name = "Leader" }); party.Apply(new S.AddMember { Name = "Member" }); party.Apply(new S.AddMember { Name = "member" });
    party.Apply(new S.GroupMembersMap { PlayerName = "Member", PlayerMap = "Town" }); party.Apply(new S.SendMemberLocation { MemberName = "Member", MemberLocation = new(5, 6) });
    Check(party.GroupMembers.SequenceEqual(new[] { "Leader", "Member" }) && party.GroupMaps["member"] == "Town" && party.GroupLocations["Member"] == new System.Drawing.Point(5, 6) && party.GroupInvitation == "", "Native party lost leader order, duplicate filtering or member map/position");
    party.Apply(new S.GroupMembersMap { PlayerName = "Member", PlayerMap = "Other Town" });
    Check(!party.GroupLocations.ContainsKey("Member"), "Party member teleport retained coordinates from the previous map");
    party.Apply(new S.DeleteMember { Name = "MEMBER" });
    Check(party.GroupMembers.SequenceEqual(new[] { "Leader" }) && party.GroupMaps.Count == 0 && party.GroupLocations.Count == 0, "Party removal left stale map/position metadata");
    party.Apply(new S.DeleteGroup()); Check(party.GroupMembers.Count == 0 && party.AllowGroup, "Disbanding changed permission or retained members");
    party.Apply(new S.AddMember { Name = "Leader" }); party.Apply(new S.SwitchGroup { AllowGroup = false });
    Check(!party.AllowGroup && party.GroupMembers.Count == 0, "Disabling groups retained party data");
    party.Apply(new S.SwitchGroup { AllowGroup = true }); party.Apply(new S.GroupInvite { Name = "Leader" }); party.Apply(new S.LogOutSuccess { Characters = [] });
    Check(!party.AllowGroup && party.GroupInvitation == "" && party.GroupMembers.Count == 0, "Character switching leaked group permission or invitations");
    party.Apply(new S.KeepAlive { Time = Environment.TickCount64 - 35 }); Check(party.Ping >= 35 && party.Ping < 500, "Original keepalive did not measure round-trip latency");

    var state = new GameState();
    var info = new ItemInfo { Index = 7, Type = ItemType.Weapon, Shape = 1, Name = "Sword" };
    var item = new UserItem(info) { UniqueID = ulong.MaxValue };
    state.Apply(new S.NewItemInfo { Info = info });
    state.Apply(new S.UserInformation { ObjectID = 1, Location = new System.Drawing.Point(10, 10),
        Inventory = new UserItem[] { item, null! }, Equipment = new UserItem[14], QuestInventory = [] });
    state.Apply(new S.EquipItem { Grid = MirGridType.Inventory, UniqueID = ulong.MaxValue, To = 0, Success = true });
    Check(state.User!.Equipment[0] == item && state.User.Inventory[0] == null, "Native equipment acknowledgement failed");
    Check(state.Me!.Weapon == 1, "Native weapon appearance did not follow the original item shape");
    state.Apply(new S.UserSlotsRefresh { Inventory = null, Equipment = null });
    Check(state.User.Equipment[0] == item, "Partial refresh cleared equipment");
    state.Apply(new S.RemoveItem { Grid = MirGridType.Inventory, UniqueID = ulong.MaxValue, To = 1, Success = true });
    Check(state.User.Inventory[1] == item && state.User.Equipment[0] == null, "Native unequip acknowledgement failed");
    state.Apply(new S.UserLocation { Location = new System.Drawing.Point(11, 10), Direction = MirDirection.Right });
    Check(state.Me.Location.X == 11 && state.User.Location.X == 11, "Native authoritative movement failed");
    state.Apply(new S.GainExperience { Amount = 25 });
    Check(state.User.Experience == 25, "Native experience was not updated");
    state.Apply(new S.UseItem { Grid = MirGridType.Inventory, UniqueID = ulong.MaxValue, Success = true });
    Check(state.User.Inventory.All(i => i == null), "Native successful item consumption was not reflected");
    state.User.Inventory = new UserItem[46];
    var potionInfo = new ItemInfo { Index = 8, Type = ItemType.Potion, StackSize = 5 };
    state.Apply(new S.NewItemInfo { Info = potionInfo });
    state.Apply(new S.GainedItem { Item = new UserItem(potionInfo) { UniqueID = 1, Count = 4 } });
    state.Apply(new S.GainedItem { Item = new UserItem(potionInfo) { UniqueID = 2, Count = 3 } });
    Check(state.User.Inventory[0].Count == 5 && state.User.Inventory[1].Count == 2, "Partial stack overflow differs from original client/server");
    state.Apply(new S.GainedItem { Item = new UserItem(info) { UniqueID = 3 } });
    Check(state.User.Inventory[6]?.UniqueID == 3, "Equipment loot did not prefer bag slots");
    state.Apply(new S.DuraChanged { UniqueID = 3, CurrentDura = 42 });
    Check(state.User.Inventory[6].CurrentDura == 42, "Durability acknowledgement was lost");
    state.Apply(new S.MoveItem { Grid = MirGridType.Inventory, From = 6, To = 7, Success = false });
    Check(state.User.Inventory[6]?.UniqueID == 3, "Rejected inventory move changed local state");
    state.Apply(new S.SpellToggle { ObjectID = state.User.ObjectID, Spell = Spell.FlamingSword, CanUse = true });
    Check(state.ActiveSkills.Contains(Spell.FlamingSword), "Original melee skill acknowledgement was ignored");
    state.Apply(new S.SpellToggle { ObjectID = state.User.ObjectID, Spell = Spell.FlamingSword, CanUse = false });
    Check(!state.ActiveSkills.Contains(Spell.FlamingSword), "Expired melee skill stayed enabled");
    var stored = new UserItem(info) { UniqueID = ulong.MaxValue };
    state.User.Inventory[8] = stored;
    state.User.RequireStoragePassword = true;
    state.Apply(new S.UserStorage { Storage = new UserItem[160] });
    state.Apply(new S.NPCStorage());
    Check(state.StorageActive && !state.StorageUnlocked, "Password-protected storage opened without authentication");
    state.Apply(new S.StoragePasswordResult { Result = 4, HasPassword = true });
    Check(state.StorageUnlocked && state.User.HasStoragePassword, "Initial storage password setup did not unlock the active window");
    state.Apply(new S.StoreItem { From = 8, To = 0, Success = false });
    Check(state.User.Inventory[8] == stored && state.Storage[0] == null, "Rejected storage deposit changed inventory");
    state.Apply(new S.StoreItem { From = 8, To = 0, Success = true });
    Check(state.User.Inventory[8] == null && state.Storage[0]?.UniqueID == ulong.MaxValue, "Storage deposit lost a 64-bit item ID");
    state.Apply(new S.MoveItem { Grid = MirGridType.Storage, From = 0, To = 3, Success = true });
    Check(state.Storage[0] == null && state.Storage[3] == stored, "Storage rearrangement was ignored");
    state.Apply(new S.MoveItem { Grid = MirGridType.Storage, From = -1, To = 300, Success = true });
    state.Apply(new S.TakeBackItem { From = 3, To = 8, Success = false });
    Check(state.Storage[3] == stored && state.User.Inventory[8] == null, "Rejected withdrawal or invalid slots mutated storage");
    state.Apply(new S.TakeBackItem { From = 3, To = 8, Success = true });
    Check(state.Storage[3] == null && state.User.Inventory[8] == stored, "Storage withdrawal did not restore the same item");
    state.Apply(new S.ResizeStorage { Size = 160, HasExpandedStorage = true, ExpiryTime = DateTime.Today.AddDays(10) });
    Check(state.User.HasExpandedStorage && state.User.ExpandedStorageExpiryTime > DateTime.Today && state.User.Inventory[8] == stored, "Storage rental acknowledgement lost entitlement or items");
    state.Apply(new S.ResizeStorage { Size = 160, HasExpandedStorage = false });
    Check(!state.User.HasExpandedStorage, "Expired expanded storage remained enabled");
    state.Apply(new S.NPCStorage());
    Check(!state.StorageUnlocked && state.Storage.Length == 0, "Reopened password-protected storage retained unlocked contents");
    state.Apply(new S.StorageUnlockResult { Result = 2, HasPassword = true });
    Check(!state.StorageUnlocked, "Incorrect storage password unlocked the client");
    state.Apply(new S.StorageUnlockResult { Result = 0, HasPassword = true });
    state.Apply(new S.UserStorage { Storage = new[] { new UserItem(info) { Info = null!, UniqueID = 99 } } });
    Check(state.StorageUnlocked && state.Storage[0].Info == info, "Unlocked storage metadata did not bind");
    state.Apply(new S.NPCResponse { Page = [] });
    state.Apply(new S.StorageUnlockResult { Result = 0, HasPassword = true });
    Check(!state.StorageActive && !state.StorageUnlocked, "Late authentication reopened a closed NPC storage page");

    state.User.Level = 5; state.User.Class = MirClass.Warrior; state.User.QuestInventory = new UserItem[40];
    var quest = new ClientQuestInfo { Index = 1, NPCIndex = 10, FinishNPCIndex = 11, MinLevelNeeded = 1, MaxLevelNeeded = 10, ClassNeeded = RequiredClass.None };
    state.Apply(new S.ChangeQuest { Quest = new ClientQuestProgress { Id = 1, Taken = true, TaskList = ["NativeTarget 1/2"] }, QuestState = QuestState.Add });
    Check(state.Quests[1].QuestInfo == null, "Unknown quest metadata was fabricated");
    state.Apply(new S.NewQuestInfo { Info = quest });
    Check(state.Quests[1].QuestInfo == quest && !state.CanAcceptQuest(quest), "Late quest metadata failed to bind or an active quest was offered twice");
    state.Apply(new S.ChangeQuest { Quest = new ClientQuestProgress { Id = 1, Taken = true, Completed = true, TaskList = ["NativeTarget 2/2"] }, QuestState = QuestState.Update });
    Check(state.Quests.Count == 1 && state.Quests[1].Completed && state.Quests[1].TaskList.Single().Contains("2/2"), "Quest update duplicated progress or lost completion");
    Check(state.QuestsAt(11).Single().Index == 1, "Completed quest was not offered at its finish NPC");
    state.Apply(new S.ChangeQuest { Quest = new ClientQuestProgress { Id = 1 }, QuestState = QuestState.Remove });
    Check(state.Quests.Count == 0 && state.CanAcceptQuest(quest), "Removed quest remained active");
    quest.MinLevelNeeded = 6;
    Check(!state.CanAcceptQuest(quest), "Quest minimum level ignored");
    quest.MinLevelNeeded = 1; quest.MaxLevelNeeded = 4;
    Check(!state.CanAcceptQuest(quest), "Quest maximum level ignored");
    quest.MaxLevelNeeded = 10; quest.ClassNeeded = RequiredClass.Wizard;
    Check(!state.CanAcceptQuest(quest), "Quest class restriction ignored");
    quest.ClassNeeded = RequiredClass.None; quest.QuestNeeded = 2;
    Check(!state.CanAcceptQuest(quest), "Quest prerequisite ignored");
    state.Apply(new S.CompleteQuest { CompletedQuests = [2] });
    Check(state.CanAcceptQuest(quest), "Completed prerequisite was not recognized");
    state.Apply(new S.CompleteQuest { CompletedQuests = [1] });
    Check(!state.CompletedQuests.Contains(2) && !state.CanAcceptQuest(quest), "Completed quest reset merged stale daily state");
    var tokenInfo = new ItemInfo { Index = 20, Type = ItemType.Quest, StackSize = 5 };
    state.Apply(new S.NewItemInfo { Info = tokenInfo });
    state.Apply(new S.GainedQuestItem { Item = new UserItem(tokenInfo) { UniqueID = 20, Count = 4 } });
    state.Apply(new S.GainedQuestItem { Item = new UserItem(tokenInfo) { UniqueID = 21, Count = 3 } });
    Check(state.User.QuestInventory[0].Count == 5 && state.User.QuestInventory[1].Count == 2, "Quest inventory stacking differs from the original rules");
    state.Apply(new S.DeleteQuestItem { UniqueID = 20, Count = 2 });
    Check(state.User.QuestInventory[0].Count == 3 && state.User.Inventory[8] == stored, "Quest item deletion changed the regular inventory");
    state.Apply(new S.DeleteQuestItem { UniqueID = 20, Count = 10 });
    Check(state.User.QuestInventory[0] == null, "Quest item removal underflowed");
    state.Apply(new S.LogOutSuccess { Characters = [] });
    Check(state.User == null && state.Storage.Length == 0 && !state.StorageUnlocked && state.Quests.Count == 0 && state.CompletedQuests.Count == 0, "Character logout leaked private per-character state");
    Check(state.Items[info.Index] == info && state.QuestDefinitions[1] == quest, "Same-connection logout discarded metadata the server will not resend");
    state.Reset();
    Check(state.User == null && state.Actors.Count == 0 && state.Items.Count == 0, "Reconnect retained the previous world state");

    var pathEnvironment = new[] { "MIR2_PROJECT_ROOT", "MIR2_DATA_ROOT", "MIR2_RUNTIME_ROOT" }.ToDictionary(name => name, Environment.GetEnvironmentVariable);
    try
    {
        foreach (var name in pathEnvironment.Keys) Environment.SetEnvironmentVariable(name, null);
        Environment.SetEnvironmentVariable("MIR2_PROJECT_ROOT", temporary);
        var original = new ProjectPaths();
        Check(original.Data == Path.Combine(temporary, "data", "versions", "default"), "Default game version was not isolated under data/versions/default");
        var configured = new ProjectPaths(versionDirectory: "versions/复古");
        Check(configured.Data == Path.GetFullPath("versions/复古", AppContext.BaseDirectory) && configured.Runtime == Path.Combine(configured.Data, "runtime"), "Configured version or its runtime depends on the working directory");
        Check(configured.Assets == Path.Combine(configured.Data, "assets") && configured.World == Path.Combine(configured.Data, "world") && configured.Database == Path.Combine(configured.Data, "runtime", "mir2.sqlite3"), "Version resources or SQLite escaped the version directory");
        Check(configured.VersionName == "复古" && configured.VersionId != original.VersionId, "Distinct versions share their deployment identity");
        Check(new ProjectPaths(configured.Data + Path.DirectorySeparatorChar).VersionId == configured.VersionId, "Trailing directory separator changed the version identity");
        Environment.SetEnvironmentVariable("MIR2_DATA_ROOT", original.Data);
        Check(new ProjectPaths(versionDirectory: configured.Data).Data == original.Data, "MIR2_DATA_ROOT did not override published version configuration");
        Check(new ProjectPaths(configured.Data).Data == configured.Data, "Explicit test data path did not override the environment");
    }
    finally { foreach (var pair in pathEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }

    var managed = new ProjectPaths(Path.Combine(temporary, "managed"));
    var key = managed.AdminKey();
    Check(key.Length == 64 && key == managed.AdminKey() && ProjectPaths.KeyEquals(key, key) && !ProjectPaths.KeyEquals("invalid", key), "Management key generation or constant-time comparison failed");
    if (!OperatingSystem.IsWindows())
    {
        Check(File.GetUnixFileMode(managed.AdminKeyFile) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Management key permissions are not owner-only");
        Check(File.GetUnixFileMode(managed.Runtime) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "Private runtime directory is accessible to other users");
    }
    Reject(() => ProjectPaths.SafePath(temporary, "../outside"), "Managed path traversal accepted");
    Reject(() => ProjectPaths.SafePath(temporary, "/outside"), "Absolute managed path accepted");
    var linked = Path.Combine(temporary, "linked"); Directory.CreateSymbolicLink(linked, source);
    Reject(() => ProjectPaths.SafePath(temporary, "linked/Server.MirDB"), "Nested symbolic link escaped the managed directory");
    foreach (var kind in new[] { "maps", "libraries", "sounds" }) Directory.CreateDirectory(Path.Combine(managed.Assets, kind));
    var soundList = Path.Combine(managed.Assets, "sounds", "SoundList.lst"); File.WriteAllText(soundList, "1:test.wav\n");
    var catalog = new AssetCatalog(managed); var originalVersion = catalog.Version;
    Check(originalVersion.Length == 20 && catalog.Files.Length == 1, "Canonical content manifest was not created");
    File.SetLastWriteTimeUtc(soundList, DateTime.UtcNow.AddSeconds(5)); catalog.Refresh();
    Check(catalog.Version == originalVersion, "Resource version changed for identical contents");
    File.AppendAllText(soundList, "2:other.wav\n"); catalog.Refresh();
    Check(catalog.Version != originalVersion, "Resource content changes did not invalidate the micro-client version");
    File.AppendAllText(soundList, "3:unpublished.wav\n");
    try { catalog.ReadPath("sounds", "SoundList.lst"); throw new Exception("Unpublished resource mutation was served"); }
    catch (InvalidOperationException) { checks++; }
    catalog.Refresh();
    var publishedVersion = catalog.Version; var timestamp = File.GetLastWriteTimeUtc(soundList); var content = File.ReadAllText(soundList);
    File.WriteAllText(soundList, content.Replace("1:", "9:")); File.SetLastWriteTimeUtc(soundList, timestamp);
    catalog = new AssetCatalog(managed);
    Check(catalog.Version != publishedVersion, "Startup trusted stale persisted resource hashes instead of actual content");
    File.WriteAllText(soundList, content); File.SetLastWriteTimeUtc(soundList, timestamp);
    catalog.Refresh(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sounds/SoundList.lst" });
    Check(catalog.Version == publishedVersion, "Publication did not rehash a changed file with unchanged size and timestamp");
    var lease = catalog.TryRead()!; var publication = catalog.BeginPublish(CancellationToken.None);
    Check(!publication.IsCompleted && catalog.TryRead() == null, "Publication did not block new reads or wait for an in-flight resource response");
    lease.Dispose(); await publication; catalog.EndPublish();
    using (var resumed = catalog.TryRead()) Check(resumed != null, "Resource reads did not resume after publication");
    Directory.CreateDirectory(Path.Combine(managed.World, "Configs")); Directory.CreateDirectory(Path.Combine(managed.World, "Envir"));
    var documents = new DevelopmentFiles(managed);
    var created = documents.Save(new TextEdit("Configs/Test.ini", "[Setup]\nValue=1\n", ""));
    Check(created.Version.Length == 64 && created.Content.Contains("Value=1"), "Configuration editor did not atomically create a versioned file");
    var updated = documents.Save(new TextEdit(created.Path, "[Setup]\nValue=2\n", created.Version));
    Check(updated.Version != created.Version && Directory.GetFiles(Path.Combine(managed.Runtime, "backups"), "Test.ini", SearchOption.AllDirectories).Length == 1, "Text editor did not preserve a backup");
    try { documents.Save(new TextEdit(created.Path, "stale", created.Version)); throw new Exception("Stale text edit overwrote a newer version"); }
    catch (InvalidOperationException) { checks++; }
    Reject(() => documents.Read("../runtime/admin.key"), "Development tools exposed the management key");
    var png = SpritePreview.Png(new byte[8]);
    Check(png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Native sprite preview is not PNG");
    await MapRenderingChecks.Run(Check, catalog, managed);

    var previousDirectory = Directory.GetCurrentDirectory();
    var previousStorage = Server.DatabaseStorage.Current;
    var world = Server.MirEnvir.Envir.Main; var previousCounter = world.ItemIndex;
    var testDefinition = new ItemInfo { Index = 20, Name = "EditorTest", Price = 10, StartItem = true };
    try
    {
        Directory.SetCurrentDirectory(managed.World);
        using var sqlite = new SqliteDatabaseStorage(Path.Combine(managed.Runtime, "definition-tests.sqlite3"));
        var failing = new FailingStorage(sqlite); Server.DatabaseStorage.Current = failing;
        world.ItemInfoList.Add(testDefinition); world.ItemIndex = 1;
        var definitions = new WorldDefinitions(managed, catalog); var original = definitions.Read("items", 20);
        failing.WritesBeforeFailure = 2;
        try { definitions.Save(original with { Price = 99 }); throw new Exception("Storage failure was swallowed"); }
        catch (IOException) { checks++; }
        Check(definitions.Read("items", 20) == original, "Failed save changed the in-memory item definition");
        using (var snapshot = sqlite.Read(Server.MirEnvir.Envir.DatabasePath))
        using (var backup = File.OpenRead(Directory.GetFiles(Path.Combine(managed.Runtime, "backups"), "Server.MirDB", SearchOption.AllDirectories).Single()))
        {
            using var saved = new MemoryStream(); snapshot.CopyTo(saved); using var copy = new MemoryStream(); backup.CopyTo(copy);
            Check(saved.ToArray().SequenceEqual(copy.ToArray()), "Independent pre-edit backup differs from the SQLite snapshot after failed save");
        }
        failing.WritesBeforeFailure = 2;
        try { definitions.Clone(new DefinitionClone("items", 20, "FailedCopy", original.Version)); throw new Exception("Copy storage failure was swallowed"); }
        catch (IOException) { checks++; }
        Check(world.ItemInfoList.Count == 1 && world.ItemIndex == 1, "Failed copy leaked an item or advanced the allocator");
        failing.WritesBeforeFailure = int.MaxValue;
        var copied = definitions.Clone(new DefinitionClone("items", 20, "SavedCopy", original.Version));
        var clone = world.ItemInfoList.Single(i => i.Index == copied.Id);
        Check(copied.Id == 21 && !clone.StartItem && !ReferenceEquals(clone.Stats, testDefinition.Stats), "Copied item reused a legacy ID, starter flag or mutable stats");
        world.ItemInfoList.Remove(clone);

        var running = typeof(Server.MirEnvir.Envir).GetProperty("Running")!;
        var network = typeof(Server.MirEnvir.Envir).GetProperty("NetworkStarted")!;
        try
        {
            // Pump the real queue manually, without starting any sockets or game threads.
            running.SetValue(world, true); network.SetValue(world, true);
            var runtime = new GameRuntime(managed, catalog);
            using var pendingCancellation = new CancellationTokenSource(); var touched = false;
            var pending = runtime.Invoke(() => { touched = true; return true; }, pendingCancellation.Token);
            pendingCancellation.Cancel();
            try { await pending; throw new Exception("Queued request ignored cancellation"); }
            catch (OperationCanceledException) { checks++; }
            Check(world.HostActions.TryDequeue(out var skipped), "Canceled game action was not queued"); skipped!();
            Check(!touched, "Canceled queued action mutated the world later");
            using var activeCancellation = new CancellationTokenSource();
            var active = runtime.Invoke(() => { activeCancellation.Cancel(); return 42; }, activeCancellation.Token);
            Check(world.HostActions.TryDequeue(out var execute), "Active game action was not queued"); execute!();
            Check(await active == 42, "A browser disconnect detached an active game-thread write");
            var fault = runtime.Invoke<int>(() => throw new InvalidDataException("Invalid queued command"));
            Check(world.HostActions.TryDequeue(out var fail), "Faulted game action was not queued"); fail!();
            try { await fault; throw new Exception("Game action swallowed a validation failure"); }
            catch (InvalidDataException) { checks++; }
        }
        finally { running.SetValue(world, false); network.SetValue(world, false); }
    }
    finally
    {
        world.ItemInfoList.Remove(testDefinition); world.ItemIndex = previousCounter;
        Server.DatabaseStorage.Current = previousStorage; Directory.SetCurrentDirectory(previousDirectory);
    }

    File.WriteAllText(Path.Combine(temporary, "inside.map"), "test");
    Check(GameAssets.Resolve(temporary, "inside", ".map").EndsWith("inside.map"), "Asset resolution failed");
    Reject(() => GameAssets.Resolve(temporary, "../outside", ".map"), "Asset traversal escaped the asset directory");
    File.WriteAllBytes(Path.Combine(temporary, "bad.map"), new byte[] { 255, 127, 255, 127, 0, 0, 0, 0 });
    Reject(() => new Client.MirObjects.MapReader(Path.Combine(temporary, "bad.map")), "Hostile map dimensions were accepted");
    File.WriteAllText(Path.Combine(temporary, "MixedCase.map"), "test");
    Check(GameAssets.Resolve(temporary, "mixedcase", ".map").EndsWith("MixedCase.map", StringComparison.OrdinalIgnoreCase), "Original Windows asset names failed case-insensitive lookup");

    var root = Environment.GetEnvironmentVariable("MIR2_DATA_ROOT");
    if (root != null)
    {
        var assets = new GameAssets(Path.Combine(root, "assets"));
        Check(assets.Frame("CArmour/00", 0).Length > 8, "Real player sprite failed to decode");
        Check(assets.Frames("Monster/000") != null, "Real animation frames failed to decode");
        Check(File.Exists(assets.MapPath("0")), "Real map failed to decode");
        var catalogPaths = new ProjectPaths(); var realCatalog = new AssetCatalog(catalogPaths);
        var terrain = await new MapThumbnails(realCatalog, catalogPaths).Get("0", 0, realCatalog.Version, CancellationToken.None);
        var referenceMap = new Client.MirObjects.MapReader(assets.MapPath("0")); var mask = Convert.FromBase64String(terrain.Walkable);
        Check(terrain.Width == referenceMap.Width && terrain.Height == referenceMap.Height && mask.Length == (terrain.Width * terrain.Height + 7) / 8, "Map thumbnail dimensions/mask differ from native map");
        Check(Enumerable.Range(0, terrain.Width * terrain.Height).All(i => ((mask[i / 8] & (1 << (i % 8))) != 0) ==
            ((referenceMap.MapCells[i % terrain.Width, i / terrain.Width].BackImage & 0x20000000) == 0 && (referenceMap.MapCells[i % terrain.Width, i / terrain.Width].FrontImage & 0x8000) == 0)), "Thumbnail collision mask differs from every native tile");
        Check(terrain.Png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && terrain.Png.Length > 10000, "Terrain thumbnail is empty or invalid PNG");
        Check(Client.MirObjects.MapTileLibraries.Resolve(0) == "Map/WemadeMir2/Tiles" && Client.MirObjects.MapTileLibraries.Resolve(-1) == "", "Shared map library mapping changed");
    }
    Console.WriteLine($"Passed {checks} macOS compatibility checks.");
}
finally { Directory.Delete(temporary, recursive: true); }
