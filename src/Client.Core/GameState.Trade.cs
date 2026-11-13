using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    public string TradeInvitation = "", TradePartner = "";
    public UserItem[] TradeItems = new UserItem[10], GuestTradeItems = new UserItem[10];
    public uint TradeGold, GuestTradeGold, PendingTradeGold;
    public bool TradeLocked;
    public int TradeRevision;
    public bool Trading => TradePartner.Length > 0;

    private void ClearTrade()
    {
        TradeInvitation = TradePartner = "";
        Array.Clear(TradeItems); Array.Clear(GuestTradeItems);
        TradeGold = GuestTradeGold = PendingTradeGold = 0; TradeLocked = false;
        TradeRevision++; InventoryRevision++;
    }
    private void ApplyTrade(Packet packet)
    {
        switch (packet)
        {
            case S.UserInformation: ClearTrade(); break;
            case S.TradeRequest p: TradeInvitation = p.Name; TradeRevision++; break;
            case S.TradeAccept p: ClearTrade(); TradePartner = p.Name; TradeRevision++; break;
            case S.DepositTradeItem p:
                if (p.Success) Transfer(User?.Inventory, TradeItems, p.From, p.To);
                TradeLocked = false; TradeRevision++; InventoryRevision++; break;
            case S.RetrieveTradeItem p:
                if (p.Success) Transfer(TradeItems, User?.Inventory, p.From, p.To);
                TradeLocked = false; TradeRevision++; InventoryRevision++; break;
            case S.TradeItem p:
                GuestTradeItems = p.TradeItems ?? new UserItem[10]; Bind(GuestTradeItems);
                TradeLocked = false; TradeRevision++; break;
            case S.TradeGold p: GuestTradeGold = p.Amount; TradeLocked = false; TradeRevision++; break;
            case S.LoseGold p when Trading && PendingTradeGold != 0 && p.Gold == PendingTradeGold:
                TradeGold += PendingTradeGold; PendingTradeGold = 0; TradeLocked = false; TradeRevision++; break;
            case S.TradeCancel { Unlock: true }:
                TradeLocked = false; TradeRevision++; break;
            case S.TradeCancel: ClearTrade(); NotifyMessage("交易已取消，报价已退回。"); break;
            case S.TradeConfirm: ClearTrade(); NotifyMessage("交易成功。", ChatType.System2); break;
        }
    }
}
