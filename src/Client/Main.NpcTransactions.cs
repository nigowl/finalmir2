using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private ConfirmationDialog npcTransaction = null!;
    private Label npcTransactionText = null!;
    private SpinBox npcTransactionCount = null!;
    private UserItem? npcTransactionItem;
    private S.NPCGoods? npcTransactionGoods;
    private uint npcTransactionNpc;
    private int npcTransactionMap;
    private bool npcTransactionPending;
    private ulong npcPendingSale;
    private long npcTransactionSentAt;

    private void BuildNpcTransactions()
    {
        npcTransaction = new ConfirmationDialog { Name = "NpcTransactionDialog", Title = "商品确认", MinSize = new Vector2I(380, 170) };
        game.AddChild(npcTransaction); npcTransaction.GetCancelButton().Text = "取消";
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 8); npcTransaction.AddChild(content);
        npcTransactionText = new Label { CustomMinimumSize = new Vector2(340, 55), AutowrapMode = TextServer.AutowrapMode.WordSmart }; content.AddChild(npcTransactionText);
        npcTransactionCount = new SpinBox { CustomMinimumSize = new Vector2(340, 28), Prefix = "数量 ", MinValue = 1, Step = 1 }; content.AddChild(npcTransactionCount);
        npcTransactionCount.ValueChanged += _ => RefreshNpcTransaction();
        npcTransaction.Confirmed += SubmitNpcTransaction;
        npcTransaction.Canceled += InvalidateNpcPurchase;
    }

    private void ConfirmNpcPurchase(UserItem item, S.NPCGoods goods)
    {
        if (npcTransactionPending) { Log("正在等待上一次买卖回执，请稍候。"); return; }
        npcTransactionItem = item; npcTransactionGoods = goods;
        npcTransactionCount.MaxValue = goods.Type == PanelType.Buy ? item.Info.StackSize : Math.Min(item.Count, item.Info.StackSize);
        ShowNpcTransaction();
    }

    private void ConfirmNpcSale(UserItem item)
    {
        if (npcTransactionPending) { Log("正在等待上一次买卖回执，请稍候。"); return; }
        npcTransactionItem = item; npcTransactionGoods = null; npcTransactionCount.MaxValue = item.Count;
        ShowNpcTransaction();
    }

    private void ShowNpcTransaction()
    {
        npcTransactionNpc = npcDialogueId; npcTransactionMap = State.Map?.MapIndex ?? -1; npcTransactionCount.Value = 1;
        npcTransaction.Title = npcTransactionGoods != null ? "确认购买" : "确认出售";
        npcTransaction.GetOkButton().Text = npcTransactionGoods != null ? "购买" : "出售";
        RefreshNpcTransaction(); npcTransaction.PopupCentered();
    }

    private void RefreshNpcTransaction()
    {
        if (npcTransactionItem is not { } item) return;
        var count = (ushort)Math.Clamp(npcTransactionCount.Value, 1, ushort.MaxValue);
        npcTransactionText.Text = npcTransactionGoods is { } goods ?
            $"购买 {item.Info.FriendlyName} ×{count}？\n单价 {ShopPrice(item, goods.Rate):N0} · 合计 {TransactionPrice(item, count, goods.Rate):N0}\n以商人实际使用的货币结算。" :
            $"出售 {item.Info.FriendlyName} ×{count}？\n成交价格按商人的当前价格结算。";
    }

    private static uint TransactionPrice(UserItem item, ushort count, float rate)
    { var priced = item.Clone(); priced.Count = count; return (uint)Math.Clamp(priced.Price() * (double)rate, 0, uint.MaxValue); }

    private void SubmitNpcTransaction()
    {
        if (npcTransactionPending || npcTransactionItem is not { } item || State.Me?.Dead != false || !npc.Visible ||
            npcDialogueId != npcTransactionNpc || State.Map?.MapIndex != npcTransactionMap || !NearQuestNpc(npcTransactionNpc))
        { InvalidateNpcPurchase(); return; }
        var count = (ushort)Math.Clamp(npcTransactionCount.Value, 1, Math.Min(ushort.MaxValue, npcTransactionCount.MaxValue));
        if (npcTransactionGoods == null && (shopMode != PanelType.Sell ||
            State.User?.Inventory.Any(current => current?.UniqueID == item.UniqueID && current.Count >= count) != true ||
            automation.PendingItem == item.UniqueID || pendingDrop == item.UniqueID)) { InvalidateNpcPurchase(); return; }
        npcTransactionPending = true; npcTransactionSentAt = Environment.TickCount64;
        npcPendingSale = npcTransactionGoods == null ? item.UniqueID : 0;
        Send(npcTransactionGoods is { } goods ? new C.BuyItem { ItemIndex = item.UniqueID, Count = count, Type = goods.Type } :
            new C.SellItem { UniqueID = item.UniqueID, Count = count });
        InvalidateNpcPurchase();
    }

    private void InvalidateNpcPurchase()
    { npcTransaction?.Hide(); npcTransactionItem = null; npcTransactionGoods = null; }

    private void NpcTransactionPacket(Packet packet)
    {
        if (packet is S.MapChanged or S.Death or S.Disconnect) InvalidateNpcPurchase();
        if (npcTransactionPending && (packet is S.SellItem sold && sold.UniqueID == npcPendingSale ||
            npcPendingSale == 0 && packet is S.Chat { Type: ChatType.System or ChatType.System2 } chat &&
            (chat.Message.StartsWith("购买成功", StringComparison.Ordinal) || chat.Message.StartsWith("购买失败", StringComparison.Ordinal))))
            npcTransactionPending = false;
    }

    private void ProcessNpcTransaction()
    {
        if (npcTransaction.Visible && (State.Me?.Dead != false || !npc.Visible || !NearQuestNpc(npcTransactionNpc))) InvalidateNpcPurchase();
        if (!npcTransactionPending || Environment.TickCount64 - npcTransactionSentAt < 5000) return;
        npcTransactionSentAt = long.MaxValue / 2; Log("买卖回执超时，请重新连接后核对物品和金币；不会自动重复提交。");
    }
}
