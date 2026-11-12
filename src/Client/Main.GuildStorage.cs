using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Control guildStoragePage = null!;
    private readonly List<Button> guildStorageCells = new();
    private Label guildStorageGold = null!, guildStoragePosition = null!;
    private OriginalButton guildDonate = null!, guildWithdraw = null!;
    private AcceptDialog guildGoldAmount = null!;
    private LineEdit guildGoldInput = null!;
    private bool guildStorageBusy, guildStorageRequested, guildGoldBusy, guildWithdrawing;
    private int guildStorageRow, guildStorageRevision = -1;
    private long guildStorageRequestTime;
    private ulong draggedGuildItem;
    private bool GuildStorageOpen => guild.Visible && guildPage == 3 && State.InGuild;
    private bool CanChangeGuildStorage => State.InGuild && State.GuildStorageLoaded && State.Me?.Dead == false && !State.Trading && !guildStorageBusy && !guildGoldBusy;

    private void BuildGuildStorage()
    {
        guildTabs.Add(ArtButton(guild, "Title", 105, 162, 38, "行会仓库与资金（仅安全区）", () => SelectGuildPage(3)));
        guildStoragePage = new Control { Position = new Vector2(0, 60), Size = new Vector2(352, 342) }; guild.AddChild(guildStoragePage);
        Picture(guildStoragePage, "Prguse", 1851, 30, 19);
        for (var i = 0; i < 64; i++)
        {
            var index = i;
            var button = new Button { Position = new Vector2(31 + i % 8 * 36, 20 + i / 8 * 36), Size = new Vector2(35, 35), ExpandIcon = true, FocusMode = FocusModeEnum.None };
            CellStyle(button); button.GuiInput += input => GuildStorageCellInput(index, input);
            guildStoragePage.AddChild(button); guildStorageCells.Add(button);
        }
        ArtButton(guildStoragePage, "Prguse2", 197, 337, 1, "上滚一行", () => ScrollGuildStorage(-1));
        ArtButton(guildStoragePage, "Prguse2", 207, 337, 318, "下滚一行", () => ScrollGuildStorage(1));
        guildStoragePosition = Text(guildStoragePage, "", 14, 310, 116); guildStoragePosition.AddThemeFontSizeOverride("font_size", 9);
        guildWithdraw = ArtButton(guildStoragePage, "Prguse", 917, 142, 313, "取出金币（会长）", () => ShowGuildGold(true));
        guildDonate = ArtButton(guildStoragePage, "Prguse", 918, 158, 313, "捐献金币", () => ShowGuildGold(false));
        guildWithdraw.HoverIndex = guildWithdraw.PressedIndex = 917; guildDonate.HoverIndex = guildDonate.PressedIndex = 918;
        guildStorageGold = Text(guildStoragePage, "", 194, 308, 125); guildStorageGold.HorizontalAlignment = HorizontalAlignment.Right;
        guildGoldAmount = new AcceptDialog { Title = "行会资金", MinSize = new Vector2I(320, 125) }; game.AddChild(guildGoldAmount); guildGoldAmount.GetOkButton().Text = "确定";
        guildGoldInput = new LineEdit { Position = new Vector2(12, 12), Size = new Vector2(290, 28), MaxLength = 10 }; guildGoldAmount.AddChild(guildGoldInput);
        guildGoldInput.TextSubmitted += _ => SubmitGuildGold(); guildGoldAmount.Confirmed += SubmitGuildGold;
    }
    private void ScrollGuildStorage(int rows) { guildStorageRow = Math.Clamp(guildStorageRow + rows, 0, Math.Max(0, (State.GuildStorage.Length + 7) / 8 - 8)); RenderGuildStorage(); }
    public void SetGuildStorageRow(int row) { guildStorageRow = Math.Clamp(row, 0, Math.Max(0, (State.GuildStorage.Length + 7) / 8 - 8)); RenderGuildStorage(); }
    public void StoreGuildItem(int from, int to = -1)
    {
        if (!CanChangeGuildStorage || !State.HasGuildPermission(GuildRankOptions.CanStoreItem) || State.User == null || (uint)from >= State.User.Inventory.Length || State.User.Inventory[from] == null) return;
        if (to < 0) to = Array.IndexOf(State.GuildStorage, null);
        if ((uint)to >= State.GuildStorage.Length || State.GuildStorage[to] != null) { Log("行会仓库没有可用空位。"); return; }
        SendGuildStorage(0, from, to);
    }
    public void RetrieveGuildItem(int from, int to = -1)
    {
        if (!CanChangeGuildStorage || !State.HasGuildPermission(GuildRankOptions.CanRetrieveItem) || State.User == null || (uint)from >= State.GuildStorage.Length || State.GuildStorage[from] == null) return;
        if (to < 0) to = Array.IndexOf(State.User.Inventory, null);
        if ((uint)to >= State.User.Inventory.Length || State.User.Inventory[to] != null) { Log("背包没有可用空位。"); return; }
        SendGuildStorage(1, from, to);
    }
    public void MoveGuildItem(int from, int to)
    {
        if (!CanChangeGuildStorage || !State.HasGuildPermission(GuildRankOptions.CanStoreItem) || (uint)from >= State.GuildStorage.Length || (uint)to >= State.GuildStorage.Length || from == to || State.GuildStorage[from] == null) return;
        SendGuildStorage(2, from, to);
    }
    private void SendGuildStorage(byte type, int from, int to)
    {
        guildStorageBusy = true; guildStorageRequestTime = Environment.TickCount64;
        Send(new C.GuildStorageItemChange { Type = type, From = from, To = to }); RenderGuildStorage();
    }
    private void ShowGuildGold(bool withdraw)
    {
        if (!CanChangeGuildStorage || withdraw && State.Guild!.MyRankId != 0) return;
        guildWithdrawing = withdraw; guildGoldAmount.Title = withdraw ? "取出行会金币" : "捐献行会金币";
        guildGoldInput.Text = ""; guildGoldInput.PlaceholderText = $"1–{(withdraw ? State.Guild!.Gold : State.User!.Gold)} 金币";
        guildGoldAmount.PopupCentered(); guildGoldInput.GrabFocus();
    }
    private void SubmitGuildGold()
    {
        if (uint.TryParse(guildGoldInput.Text, out var amount)) ChangeGuildGold(guildWithdrawing, amount); else Log("请输入有效的金币数量。");
        guildGoldAmount.Hide();
    }
    public void ChangeGuildGold(bool withdraw, uint amount)
    {
        if (!CanChangeGuildStorage || amount == 0 || withdraw && (State.Guild!.MyRankId != 0 || amount > State.Guild.Gold) || !withdraw && amount > State.User!.Gold) return;
        guildGoldBusy = true; guildStorageRequestTime = Environment.TickCount64;
        Send(new C.GuildStorageGoldChange { Type = (byte)(withdraw ? 1 : 0), Amount = amount }); RenderGuildStorage();
    }
    private void GuildStoragePacket(Packet packet)
    {
        if (packet is S.GuildStorageItemChange p && (p.Type >= 3 || p.User == State.User?.RealId)) guildStorageBusy = false;
        if (packet is S.GuildStatus) guildGoldBusy = false;
        if (!State.InGuild) { guildStorageBusy = guildGoldBusy = guildStorageRequested = false; draggedGuildItem = 0; guildGoldAmount.Hide(); }
    }
    private void ProcessGuildStorage()
    {
        if (GuildStorageOpen && !State.GuildStorageLoaded && !guildStorageRequested)
        {
            guildStorageRequested = true; Send(new C.GuildStorageItemChange { Type = 3 });
        }
        if ((guildStorageBusy || guildGoldBusy) && Environment.TickCount64 - guildStorageRequestTime > 10000)
        {
            Log("未收到行会仓库操作回执，请重新连接以核对物品和资金。"); guildStorageRequestTime = long.MaxValue / 2;
        }
        if (guildStorageRevision != State.GuildStorageRevision) { guildStorageRevision = State.GuildStorageRevision; RenderGuildStorage(); }
    }
    private void RenderGuildStorage()
    {
        guildStoragePage.Visible = State.InGuild && guildPage == 3;
        icons.RemoveAll(pair => guildStorageCells.Contains(pair.button));
        guildStorageGold.Text = State.Guild?.Gold.ToString("N0") ?? "0";
        guildStoragePosition.Text = $"{guildStorageRow + 1}–{Math.Min(guildStorageRow + 8, 14)}/14 行";
        guildDonate.Disabled = !CanChangeGuildStorage; guildWithdraw.Visible = State.Guild?.MyRankId == 0; guildWithdraw.Disabled = !CanChangeGuildStorage;
        for (var i = 0; i < guildStorageCells.Count; i++)
        {
            var slot = guildStorageRow * 8 + i; var button = guildStorageCells[i]; var item = slot < State.GuildStorage.Length ? State.GuildStorage[slot]?.Item : null;
            button.Icon = null; button.Text = item?.Count > 1 ? item.Count.ToString() : "";
            button.Disabled = !CanChangeGuildStorage;
            button.TooltipText = item == null ? $"行会仓库 {slot + 1}" : ItemText(item) + $"\n行会仓库 {slot + 1} · 存入角色编号 {State.GuildStorage[slot].UserId}";
            if (item != null) icons.Add((button, item));
        }
        if (GuildStorageOpen) guildHint.Text = !State.GuildStorageLoaded ? "正在读取行会仓库…" : guildStorageBusy || guildGoldBusy ? "等待服务端回执…" : "仅安全区可存取 · 双击或跨窗口拖动 · 仓库内拖动交换 · 滚轮翻行";
    }
    private int GuildCellAtMouse() => !GuildStorageOpen ? -1 : guildStorageCells.FindIndex(cell => cell.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
    private void GuildStorageCellInput(int visibleSlot, InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel) { ScrollGuildStorage(wheel.ButtonIndex == MouseButton.WheelUp ? -1 : 1); return; }
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse) return;
        var slot = guildStorageRow * 8 + visibleSlot;
        if (mouse.Pressed)
        {
            draggedGuildItem = (uint)slot < State.GuildStorage.Length ? State.GuildStorage[slot]?.Item.UniqueID ?? 0 : 0;
            if (mouse.DoubleClick) { RetrieveGuildItem(slot); draggedGuildItem = 0; }
        }
        else if (draggedGuildItem != 0)
        {
            var from = Array.FindIndex(State.GuildStorage, entry => entry?.Item.UniqueID == draggedGuildItem);
            var bag = inventorySlots.FirstOrDefault(pair => pair.button.IsVisibleInTree() && pair.button.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
            if (bag.button != null) RetrieveGuildItem(from, bag.slot);
            else if (GuildCellAtMouse() is >= 0 and var to) MoveGuildItem(from, guildStorageRow * 8 + to);
            draggedGuildItem = 0;
        }
    }
    private void ResetGuildStorageUi() { guildStorageBusy = guildGoldBusy = guildStorageRequested = false; draggedGuildItem = 0; guildGoldAmount.Hide(); }
}
