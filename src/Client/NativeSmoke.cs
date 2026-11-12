using Godot;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

// Opt-in integration driver runs the actual Godot scene and original TCP connection.
public partial class NativeSmoke : Node
{
    public Main Client = null!;
    private readonly List<Packet> packets = new();
    private readonly HashSet<int> effectsPlayed = new();
    private string stage = "connection";
    private GameState State => Client.State;
    private static string Argument(string name) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(name + "="))?[(name.Length + 1)..] ?? "";

    public override async void _Ready()
    {
        Client.PacketReceived += packet => packets.Add(packet);
        Client.EffectPlayed += id => effectsPlayed.Add(id);
        try
        {
            var verify = OS.GetCmdlineUserArgs().Contains("--verify-save");
            var reference = OS.GetCmdlineUserArgs().Contains("--reference-world");
            var account = reference ? "View" + (Environment.TickCount64 % 10000000) : "NativeSmoke";
            var name = reference ? account : "NativeWarrior";
            var record = Argument("--smoke-record");
            if (record.Length == 0 && !reference) throw new InvalidOperationException("--smoke-record is required.");
            await Until(() => Client.HandshakeReady);
            await VerifyLoginPresentation();
            if (reference && Argument("--screenshot") is { Length: > 0 } loginShot)
            {
                await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                Capture(Path.ChangeExtension(loginShot, "login.png"));
            }
            stage = "login";
            if (verify) Client.Login(account, "Native123");
            else Client.Register(account, "Native123");
            await Packet<S.LoginSuccess>();
            if (!verify && !reference)
            {
                stage = "native login password change";
                await ToSignal(GetTree().CreateTimer(6), SceneTreeTimer.SignalName.Timeout);
                await Client.Connect(Argument("--server")); await Until(() => Client.HandshakeReady);
                Client.ChangeLoginPassword("Native321");
                if ((await Packet<S.ChangePassword>()).Result != 6) throw new Exception("Native password change failed.");
                Client.ChangeLoginPassword("Native123");
                if ((await Packet<S.ChangePassword>()).Result != 6) throw new Exception("Native password restore failed.");
                Client.Login(account, "Native123"); await Packet<S.LoginSuccess>();
            }
            stage = "character";
            if (!verify)
            {
                Client.Send(new C.NewCharacter { Name = name, Class = MirClass.Warrior, Gender = MirGender.Male });
                await Packet<S.NewCharacterSuccess>();
            }
            if (reference && Argument("--screenshot") is { Length: > 0 } selectShot)
            {
                await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                Capture(Path.ChangeExtension(selectShot, "selection.png"));
            }
            await VerifySelection();
            Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(c => c.Name == name).Index });
            await Until(() => State.User != null && Client.World.Map != null && !Client.World.Loading);
            stage = "canonical map thumbnail";
            await Until(() => Client.MiniMap.Revision.Length == 64);
            if (Client.MiniMap.Error.Length != 0) throw new Exception(Client.MiniMap.Error);
            await Until(() => State.User!.Inventory.Any(i => i?.Info?.Type == ItemType.Weapon) || State.User!.Equipment[0] != null);
            if (OS.GetCmdlineUserArgs().Contains("--chinese-world")) await VerifyChineseWorld();
            if (verify)
            {
                stage = "restored quests and storage";
                await Until(() => State.Quests.ContainsKey(2) && State.Quests[2].QuestInfo != null && State.CompletedQuests.Contains(1));
                await Until(() => State.InGuild && State.GuildNotice.Count == 2 && State.GuildRanks.Sum(r => r.Members.Count) == 2);
                await ReadGuildStorage(open: true); Client.SetGuildStorageRow(6); await Screenshot("guild-bank-restored");
                using (var closeGuild = new InputEventKey { PhysicalKeycode = Key.H, Pressed = true }) Client._UnhandledInput(closeGuild);
                var savedLocation = State.User!.Location;
                await OpenStorage();
                Client.EnterStoragePassword("Store123");
                if ((await Packet<S.StorageUnlockResult>()).Result != 0) throw new Exception("Saved storage password was lost.");
                await Until(() => Client.CanUseStorage && State.Storage.Any(i => i != null));
                Client.SetDestination(savedLocation);
                await Until(() => State.User.Location == savedLocation);
                var expected = File.ReadAllText(record);
                if (Snapshot() != expected) throw new Exception($"SQLite restart mismatch.\nExpected {expected}\nActual {Snapshot()}");
                await Screenshot("storage");
                Client.SelectStoragePage(1); await Screenshot("storage-expansion"); Client.SelectStoragePage(0);
                GD.Print("PASS native SQLite restart: location, XP, gold, inventory/equipment/durability, spells/hotkeys, active/completed quests, quest items, account storage/password, guild membership/rank/notice/funds/storage item IDs and counts.");
            }
            else if (reference)
            {
                stage = "reference world";
                var start = State.User!.Location;
                var sword = State.User.Inventory.FirstOrDefault(i => i?.Info?.Type == ItemType.Weapon);
                if (sword != null) { Client.UseItem(sword.UniqueID); await Packet<S.EquipItem>(); }
                var step = Enumerable.Range(-2, 5).SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => new Point(start.X + dx, start.Y + dy)))
                    .First(p => p != start && Client.World.Walkable(p) && Client.World.NextStep(p) != null);
                Client.SetDestination(step);
                await Until(() => State.User.Location == step);
                GD.Print($"PASS native reference world: {State.Map!.Title}, {State.User.Location}, {State.Actors.Count} visible objects.");
            }
            else
            {
                var start = State.User!.Location;
                stage = "equipment";
                var sword = State.User.Inventory.Single(i => i?.Info?.Type == ItemType.Weapon);
                Client.UseItem(sword.UniqueID);
                var equipped = await Packet<S.EquipItem>();
                if (!equipped.Success || State.User.Equipment[0]?.UniqueID != sword.UniqueID) throw new Exception("Equip failed.");
                var armour = State.User.Inventory.Single(i => i?.Info?.Type == ItemType.Armour);
                Client.UseItem(armour.UniqueID);
                if (!(await Packet<S.EquipItem>()).Success || State.User.Equipment[(int)EquipmentSlot.Armour]?.UniqueID != armour.UniqueID)
                    throw new Exception("Armour equip failed.");
                await VerifyHud();
                await VerifyInventoryPresentation();
                await VerifySettings();
                await VerifyChatInteraction();
                await VerifyCommandFeedback();
                await VerifyItemDrag();
                stage = "learn spell";
                Client.UseItem(State.User.Inventory.First(i => i?.Info?.Type == ItemType.Book).UniqueID);
                if (!(await Packet<S.UseItem>()).Success) throw new Exception("Skill book failed.");
                await Packet<S.NewMagic>();
                stage = "potion";
                await UseQuickPotion(false);
                stage = "accept and abandon quests";
                await Until(() => State.QuestDefinitions.Count == 3 && State.Actors.Values.Any(a => a.Kind == ObjectType.Merchant));
                var questNpc = State.Actors.Values.Single(a => a.Kind == ObjectType.Merchant);
                Client.OpenQuestJournal(questNpc.Id);
                for (var id = 1; id <= 3; id++) { Client.AcceptQuest(id); await Until(() => State.Quests.ContainsKey(id)); }
                if (State.User.QuestInventory.Count(i => i != null) != 3) throw new Exception("Quest carry items were not delivered.");
                Client.AbandonQuest(3);
                await Until(() => !State.Quests.ContainsKey(3) && State.User.QuestInventory.Count(i => i != null) == 2);
                stage = "movement";
                await VerifyMapAndMovement(start);
                await VerifyPatrol();
                Client.SetDestination(new Point(start.X, start.Y - 1));
                await Until(() => State.User.Location == new Point(start.X, start.Y - 1));
                stage = "combat";
                await Until(() => State.Actors.Values.Any(a => a.Kind == ObjectType.Monster && !a.Dead));
                var target = ClientAutomation.Targets(State).First();
                EnableAutomaticBattle(false, Spell.FireBall);
                await Until(() => target.Dead && State.User.Experience > 0);
                if (!packets.OfType<S.Magic>().Any(packet => packet.Cast)) throw new Exception("The configured learned skill was not cast by automation.");
                using (var stop = new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true }) Client._UnhandledInput(stop);
                await Until(() => State.Quests[1].TaskList.Any(t => t.Contains("1/2")) && State.Quests[2].TaskList.Any(t => t.Contains("1/3")));
                stage = "spell combat and level";
                await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                var magicTarget = ClientAutomation.Targets(State).First();
                packets.RemoveAll(packet => packet is S.Magic);
                Client.SetDestination(State.User.Location); Client.World.Selected = magicTarget.Id;
                Client.BindMagic(0, 1);
                using (var skillKey = new InputEventKey { PhysicalKeycode = Key.F1, Pressed = true }) Client._UnhandledInput(skillKey);
                if (!(await Packet<S.Magic>()).Cast) throw new Exception("Fireball cast was rejected.");
                await Until(() => magicTarget.Dead && State.User.Level > 1);
                await VerifyAutomaticManaPotion();
                await Until(() => State.Quests[1].Completed && State.Quests[2].TaskList.Any(t => t.Contains("2/3")));
                stage = "loot";
                await Until(() => State.Actors.Values.Any(a => a.Kind == ObjectType.Item));
                while (State.Actors.Values.FirstOrDefault(a => a.Kind == ObjectType.Item) is { } loot)
                {
                    Client.SelectTarget(loot.Id);
                    await Until(() => !State.Actors.ContainsKey(loot.Id));
                }
                if (State.User.Gold < 100 || !State.User.Inventory.Any(i => i?.Info?.Type == ItemType.Potion)) throw new Exception("Loot was not received.");
                stage = "shop";
                var npc = State.Actors.Values.Single(a => a.Kind == ObjectType.Merchant);
                Client.SelectTarget(npc.Id);
                await Packet<S.NPCResponse>();
                stage = "quest reward selection";
                Client.OpenQuestJournal(npc.Id);
                await Screenshot("quest");
                Client.OpenQuestJournal(); await Screenshot("journal");
                Client.OpenQuestJournal(npc.Id);
                var questGold = State.User.Gold; var questExperience = State.User.Experience;
                Client.FinishQuest(1); // Client must refuse to silently discard a selectable reward.
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!State.Quests.ContainsKey(1)) throw new Exception("Quest finished without an explicit reward selection.");
                Client.FinishQuest(1, 1);
                await Until(() => State.CompletedQuests.Contains(1) && !State.Quests.ContainsKey(1) && State.User.Inventory.Any(i => i?.Info?.Name == "NativeChoice1"));
                await Until(() => State.User.Gold == questGold + 77 && State.User.Experience == questExperience + 5 && State.User.QuestInventory.Count(i => i != null) == 1);
                if (State.User.Inventory.Any(i => i?.Info?.Name == "NativeChoice0")) throw new Exception("Wrong quest choice was granted.");
                stage = "shop";
                // 交任务会调用默认 NPC；重新点击商人，按真实界面流程恢复当前对话目标。
                packets.RemoveAll(packet => packet is S.NPCResponse);
                Client.SelectTarget(npc.Id); await Packet<S.NPCResponse>();
                Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@BUY]" });
                var goods = await Packet<S.NPCGoods>();
                await VerifyNpcGoods(goods);
                var count = State.User.Inventory.Where(i => i?.Info?.Type == ItemType.Potion).Sum(i => i.Count);
                var beforeGold = State.User.Gold;
                Client.Send(new C.BuyItem { ItemIndex = goods.List[0].UniqueID, Count = 1, Type = goods.Type });
                await Until(() => State.User.Inventory.Where(i => i?.Info?.Type == ItemType.Potion).Sum(i => i.Count) == count + 1 && State.User.Gold < beforeGold);
                await Until(() => packets.OfType<S.Chat>().Any(packet => packet.Message.StartsWith("购买成功")));
                Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" });
                await Packet<S.NPCResponse>();
                await UseQuickPotion(true);
                Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@SELL]" });
                await Packet<S.NPCSell>();
                var sell = State.User.Inventory.First(i => i?.Info?.Type == ItemType.Potion && i.Info.Stats[Stat.HP] >= 0);
                Client.Send(new C.SellItem { UniqueID = sell.UniqueID, Count = 1 });
                if (!(await Packet<S.SellItem>()).Success) throw new Exception("Sell failed.");
                stage = "inventory";
                var remaining = State.User.Inventory.First(i => i?.Info?.Type == ItemType.Potion && i.Info.Stats[Stat.HP] >= 0);
                var from = Array.FindIndex(State.User.Inventory, i => i?.UniqueID == remaining.UniqueID);
                Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = from, To = 7 });
                var moved = await Packet<S.MoveItem>();
                if (!moved.Success || State.User.Inventory[7]?.UniqueID != remaining.UniqueID)
                    throw new Exception($"Bag move failed from {from}, reply {moved.From}->{moved.To}, success {moved.Success}. {Snapshot()}");
                stage = "storage setup and transfers";
                await OpenStorage();
                if (!State.User.RequireStoragePassword || State.StorageUnlocked) throw new Exception("Storage bypassed mandatory password setup.");
                await Screenshot("storage-setup");
                Client.EnterStoragePassword("Store123", confirmation: "Store123");
                if ((await Packet<S.StoragePasswordResult>()).Result != 4) throw new Exception("Storage password setup failed.");
                await Until(() => Client.CanUseStorage);
                var potionSlot = Array.FindIndex(State.User.Inventory, i => i?.UniqueID == remaining.UniqueID);
                var reward = State.User.Inventory.Single(i => i?.Info?.Name == "NativeChoice1");
                var rewardSlot = Array.FindIndex(State.User.Inventory, i => i?.UniqueID == reward.UniqueID);
                Client.StoreInventory(potionSlot, 0);
                if (!(await Packet<S.StoreItem>()).Success || State.Storage[0]?.UniqueID != remaining.UniqueID) throw new Exception("Deposit failed.");
                Client.StoreInventory(rewardSlot, 1);
                if (!(await Packet<S.StoreItem>()).Success) throw new Exception("Quest reward deposit failed.");
                Client.MoveStorage(0, 1);
                var storageMove = await Packet<S.MoveItem>();
                if (!storageMove.Success || State.Storage[0]?.UniqueID != reward.UniqueID || State.Storage[1]?.UniqueID != remaining.UniqueID) throw new Exception("Storage swap failed.");
                Client.TakeStorage(1, potionSlot);
                if (!(await Packet<S.TakeBackItem>()).Success || State.User.Inventory[potionSlot]?.UniqueID != remaining.UniqueID) throw new Exception("Withdrawal failed.");
                await Screenshot("storage");
                stage = "storage page and password rejection";
                Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" });
                await Until(() => !State.StorageActive);
                Client.Send(new C.TakeBackItem { From = 0, To = rewardSlot });
                if ((await Packet<S.TakeBackItem>()).Success || State.Storage[0]?.UniqueID != reward.UniqueID || State.User.Inventory[rewardSlot] != null) throw new Exception("Closed storage page allowed a withdrawal.");
                await OpenStorage();
                Client.EnterStoragePassword("Wrong123");
                if ((await Packet<S.StorageUnlockResult>()).Result != 2 || State.StorageUnlocked || State.Storage.Length != 0) throw new Exception("Incorrect password exposed storage.");
                await Screenshot("storage-locked");
                Client.EnterStoragePassword("Store123");
                if ((await Packet<S.StorageUnlockResult>()).Result != 0) throw new Exception("Correct storage password rejected.");
                await Until(() => Client.CanUseStorage && State.Storage.Any(i => i?.UniqueID == reward.UniqueID));
                stage = "storage password change";
                Client.ChangeStoragePassword(); Client.EnterStoragePassword("Wrong123", "Store456", "Store456");
                if ((await Packet<S.StoragePasswordResult>()).Result != 2) throw new Exception("Incorrect current storage password was accepted.");
                Client.EnterStoragePassword("Store123", "Store456", "Store456");
                if ((await Packet<S.StoragePasswordResult>()).Result != 4) throw new Exception("Storage password change failed.");
                await OpenStorage(); Client.EnterStoragePassword("Store123");
                if ((await Packet<S.StorageUnlockResult>()).Result != 2) throw new Exception("Old storage password still worked.");
                Client.EnterStoragePassword("Store456");
                if ((await Packet<S.StorageUnlockResult>()).Result != 0) throw new Exception("Changed storage password failed.");
                await Until(() => State.Storage.Any(i => i?.UniqueID == reward.UniqueID));
                Client.ChangeStoragePassword(); Client.EnterStoragePassword("Store456", "Store123", "Store123");
                if ((await Packet<S.StoragePasswordResult>()).Result != 4) throw new Exception("Storage password restoration failed.");
                stage = "map transition";
                Client.SetDestination(new Point(start.X + 4, start.Y));
                await Until(() => State.Map?.MapIndex == 2 && !Client.World.Loading);
                stage = "death and revival";
                await Until(() => State.Actors.Values.Any(a => a.Kind == ObjectType.Monster && !a.Dead));
                Client.SelectTarget(State.Actors.Values.Single(a => a.Kind == ObjectType.Monster && !a.Dead).Id);
                await Until(() => State.Me!.Dead);
                Client.Send(new C.TownRevive());
                await Until(() => State.Map?.MapIndex == 1 && !State.Me!.Dead && State.User.HP > 0 && !Client.World.Loading);
                stage = "chat";
                Client.Send(new C.Chat { Message = "Native Godot integration" });
                await Packet<S.ObjectChat>();
                // Decode real art through the same cache used by the renderer, even headless.
                await VerifyGroup();
                await VerifyNpcRepair();
                await VerifyAutomaticRoam();
                await VerifyAutomaticEscape(start);
                await VerifyTerrainCache();
                stage = "native guild bank durable item";
                Client.SetDestination(start); await Until(() => State.User!.Location == start);
                await ReadGuildStorage(open: true);
                var bankItem = State.User!.Inventory.First(i => i?.Info?.Type == ItemType.Weapon)!;
                Client.StoreGuildItem(Array.IndexOf(State.User.Inventory, bankItem), 111);
                await Until(() => State.GuildStorage[111]?.Item.UniqueID == bankItem.UniqueID);
                Client.SetGuildStorageRow(6); await Screenshot("guild-bank-saved");
                using (var closeGuild = new InputEventKey { PhysicalKeycode = Key.H, Pressed = true }) Client._UnhandledInput(closeGuild);
                stage = "original resources";
                await Until(() => Client.Assets.Sprite("CArmour/00", 0) != null && Client.Assets.Sprite("Prguse", 1) != null);
                stage = "durability acknowledgement";
                // Original HumanObject batches durability updates on a ten-second timer.
                await ToSignal(GetTree().CreateTimer(10.5), SceneTreeTimer.SignalName.Timeout);
                File.WriteAllText(record, Snapshot());
                stage = "same-connection character switch";
                await VerifyCharacterSwitch();
                if (Snapshot() != File.ReadAllText(record)) throw new Exception("Character switch changed the original character or shared storage.");
                GD.Print("PASS native gameplay: register/login/change-password/create, equip, potion, walk/run, melee, learn/cast Fireball, XP/level, loot, quest accept/progress/choice/finish/abandon, storage setup/unlock/change-password/rejection/store/swap/take, shared storage across characters, buy/sell, bag, map transition, death/revive, chat, original sprites.");
            }
            if (Argument("--screenshot") is { Length: > 0 } screenshot)
            {
                await ToSignal(GetTree().CreateTimer(5), SceneTreeTimer.SignalName.Timeout);
                Capture(screenshot);
                using var bag = new InputEventKey { PhysicalKeycode = Key.I, Pressed = true };
                Client._UnhandledInput(bag);
                await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                Capture(Path.ChangeExtension(screenshot, "bag.png"));
                Client._UnhandledInput(bag);
                using var character = new InputEventKey { PhysicalKeycode = Key.C, Pressed = true };
                Client._UnhandledInput(character);
                await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
                Capture(Path.ChangeExtension(screenshot, "equipment.png"));
            }
            if (OS.GetCmdlineUserArgs().Contains("--stay")) return;
            await Client.Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr($"FAIL native smoke at {stage}: {error}\nRecent packets: {string.Join(", ", packets.TakeLast(15).Select(p => p.GetType().Name))}");
            GD.PrintErr($"World: {State.Map?.Title}; user {State.User?.Location}, HP {State.User?.HP}; actors {string.Join("; ", State.Actors.Values.Select(a => $"{a.Name}@{a.Location}, health={a.Health}, dead={a.Dead}"))}");
            GetTree().Quit(1);
        }
    }

    private async Task VerifyCharacterSwitch()
    {
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        Client.Send(new C.NewCharacter { Name = "NativeAlternate", Class = MirClass.Wizard, Gender = MirGender.Female });
        await Packet<S.NewCharacterSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(c => c.Name == "NativeAlternate").Index });
        await Until(() => State.User?.Name == "NativeAlternate" && !Client.World.Loading && State.Actors.Values.Any(a => a.Kind == ObjectType.Merchant));
        if (State.Quests.Count != 0 || State.CompletedQuests.Count != 0 || State.User!.QuestInventory.Any(i => i != null) || State.InGuild || State.GuildNotice.Count != 0) throw new Exception("Character-specific quests or guild leaked between characters.");
        await OpenStorage(); Client.EnterStoragePassword("Store123");
        if ((await Packet<S.StorageUnlockResult>()).Result != 0) throw new Exception("Account storage password was not shared.");
        await Until(() => State.Storage.Any(i => i?.Info?.Name == "NativeChoice1"));
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(c => c.Name == "NativeWarrior").Index });
        await Until(() => State.User?.Name == "NativeWarrior" && !Client.World.Loading && State.Quests.ContainsKey(2) && State.CompletedQuests.Contains(1) && State.Actors.Values.Any(a => a.Kind == ObjectType.Merchant));
        await Until(() => State.InGuild && State.GuildNotice.Count == 2);
        await ReadGuildStorage();
        if (State.Quests[2].QuestInfo == null || State.User!.Equipment[0]?.Info == null || State.User.QuestInventory.Any(i => i != null && i.Info == null)) throw new Exception("Same-connection metadata was lost on character switching.");
        await OpenStorage(); Client.EnterStoragePassword("Store123");
        if ((await Packet<S.StorageUnlockResult>()).Result != 0) throw new Exception("Original character storage failed to reopen.");
        await Until(() => State.Storage.Any(i => i?.Info?.Name == "NativeChoice1"));
        using var close = new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true }; Client._UnhandledInput(close);
        using var bag = new InputEventKey { PhysicalKeycode = Key.I, Pressed = true }; Client._UnhandledInput(bag);
    }
    private async Task OpenStorage()
    {
        var npc = State.Actors.Values.Single(a => a.Kind == ObjectType.Merchant);
        packets.RemoveAll(p => p is S.NPCResponse or S.NPCStorage);
        Client.SelectTarget(npc.Id);
        await Packet<S.NPCResponse>();
        Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@STORAGE]" });
        await Packet<S.NPCStorage>();
        await Until(() => State.StorageActive);
    }
    private async Task Screenshot(string suffix)
    {
        if (Argument("--screenshot") is not { Length: > 0 } screenshot) return;
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        Capture(Path.ChangeExtension(screenshot, suffix + ".png"));
    }
    private async Task Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 45000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException(stage);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private void Capture(string path) { using var image = GetViewport().GetTexture().GetImage(); image.SavePng(path); }
    private async Task<T> Packet<T>() where T : Packet
    {
        await Until(() => packets.Any(p => p is T));
        var packet = packets.OfType<T>().First(); packets.Remove(packet); return packet;
    }
    private string Snapshot()
    {
        var user = State.User!;
        object? Item(UserItem? item) => item == null ? null : new { item.UniqueID, item.ItemIndex, item.Count, item.CurrentDura, name = item.Info?.Name };
        return JsonSerializer.Serialize(new { map = State.Map!.MapIndex, user.Location.X, user.Location.Y, user.Level, user.Experience, user.Gold,
            user.GuildName, user.GuildRank, guildNotice = State.GuildNotice.ToArray(),
            guildGold = State.Guild?.Gold, guildStorage = State.GuildStorage.Select(entry => entry == null ? null : new { entry.UserId, item = Item(entry.Item) }),
            inventory = user.Inventory.Select(Item), equipment = user.Equipment.Select(Item), spells = user.Magics.Select(m => new { m.Spell, m.Key, m.Level, m.Experience }),
            user.HasStoragePassword, storage = State.Storage.Select(Item), questItems = user.QuestInventory.Select(Item),
            quests = State.Quests.Values.OrderBy(q => q.Id).Select(q => new { q.Id, q.Taken, q.Completed, q.TaskList }), completed = State.CompletedQuests.Order() });
    }
}
