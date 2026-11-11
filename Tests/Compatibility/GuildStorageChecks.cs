using System.Runtime.CompilerServices;
using Crystal.Native;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirNetwork;
using Server.MirObjects;
using S = ServerPackets;

internal static class GuildStorageChecks
{
    public static void Run(Action<bool, string> check, string temporary)
    {
        var world = Envir.Main; var definition = new ItemInfo { Index = 986001, Name = "BankTest", Type = ItemType.Ring, StackSize = 1 };
        BankPlayer Player(int id)
        {
            var account = new AccountInfo { Index = id, AccountID = "Bank" + id, Gold = 1000 };
            var player = new BankPlayer { Info = new CharacterInfo { Index = id, Name = "Bank" + id, AccountInfo = account, CreationIP = "", Heroes = new HeroInfo[1] }, Account = account, InSafeZone = true, Stats = new Stats { [Stat.BagWeight] = int.MaxValue } };
            player.Connection = (MirConnection)RuntimeHelpers.GetUninitializedObject(typeof(MirConnection)); player.Connection.SentItemInfo = new() { definition }; player.Connection.Observers = new();
            account.Characters.Add(player.Info); return player;
        }
        var owner = Player(91001); var peer = Player(91002);
        var guild = new GuildObject(new GuildInfo(owner, "BankChecks") { GuildIndex = 91000 });
        var members = new GuildRank { Index = 1, Name = "Members", Options = GuildRankOptions.CanStoreItem | GuildRankOptions.CanRetrieveItem };
        members.Members.Add(new GuildMember { Name = peer.Name, Id = peer.Info.Index, Player = peer }); guild.Ranks.Add(members);
        owner.MyGuild = peer.MyGuild = guild; owner.MyGuildRank = guild.Ranks[0]; peer.MyGuildRank = members;
        owner.Info.GuildIndex = peer.Info.GuildIndex = guild.Guildindex; guild.Info.Membercount = 2;
        UserItem Item(ulong id) => new(definition) { UniqueID = id, Count = 1 };
        try
        {
            owner.GuildStorageGoldChange(0, 30); owner.GuildStorageGoldChange(1, 10);
            check(owner.Account.Gold == 980 && guild.Gold == 20 && owner.Packets.OfType<S.GuildStatus>().Last().Gold == 20, "Guild funds failed donation/withdrawal conservation or status acknowledgement");
            peer.GuildStorageGoldChange(1, 1); owner.GuildStorageGoldChange(2, 1); owner.GuildStorageGoldChange(255, 1);
            check(peer.Account.Gold == 1000 && guild.Gold == 20 && owner.Account.Gold == 980, "Non-leader or unknown fund action withdrew gold");
            owner.InSafeZone = false; owner.GuildStorageGoldChange(0, 1); owner.InSafeZone = true;
            owner.Dead = true; owner.GuildStorageGoldChange(1, 1); owner.Dead = false;
            check(owner.Account.Gold == 980 && guild.Gold == 20, "Unsafe or dead player changed guild funds");
            guild.Gold = uint.MaxValue; owner.GuildStorageGoldChange(0, 1);
            check(guild.Gold == uint.MaxValue && owner.Account.Gold == 980, "Guild fund deposit overflowed or consumed gold");
            owner.Account.Gold = uint.MaxValue; owner.GuildStorageGoldChange(1, 1);
            check(owner.Account.Gold == uint.MaxValue && guild.Gold == uint.MaxValue, "Guild withdrawal overflowed recipient funds");
            owner.Account.Gold = 980; guild.Gold = 20;
            var item = Item(600001); owner.Info.Inventory[6] = item;
            owner.GuildStorageItemChange(0, 6, 0);
            check(owner.Info.Inventory[6] == null && guild.StoredItems[0].Item == item && guild.StoredItems[0].UserId == owner.Info.Index, "Guild deposit did not retain exactly one item and depositor ID");
            owner.GuildCanRequestItems = true; owner.GuildStorageItemChange(3, 0, 0); var snapshot = owner.Packets.OfType<S.GuildStorageList>().Last();
            owner.GuildStorageItemChange(2, 0, 0);
            check(guild.StoredItems[0]?.Item == item && owner.Packets.OfType<S.GuildStorageItemChange>().Last().Type == 5, "Same-cell guild move deleted its source");
            members.Options = 0; peer.GuildStorageItemChange(1, 0, 6);
            check(guild.StoredItems[0]?.Item == item && peer.Info.Inventory[6] == null && peer.Packets.OfType<S.GuildStorageItemChange>().Last().Type == 4, "Denied retrieval changed assets or omitted the failure acknowledgement");
            members.Options = GuildRankOptions.CanStoreItem | GuildRankOptions.CanRetrieveItem;
            peer.GuildStorageItemChange(1, 0, 6); owner.GuildStorageItemChange(1, 0, 6);
            check(peer.Info.Inventory[6] == item && owner.Info.Inventory[6] == null && guild.StoredItems[0] == null, "Competing retrieval duplicated the same guild item");
            check(snapshot.Items[0]?.Item.UniqueID == item.UniqueID, "Pending full bank snapshot mutated after a later withdrawal");
            foreach (var flag in new[] { false, true })
            {
                definition.Bind = flag ? 0 : BindMode.DontStore;
                item.RentalInformation = flag ? new RentalInformation { BindingFlags = BindMode.DontStore } : null;
                peer.GuildStorageItemChange(0, 6, 0);
                check(peer.Info.Inventory[6] == item && guild.StoredItems[0] == null, "Storage restriction was bypassed through the guild bank");
            }
            definition.Bind = 0; item.RentalInformation = null;
            peer.InSafeZone = false; peer.GuildStorageItemChange(0, 6, 0); peer.InSafeZone = true;
            peer.Dead = true; peer.GuildStorageItemChange(0, 6, 0); peer.Dead = false;
            check(peer.Info.Inventory[6] == item && guild.StoredItems[0] == null, "Unsafe/dead actor deposited a bank item");
            for (var i = 0; i < 3; i++) foreach (var invalid in new[] { -1, int.MaxValue })
            {
                peer.GuildStorageItemChange((byte)i, invalid, 0); peer.GuildStorageItemChange((byte)i, 0, invalid);
                check(peer.Info.Inventory[6] == item && guild.StoredItems[0] == null, "Malformed bank slot changed inventory");
            }
            peer.GuildStorageItemChange(0, 6, 0); guild.StoredItems[1] = new GuildStorageItem { Item = Item(600002), UserId = owner.Info.Index };
            peer.GuildStorageItemChange(2, 0, 1);
            check(guild.StoredItems[1]?.Item == item && guild.StoredItems[0]?.Item.UniqueID == 600002, "Guild swap lost the occupied destination");
            var native = new GameState(); native.Apply(new S.NewItemInfo { Info = definition }); native.Apply(new S.UserInformation { RealId = (uint)owner.Info.Index, ObjectID = 1234, Inventory = new UserItem[46], Equipment = new UserItem[14], QuestInventory = [] });
            native.Apply(new S.GuildStatus { GuildName = "BankChecks", Gold = 20, MyRankId = 0 }); native.Apply(new S.GuildStorageList { Items = new GuildStorageItem[112] });
            native.User!.Inventory[6] = Item(700001);
            native.Apply(new S.GuildStorageItemChange { Type = 0, User = owner.Info.Index, From = 6, To = 0, Item = new GuildStorageItem { Item = Item(700001), UserId = owner.Info.Index } });
            check(native.GuildStorageLoaded && native.User.Inventory[6] == null && native.GuildStorage[0].Item.Info == definition, "Native bank confused character RealId with scene ObjectID or lost metadata");
            native.Apply(new S.GuildStorageItemChange { Type = 0, User = peer.Info.Index, From = 7, To = 1, Item = new GuildStorageItem { Item = Item(700002), UserId = peer.Info.Index } });
            native.Apply(new S.GuildStorageItemChange { Type = 2, User = peer.Info.Index, From = 0, To = 1, Item = new GuildStorageItem { Item = Item(700001), UserId = owner.Info.Index } });
            native.Apply(new S.GuildStorageItemChange { Type = 1, User = owner.Info.Index, From = 1, To = 6 });
            check(native.GuildStorage[1] == null && native.GuildStorage[0].Item.UniqueID == 700002 && native.User.Inventory[6]?.UniqueID == 700001, "Native bank swap/withdrawal acknowledgements diverged");
            native.Apply(new S.GuildStorageGoldChange { Type = 0, Amount = 8 }); native.Apply(new S.GuildStorageGoldChange { Type = 1, Amount = 3 });
            check(native.Guild!.Gold == 25, "Native guild fund deltas were not applied exactly once");
            native.Apply(new S.GuildStatus()); check(!native.GuildStorageLoaded && native.GuildStorage.All(i => i == null), "Leaving guild retained bank entries");
            CheckAtomicSave(check, temporary, owner, peer, guild);
        }
        finally { world.Guilds.Remove(guild); }
    }
    private static void CheckAtomicSave(Action<bool, string> check, string temporary, BankPlayer owner, BankPlayer peer, GuildObject guild)
    {
        var world = Envir.Main; var accounts = world.AccountList.ToArray(); var guilds = world.GuildList.ToArray(); var previous = DatabaseStorage.Current; var cwd = Directory.GetCurrentDirectory(); var folder = Path.Combine(temporary, "bank-persistence"); Directory.CreateDirectory(folder);
        try
        {
            Directory.SetCurrentDirectory(folder); world.AccountList.Clear(); world.AccountList.AddRange(new[] { owner.Account, peer.Account }); world.GuildList.Clear(); world.GuildList.Add(guild.Info);
            using var storage = new SqliteDatabaseStorage(Path.Combine(folder, "bank.sqlite3")); DatabaseStorage.Current = storage;
            world.SaveAccounts();
            byte[] Read(string name) { using var value = storage.Read(name); using var memory = new MemoryStream(); value.CopyTo(memory); return memory.ToArray(); }
            var guildPath = Path.Combine(Settings.GuildPath, "0.mgd"); var beforeAccount = Read(Envir.AccountPath); var beforeGuild = Read(guildPath);
            owner.GuildStorageGoldChange(0, 5); owner.GuildCanRequestItems = true;
            var failed = new FailingStorage(storage) { WritesBeforeFailure = 2 }; DatabaseStorage.Current = failed;
            try { world.SaveAccounts(); throw new Exception("Injected guild write failure was swallowed"); } catch (IOException) { }
            check(Read(Envir.AccountPath).SequenceEqual(beforeAccount) && Read(guildPath).SequenceEqual(beforeGuild), "Failed guild write committed only the account side of a transfer");
            check(guild.NeedSave && world.GuildRefreshNeeded, "Failed guild transaction discarded dirty/reindex flags");
            DatabaseStorage.Current = storage; world.SaveAccounts();
            check(!Read(Envir.AccountPath).SequenceEqual(beforeAccount) && !Read(guildPath).SequenceEqual(beforeGuild), "Retry did not commit both account and guild transfer snapshots");
        }
        finally { DatabaseStorage.Current = previous; Directory.SetCurrentDirectory(cwd); world.AccountList.Clear(); world.AccountList.AddRange(accounts); world.GuildList.Clear(); world.GuildList.AddRange(guilds); world.GuildRefreshNeeded = false; }
    }
    private sealed class BankPlayer : PlayerObject
    {
        public readonly List<Packet> Packets = new();
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void ReceiveChat(string text, ChatType type) { }
    }
}
