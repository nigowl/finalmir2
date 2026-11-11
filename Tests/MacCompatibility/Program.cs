// macOS compatibility and persistence checks, 2026-11-11. GPL-3.0-only.
using System.Text.Json;
using Crystal.Native;
using Microsoft.Data.Sqlite;
using Server.ConsoleHost;
using C = ClientPackets;
using S = ServerPackets;

if (args.Contains("--fixture"))
{
    Fixture.Create(args[Array.IndexOf(args, "--fixture") + 1]);
    return;
}

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
    var path = Path.Combine(temporary, "mir2.sqlite3");
    var source = Path.Combine(temporary, "seed");
    Directory.CreateDirectory(Path.Combine(source, "Guilds"));
    File.WriteAllBytes(Path.Combine(source, "Server.MirDB"), new byte[] { 1, 2, 3 });
    File.WriteAllBytes(Path.Combine(source, "Guilds", "0.mgd"), new byte[] { 8 });
    using (var storage = new SqliteDatabaseStorage(path))
    {
        storage.Import(source);
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
    }
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
        var assets = new GameAssets(Path.Combine(root, "Crystal Client"));
        Check(assets.Frame("CArmour/00", 0).Length > 8, "Real player sprite failed to decode");
        Check(assets.Frames("Monster/000") != null, "Real animation frames failed to decode");
        Check(File.Exists(assets.MapPath("0")), "Real map failed to decode");
    }
    Console.WriteLine($"Passed {checks} macOS compatibility checks.");
}
finally { Directory.Delete(temporary, recursive: true); }
