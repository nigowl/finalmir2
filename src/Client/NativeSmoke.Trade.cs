using Godot;
using System.Net.Http.Headers;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyTrade(GameConnection bot, GameState other, List<Packet> received, Func<Func<bool>, Task> botUntil)
    {
        stage = "native trade facing and invitation consent";
        Client.SetDestination(new Point(other.User!.Location.X, other.User.Location.Y + 1));
        await Until(() => State.User!.Location == new Point(other.User.Location.X, other.User.Location.Y + 1));
        using (var turn = new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = Client.World.Screen(other.User.Location) + new Vector2(24, 16) }) Client._UnhandledInput(turn);
        await bot.Send(new C.Turn { Direction = MirDirection.Down });
        await bot.Send(new C.ChangeTrade { AllowTrade = true }); Client.SetTradeMode(true);
        await botUntil(() => other.User.Direction == MirDirection.Down && other.Actors.GetValueOrDefault(State.User!.ObjectID)?.Direction == MirDirection.Up);
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        Client.RequestTrade(); await botUntil(() => other.TradeInvitation == State.User!.Name);
        packets.RemoveAll(p => p is S.Chat);
        await bot.Send(new C.TradeReply { AcceptInvite = false });
        await Until(() => packets.OfType<S.Chat>().Any());
        if (State.Trading || other.Trading) throw new Exception("Declined trade created a session.");
        async Task Invite()
        {
            await ToSignal(GetTree().CreateTimer(2.2), SceneTreeTimer.SignalName.Timeout);
            received.RemoveAll(p => p is S.TradeAccept);
            other.TradeInvitation = ""; Client.RequestTrade(); await botUntil(() => other.TradeInvitation == State.User!.Name);
            await bot.Send(new C.TradeReply { AcceptInvite = true });
            await botUntil(() => other.Trading); await Until(() => State.Trading);
        }
        await Invite();
        // New accounts start with zero gold; fund the partner through the same original protocol.
        Client.OfferTradeGold(20); await Until(() => State.TradeGold == 20 && State.PendingTradeGold == 0);
        Client.ToggleTradeLock(); await bot.Send(new C.TradeConfirm { Locked = true });
        await Until(() => !State.Trading); await botUntil(() => !other.Trading);
        await Invite();
        var ownGold = State.User!.Gold; var peerGold = other.User.Gold;
        var ownItem = State.User.Inventory.First(i => i?.Info?.Type == ItemType.Potion && i.Info.Stats[Stat.HP] >= 0)!;
        var peerItem = other.User.Inventory.First(i => i?.Info?.Type == ItemType.Weapon)!;
        var peerPotionCount = other.User.Inventory.Where(i => i?.ItemIndex == ownItem.ItemIndex).Sum(i => i.Count);
        var ownSlot = Array.IndexOf(State.User.Inventory, ownItem); var peerSlot = Array.IndexOf(other.User.Inventory, peerItem);
        stage = "native trade acknowledgement, withdrawal and repricing";
        Client.OfferTradeItem(ownSlot); await Until(() => State.TradeItems[0]?.UniqueID == ownItem.UniqueID);
        await botUntil(() => other.GuestTradeItems[0]?.UniqueID == ownItem.UniqueID);
        Client.RetrieveTradeItem(0, ownSlot); await Until(() => State.User.Inventory[ownSlot]?.UniqueID == ownItem.UniqueID);
        Client.OfferTradeItem(ownSlot); await Until(() => State.TradeItems[0]?.UniqueID == ownItem.UniqueID);
        await bot.Send(new C.DepositTradeItem { From = peerSlot, To = 0 }); await botUntil(() => other.TradeItems[0]?.UniqueID == peerItem.UniqueID);
        await Until(() => State.GuestTradeItems[0]?.UniqueID == peerItem.UniqueID && State.GuestTradeItems[0].Info != null);
        Client.OfferTradeGold(13); await Until(() => State.PendingTradeGold == 0 && State.TradeGold == 13);
        await botUntil(() => other.GuestTradeGold == 13);
        Client.ToggleTradeLock(); await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        await bot.Send(new C.TradeGold { Amount = 7 }); await Until(() => State.GuestTradeGold == 7 && !State.TradeLocked);
        await botUntil(() => other.User.Gold == peerGold - 7);
        using (var http = new System.Net.Http.HttpClient())
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("MIR2_RUNTIME_ROOT")!, "admin.key")).Trim());
            using var status = JsonDocument.Parse(await http.GetStringAsync(Argument("--server").TrimEnd('/') + "/management/status"));
            var players = status.RootElement.GetProperty("players").EnumerateArray().Where(p => p.GetProperty("name").GetString() is "NativeMate" or "NativeWarrior").ToArray();
            if (players.Length != 2 || players.Any(p => p.GetProperty("tradePartner").GetString() is not ("NativeMate" or "NativeWarrior") || p.GetProperty("tradeLocked").GetBoolean())) throw new Exception("Management did not report unlocked mutual trading partners.");
        }
        await Screenshot("trade");
        Client.ToggleTradeLock(); await bot.Send(new C.TradeConfirm { Locked = true });
        await Until(() => !State.Trading); await botUntil(() => !other.Trading);
        if (State.User.Gold != ownGold - 6 || other.User.Gold != peerGold + 6 || State.User.Inventory.Count(i => i?.UniqueID == peerItem.UniqueID) != 1 || other.User.Inventory.Where(i => i?.ItemIndex == ownItem.ItemIndex).Sum(i => i.Count) != peerPotionCount + ownItem.Count || State.User.Inventory.Any(i => i?.UniqueID == ownItem.UniqueID) || other.User.Inventory.Any(i => i?.UniqueID == peerItem.UniqueID)) throw new Exception("Native trade lost or duplicated item IDs, stack counts or gold.");
        stage = "native trade cancellation and item return";
        await Invite();
        var receivedSlot = Array.FindIndex(State.User.Inventory, i => i?.UniqueID == peerItem.UniqueID);
        Client.OfferTradeItem(receivedSlot);
        await Until(() => State.TradeItems[0]?.UniqueID == peerItem.UniqueID);
        // Item and gold requests are deliberately serialized by the micro-client.
        Client.OfferTradeGold(4); await Until(() => State.PendingTradeGold == 0 && State.TradeGold == 4);
        Client.CancelTrade(); await Until(() => !State.Trading); await botUntil(() => !other.Trading);
        if (State.User.Gold != ownGold - 6 || State.User.Inventory.Count(i => i?.UniqueID == peerItem.UniqueID) != 1) throw new Exception("Cancel failed to return the original item and gold.");
        stage = "native trade partner disconnect";
        await Invite();
        receivedSlot = Array.FindIndex(State.User.Inventory, i => i?.UniqueID == peerItem.UniqueID);
        Client.OfferTradeItem(receivedSlot); await Until(() => State.TradeItems[0]?.UniqueID == peerItem.UniqueID);
        Client.OfferTradeGold(3); await Until(() => State.TradeGold == 3 && State.PendingTradeGold == 0);
        await bot.Send(new C.Disconnect()); await Until(() => !State.Trading && State.GroupMembers.Count == 0);
        if (State.User.Gold != ownGold - 6 || State.User.Inventory.Count(i => i?.UniqueID == peerItem.UniqueID) != 1 || !Client.Connection.Connected) throw new Exception("Partner disconnect lost escrow or broke the remaining connection.");
        GD.Print("PASS native trades: original dual-panel art, consent/decline, acknowledged offers/withdrawal, incremental gold, repricing revokes confirmation, two-player item-ID/gold conservation, cancel, partner disconnect, management trade monitoring.");
    }
}
