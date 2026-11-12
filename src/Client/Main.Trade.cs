using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Control trade = null!, tradeMenu = null!;
    private Label tradeName = null!, guestTradeName = null!, guestTradeGold = null!, tradeNotice = null!;
    private Button tradeGold = null!;
    private OriginalButton tradeConfirm = null!;
    private ConfirmationDialog tradeInvitation = null!;
    private AcceptDialog tradeAmount = null!;
    private LineEdit tradeAmountInput = null!;
    private readonly List<Button> tradeCells = new(), guestTradeCells = new();
    private bool tradeBusy;
    private ulong draggedTradeItem;
    private int tradeRevision = -1;

    private void BuildTrade()
    {
        tradeMenu = Panel(game, 337, 278, 350, 184); tradeMenu.Hide();
        Text(tradeMenu, "相邻站立，右键近处转身面对对方", 12, 10, 330);
        Button(tradeMenu, "允许交易邀请", 12, 47, 157, () => { SetTradeMode(true); tradeMenu.Hide(); });
        Button(tradeMenu, "拒绝交易邀请", 181, 47, 157, () => { SetTradeMode(false); tradeMenu.Hide(); });
        Button(tradeMenu, "向面前玩家发起交易", 12, 88, 326, RequestTrade);
        Button(tradeMenu, "关闭", 12, 134, 326, () => tradeMenu.Hide());
        trade = new Control { Position = new Vector2(210, 390), Size = new Vector2(428, 191), MouseFilter = MouseFilterEnum.Ignore }; game.AddChild(trade); trade.Hide();
        var own = Panel(trade, 0, 0, 204, 152); Picture(own, "Prguse", 389, 0, 0);
        var guest = Panel(trade, 224, 0, 204, 152); Picture(guest, "Prguse", 390, 0, 0);
        ArtButton(own, "Prguse2", 360, 181, 3, "取消交易并退回报价", CancelTrade);
        tradeName = Text(own, "", 20, 6, 155); tradeName.HorizontalAlignment = HorizontalAlignment.Center;
        guestTradeName = Text(guest, "", 20, 6, 164); guestTradeName.HorizontalAlignment = HorizontalAlignment.Center;
        tradeGold = Button(own, "0", 35, 120, 90, ShowTradeAmount); CellStyle(tradeGold); tradeGold.TooltipText = "增加交易金币；取消交易会退回";
        guestTradeGold = Text(guest, "0", 35, 119, 90); guestTradeGold.HorizontalAlignment = HorizontalAlignment.Center;
        tradeConfirm = ArtButton(own, "Title", 520, 135, 120, "确认当前报价 / 取消确认", ToggleTradeLock);
        for (var i = 0; i < 10; i++)
        {
            var index = i;
            foreach (var peer in new[] { false, true })
            {
                var cell = new Button { Position = new Vector2(10 + i / 2 * 37, 39 + i % 2 * 33), Size = new Vector2(36, 32), FocusMode = FocusModeEnum.None, ExpandIcon = true };
                CellStyle(cell); (peer ? guest : own).AddChild(cell); (peer ? guestTradeCells : tradeCells).Add(cell);
                if (!peer) cell.GuiInput += input => TradeCellInput(index, input);
            }
        }
        tradeNotice = Text(trade, "", 0, 155, 428); tradeNotice.HorizontalAlignment = HorizontalAlignment.Center; tradeNotice.AddThemeFontSizeOverride("font_size", 11);
        tradeNotice.AddThemeColorOverride("font_shadow_color", Colors.Black); tradeNotice.AddThemeConstantOverride("shadow_offset_x", 1); tradeNotice.AddThemeConstantOverride("shadow_offset_y", 1);
        tradeInvitation = new ConfirmationDialog { Title = "交易邀请" }; game.AddChild(tradeInvitation); tradeInvitation.GetOkButton().Text = "确定"; tradeInvitation.GetCancelButton().Text = "取消";
        tradeInvitation.Confirmed += () => RespondTradeInvitation(true); tradeInvitation.Canceled += () => RespondTradeInvitation(false);
        tradeAmount = new AcceptDialog { Title = "增加交易金币", MinSize = new Vector2I(310, 120) }; game.AddChild(tradeAmount); tradeAmount.GetOkButton().Text = "确定";
        tradeAmountInput = new LineEdit { Position = new Vector2(12, 12), Size = new Vector2(285, 28), PlaceholderText = "输入增加的金币数量", MaxLength = 10 }; tradeAmount.AddChild(tradeAmountInput);
        tradeAmountInput.TextSubmitted += _ => SubmitTradeAmount(); tradeAmount.Confirmed += SubmitTradeAmount;
        Draggable(own); Draggable(guest); Draggable(tradeMenu);
    }
    public void OpenTrade() { if (State.User == null) return; if (State.Trading) { trade.Show(); inventory.Show(); } else tradeMenu.Show(); }
    public void SetTradeMode(bool enabled) { if (State.User != null) { Send(new C.ChangeTrade { AllowTrade = enabled }); Log(enabled ? "已允许交易邀请。" : "已拒绝新的交易邀请。"); } }
    public void RequestTrade() { if (State.User != null && !State.Trading && State.Me?.Dead == false) { destination = null; MiniMap.Route = []; actionTarget = 0; tradeMenu.Hide(); Send(new C.TradeRequest()); } }
    public void Face(System.Drawing.Point point)
    {
        if (State.User == null || State.Me?.Dead != false || point == State.User.Location) return;
        destination = null; MiniMap.Route = []; actionTarget = 0;
        var direction = Direction(State.User.Location, point);
        if (State.User.Direction != direction) Send(new C.Turn { Direction = direction });
    }
    public void RespondTradeInvitation(bool accept)
    {
        if (State.TradeInvitation.Length == 0) return;
        State.TradeInvitation = ""; State.TradeRevision++; tradeInvitation.Hide();
        Send(new C.TradeReply { AcceptInvite = accept });
    }
    public void OfferTradeItem(int from, int to = -1)
    {
        if (!State.Trading || tradeBusy || State.PendingTradeGold != 0 || State.User == null || (uint)from >= State.User.Inventory.Length || State.User.Inventory[from] == null) return;
        if (to < 0) to = Array.IndexOf(State.TradeItems, null);
        if ((uint)to >= State.TradeItems.Length || State.TradeItems[to] != null) { Log("交易栏已满。"); return; }
        tradeBusy = true; Send(new C.DepositTradeItem { From = from, To = to }); RenderTrade();
    }
    public void RetrieveTradeItem(int from, int to = -1)
    {
        if (!State.Trading || tradeBusy || State.PendingTradeGold != 0 || State.User == null || (uint)from >= State.TradeItems.Length || State.TradeItems[from] == null) return;
        if (to < 0) to = Array.IndexOf(State.User.Inventory, null);
        if ((uint)to >= State.User.Inventory.Length || State.User.Inventory[to] != null) { Log("背包没有可用空位。"); return; }
        tradeBusy = true; Send(new C.RetrieveTradeItem { From = from, To = to }); RenderTrade();
    }
    private void ShowTradeAmount()
    {
        if (!State.Trading || tradeBusy || State.PendingTradeGold != 0) return;
        tradeAmountInput.Text = ""; tradeAmountInput.PlaceholderText = $"1–{State.User!.Gold} 金币"; tradeAmount.PopupCentered(); tradeAmountInput.GrabFocus();
    }
    private void SubmitTradeAmount() { if (uint.TryParse(tradeAmountInput.Text, out var amount)) OfferTradeGold(amount); else Log("请输入有效的金币数量。"); tradeAmount.Hide(); }
    public void OfferTradeGold(uint amount)
    {
        if (!State.Trading || tradeBusy || State.PendingTradeGold != 0 || amount == 0 || amount > State.User!.Gold || (ulong)State.TradeGold + amount > uint.MaxValue) return;
        State.PendingTradeGold = amount; Send(new C.TradeGold { Amount = amount }); RenderTrade();
    }
    public void ToggleTradeLock()
    {
        if (!State.Trading || tradeBusy || State.PendingTradeGold != 0) return;
        State.TradeLocked = !State.TradeLocked; State.TradeRevision++;
        Send(new C.TradeConfirm { Locked = State.TradeLocked }); RenderTrade();
    }
    public void CancelTrade() { if (State.Trading) Send(new C.TradeCancel()); tradeAmount.Hide(); }
    private void TradePacket(Packet packet)
    {
        if (packet is S.DepositTradeItem or S.RetrieveTradeItem or S.TradeCancel { Unlock: false } or S.TradeConfirm) tradeBusy = false;
        if (packet is S.TradeAccept)
        {
            CloseStorage(); CloseQuests(); questBag.Hide(); shopMode = null; npc.Hide(); tradeMenu.Hide(); tradeBusy = false;
            destination = null; actionTarget = 0; inventory.Position = new Vector2(700, 35); inventory.Show();
        }
    }
    private void ProcessTrade()
    {
        if (tradeRevision == State.TradeRevision) return;
        tradeRevision = State.TradeRevision; RenderTrade();
    }
    private void RenderTrade()
    {
        trade.Visible = State.Trading;
        if (!State.Trading) { tradeAmount.Hide(); draggedTradeItem = 0; }
        if (State.TradeInvitation.Length == 0) tradeInvitation.Hide();
        else { tradeInvitation.DialogText = $"{State.TradeInvitation} 请求与你交易，是否接受？"; if (!tradeInvitation.Visible) tradeInvitation.PopupCentered(); }
        icons.RemoveAll(pair => tradeCells.Contains(pair.button) || guestTradeCells.Contains(pair.button));
        tradeName.Text = State.User?.Name ?? ""; guestTradeName.Text = State.TradePartner;
        tradeGold.Text = State.TradeGold.ToString("N0"); guestTradeGold.Text = State.GuestTradeGold.ToString("N0");
        tradeGold.Disabled = tradeConfirm.Disabled = tradeBusy || State.PendingTradeGold != 0;
        tradeConfirm.Index = State.TradeLocked ? 521 : 520; tradeConfirm.HoverIndex = 521; tradeConfirm.PressedIndex = 522;
        tradeNotice.Text = tradeBusy || State.PendingTradeGold != 0 ? "等待服务端确认报价…" : State.TradeLocked ? "已确认 · 等待对方确认；点击可取消确认" : "核对对方报价后确认 · 报价变化需双方重新确认";
        for (var i = 0; i < 10; i++)
        foreach (var peer in new[] { false, true })
        {
            var cell = (peer ? guestTradeCells : tradeCells)[i]; var items = peer ? State.GuestTradeItems : State.TradeItems;
            var item = i < items.Length ? items[i] : null;
            cell.Icon = null; cell.Text = item?.Count > 1 ? item.Count.ToString() : "";
            cell.TooltipText = item == null ? "空格" : ItemText(item) + (peer ? "\n对方的报价" : "\n双击取回或拖回背包");
            cell.Disabled = !peer && (tradeBusy || State.PendingTradeGold != 0);
            if (item != null) icons.Add((cell, item));
        }
    }
    private int TradeCellAtMouse() => !trade.IsVisibleInTree() ? -1 : tradeCells.FindIndex(cell => cell.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
    private void TradeCellInput(int slot, InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse || !State.Trading) return;
        if (mouse.Pressed)
        {
            draggedTradeItem = State.TradeItems[slot]?.UniqueID ?? 0;
            if (mouse.DoubleClick) { RetrieveTradeItem(slot); draggedTradeItem = 0; }
        }
        else if (draggedTradeItem != 0)
        {
            var bag = inventorySlots.FirstOrDefault(pair => pair.button.IsVisibleInTree() && pair.button.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
            if (bag.button != null) RetrieveTradeItem(Array.FindIndex(State.TradeItems, item => item?.UniqueID == draggedTradeItem), bag.slot);
            draggedTradeItem = 0;
        }
    }
    private void CloseTradeUi() { trade.Hide(); tradeMenu.Hide(); tradeInvitation.Hide(); tradeAmount.Hide(); tradeBusy = false; draggedTradeItem = 0; }
}
