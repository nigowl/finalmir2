using System.Runtime.CompilerServices;
using Crystal.Native;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using Server.MirNetwork;
using S = ServerPackets;

internal static class TradeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var world = Envir.Main;
        var definition = new ItemInfo { Index = 987654, Name = "Trade regression", Type = ItemType.Ring, StackSize = 1 };
        world.ItemInfoList.Add(definition);
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map)); map.Info = new MapInfo();
        var nodes = new LinkedList<MapObject>();
        TradePlayer Player(string name, int y, MirDirection direction)
        {
            var account = new AccountInfo { Gold = 1000 };
            var p = new TradePlayer { Info = new CharacterInfo { Index = y + 100, Name = name, CreationIP = "", Heroes = new HeroInfo[1], HP = 100, AccountInfo = account, AllowTrade = true, CurrentLocation = new(0, y), Direction = direction }, Account = account, CurrentMap = map, Stats = new Stats { [Stat.BagWeight] = int.MaxValue } };
            p.Connection = (MirConnection)RuntimeHelpers.GetUninitializedObject(typeof(MirConnection));
            p.Connection.SentItemInfo = new() { definition };
            p.Connection.Observers = new();
            p.Report = new Reporting(p); p.Node = nodes.AddLast(p); return p;
        }
        UserItem Item(ulong id) => new(definition) { UniqueID = id, Count = 1 };
        var owner = Player("Owner", 0, MirDirection.Down); var peer = Player("Peer", 1, MirDirection.Up);
        void Session() { owner.TradePartner = peer; peer.TradePartner = owner; }
        try
        {
            var item = Item(100001); owner.Info.Inventory[6] = item;
            owner.DepositTradeItem(6, 0);
            check(owner.Info.Inventory[6] == item && owner.Info.Trade.All(i => i == null) && owner.Packets.OfType<S.DepositTradeItem>().Last().Success == false, "Trade item was escrowed without a mutual live session");
            foreach (var invalid in new Action[] { () => peer.Dead = true, () => peer.Node = null!, () => peer.CurrentLocation = new(0, 3), () => owner.AllowTrade = false })
            {
                peer.Dead = false; peer.Node ??= nodes.AddLast(peer); peer.CurrentLocation = new(0, 1); owner.AllowTrade = true;
                owner.TradeInvitation = peer; invalid(); owner.TradeReply(true);
                check(owner.TradePartner == null && peer.TradePartner == null && owner.TradeInvitation == null, "A stale or disabled invitation opened a trade");
            }
            owner.AllowTrade = true; owner.TradeInvitation = peer; owner.TradeReply(true);
            check(owner.TradePartner == peer && peer.TradePartner == owner && owner.Packets.OfType<S.TradeAccept>().Last().Name == peer.Name, "Valid original invitation did not establish both trade partners");
            owner.TradeLocked = peer.TradeLocked = true;
            owner.DepositTradeItem(6, 0);
            check(owner.Info.Trade[0] == item && owner.Info.Inventory[6] == null && !owner.TradeLocked && !peer.TradeLocked && peer.Packets.OfType<S.TradeCancel>().Last().Unlock, "Offer change did not move exactly one item and revoke both confirmations");
            var offered = peer.Packets.OfType<S.TradeItem>().Last();
            owner.RetrieveTradeItem(0, 6);
            check(offered.TradeItems[0]?.UniqueID == item.UniqueID && owner.Info.Trade[0] == null, "Queued offer snapshot changed after escrow withdrawal");
            owner.DepositTradeItem(6, 0); owner.TradeGold(25);
            check(owner.Info.TradeGold == 25 && owner.Account.Gold == 975 && peer.Packets.OfType<S.TradeGold>().Last().Amount == 25, "Trade gold did not use durable incremental escrow");
            peer.TradeGold(10); owner.TradeConfirm(true); peer.TradeConfirm(true);
            check(peer.Info.Inventory.Any(i => i?.UniqueID == item.UniqueID) && owner.Info.Inventory.All(i => i?.UniqueID != item.UniqueID) && owner.Account.Gold == 985 && peer.Account.Gold == 1015 && owner.Info.TradeGold == 0 && peer.Info.TradeGold == 0 && owner.TradePartner == null && peer.TradePartner == null, "Successful exchange failed item uniqueness or gold conservation");
            peer.TradeConfirm(true); owner.TradeConfirm(true);
            check(owner.Account.Gold + peer.Account.Gold == 2000 && peer.Info.Inventory.Count(i => i?.UniqueID == item.UniqueID) == 1, "Repeated confirmation duplicated completed trade assets");
            Session(); owner.TradeGold(15); owner.Account.Gold = uint.MaxValue - 5;
            world.CharacterList.Add(owner.Info); owner.TradeCancel();
            check(owner.Account.Gold == uint.MaxValue && owner.Info.Mail.Single().Gold == 10 && owner.Info.TradeGold == 0, "Cancellation destroyed the part of a gold refund above the balance cap");
            owner.Info.Mail.Clear(); owner.Account.Gold = 100;
            owner.Info.Trade[0] = Item(100002); owner.Info.TradeGold = 9;
            owner.TradeCancel();
            check(owner.Info.Inventory.Any(i => i?.UniqueID == 100002) && owner.Account.Gold == 109 && owner.Info.Trade.All(i => i == null), "Orphaned escrow was not returned without a partner");

            var source = new CharacterInfo { Index = 990, Name = "Interrupted", CreationIP = "", Heroes = new HeroInfo[1], TradeGold = 20 };
            source.Trade[0] = Item(100003);
            using var saved = new MemoryStream();
            using (var writer = new BinaryWriter(saved, System.Text.Encoding.UTF8, true)) source.Save(writer);
            saved.Position = 0; using var reader = new BinaryReader(saved, System.Text.Encoding.UTF8, true);
            var loaded = new CharacterInfo(reader, Envir.Version, Envir.CustomVersion) { AccountInfo = new AccountInfo { Gold = 80 } };
            check(loaded.Trade[0]?.UniqueID == 100003 && loaded.TradeGold == 20 && saved.Position == saved.Length, "Snapshot did not retain interrupted trade escrow");
            world.CharacterList.Add(loaded);
            check(world.RecoverTrades() && loaded.AccountInfo.Gold == 100 && loaded.TradeGold == 0 && loaded.Inventory.Count(i => i?.UniqueID == 100003) == 1 && loaded.Trade.All(i => i == null), "Restart did not recover escrow once");
            for (var i = 0; i < loaded.Inventory.Length; i++) loaded.Inventory[i] = Item((ulong)i + 200000);
            loaded.Trade[0] = Item(100004); loaded.TradeGold = 5; loaded.AccountInfo.Gold = uint.MaxValue - 2;
            world.RecoverTrades();
            check(loaded.InfoMailItems() == 1 && loaded.Mail.Sum(m => (long)m.Gold) == 3 && loaded.AccountInfo.Gold == uint.MaxValue, "Full-bag or gold-cap recovery lost escrow instead of mailing it");
            check(!world.RecoverTrades() && loaded.InfoMailItems() == 1, "Repeated recovery duplicated mail");
            world.CharacterList.Remove(loaded);

            var state = new GameState(); state.Apply(new S.NewItemInfo { Info = definition });
            state.Apply(new S.UserInformation { Inventory = new UserItem[46], Equipment = new UserItem[14], QuestInventory = [], Gold = 100 });
            state.User!.Inventory[6] = Item(300001);
            state.Apply(new S.TradeRequest { Name = "Peer" }); check(state.TradeInvitation == "Peer", "Native invitation was ignored");
            state.Apply(new S.TradeAccept { Name = "Peer" }); state.Apply(new S.DepositTradeItem { From = 6, To = 0, Success = true });
            check(state.Trading && state.TradeInvitation == "" && state.TradeItems[0]?.UniqueID == 300001 && state.User.Inventory[6] == null, "Native offer acknowledgement diverged from the bag");
            state.TradeLocked = true; state.Apply(new S.TradeItem { TradeItems = new[] { Item(300002) } });
            check(!state.TradeLocked && state.GuestTradeItems[0].Info == definition, "Native peer offer failed binding or consent reset");
            state.PendingTradeGold = 10; state.Apply(new S.TradeCancel { Unlock = true }); state.Apply(new S.LoseGold { Gold = 10 });
            check(state.TradeGold == 10 && state.PendingTradeGold == 0 && state.User.Gold == 90, "Peer unlock swallowed the pending original gold acknowledgement");
            state.Apply(new S.RetrieveTradeItem { From = 0, To = 6, Success = true }); state.Apply(new S.GainedGold { Gold = 10 }); state.Apply(new S.TradeCancel());
            check(!state.Trading && state.TradeGold == 0 && state.User.Gold == 100 && state.User.Inventory[6]?.UniqueID == 300001, "Native cancellation cleared escrow before its return acknowledgements");
            state.Apply(new S.TradeAccept { Name = "Peer" }); state.Apply(new S.LogOutSuccess { Characters = [] });
            check(!state.Trading && state.TradeInvitation == "", "Character switch leaked trade state");
            state.Apply(new S.TradeAccept { Name = "Peer" }); state.Apply(new S.Disconnect());
            check(state.User == null && !state.Trading && state.TradeItems.All(i => i == null), "Disconnect retained native character or trade escrow");
        }
        finally { world.CharacterList.Remove(owner.Info); world.ItemInfoList.Remove(definition); }
    }
    private static int InfoMailItems(this CharacterInfo info) => info.Mail.Sum(m => m.Items.Count);
    private sealed class TradePlayer : PlayerObject
    {
        public readonly List<Packet> Packets = new();
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void ReceiveChat(string text, ChatType type) { }
    }
}
