using Godot;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Control storage = null!, storageGrid = null!, storageAuth = null!;
    private Label storageAuthTitle = null!, storageAuthError = null!;
    private LineEdit storagePassword = null!, storageNewPassword = null!, storageConfirm = null!;
    private Button storageSubmit = null!;
    private readonly List<OriginalButton> storageTabs = new();
    private Label storageLockedHint = null!;
    private TextureRect storageLockedPage = null!;
    private readonly List<Button> storageButtons = new();
    private int storagePage;
    private bool storageBusy, changingStoragePassword;
    private ulong draggedStorageItem;

    private void BuildStorage()
    {
        storage = Panel(game, 0, 35, 386, 344); storage.Hide();
        Picture(storage, "Prguse", 586, 0, 0); WindowTitle(storage, "仓库", 310);
        ArtButton(storage, "Prguse2", 360, 363, 3, "关闭仓库", CloseStorage);
        for (var page = 0; page < Mir2.StorageRules.MaxPages; page++)
        {
            var selected = page;
            var tab = ArtButton(storage, "Title", 743, 8 + page * (68 + ClientUi.Gap), 34, Mir2.StorageRules.PageName(page), () => SelectStoragePage(selected), Mir2.StorageRules.PageName(page));
            TabStyle(tab, 68); storageTabs.Add(tab);
        }
        var passwordButton = ArtButton(storage, "Title", 113, 328, 33, "修改仓库密码", ChangeStoragePassword, "密码");
        passwordButton.Size = new Vector2(48, ClientUi.ButtonHeight);
        passwordButton.HoverIndex = passwordButton.PressedIndex = 113;
        storageGrid = new Control { MouseFilter = MouseFilterEnum.Ignore }; storage.AddChild(storageGrid);
        for (var index = 0; index < Globals.StorageGridSize; index++)
        {
            var slot = index;
            var button = new ItemTipButton { Position = new Vector2(9 + index % 10 * 37, 60 + index / 10 * 33), Size = new Vector2(36, 32), FocusMode = FocusModeEnum.None, ExpandIcon = true };
            ItemCellStyle(button); button.GuiInput += input => StorageCellInput(slot, input);
            storageGrid.AddChild(button); storageButtons.Add(button);
        }
        storageLockedPage = Picture(storage, "Prguse", 2443, 8, 59);
        storageLockedHint = new Label { Position = new Vector2(24, 125), Size = new Vector2(338, 125), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        storage.AddChild(storageLockedHint);
        storageAuth = Panel(storage, 8, 60, 370, 255);
        storageAuthTitle = Text(storageAuth, "", 12, 12, 345);
        storagePassword = Edit(storageAuth, 12, 50, 340, "仓库密码（5–15 位字母或数字）", true);
        storageNewPassword = Edit(storageAuth, 12, 90, 340, "新仓库密码", true);
        storageConfirm = Edit(storageAuth, 12, 130, 340, "再次输入新密码", true);
        foreach (var input in new[] { storagePassword, storageNewPassword, storageConfirm }) { input.MaxLength = 15; input.TextSubmitted += _ => SubmitStoragePassword(); }
        storageSubmit = Button(storageAuth, "解锁", 12, 174, 145, SubmitStoragePassword);
        Button(storageAuth, "取消", 190, 174, 145, () => { if (changingStoragePassword) { changingStoragePassword = false; ClearStoragePasswords(); RenderStorage(); } else CloseStorage(); });
        storageAuthError = Text(storageAuth, "", 12, 212, 345); storageAuthError.AddThemeFontSizeOverride("font_size", 10);
        Draggable(storage);
    }

    public bool CanUseStorage => State.StorageActive && State.StorageUnlocked && State.User is { } user && State.Me?.Dead == false &&
        !State.Trading && (!user.RequireStoragePassword || user.HasStoragePassword);
    private int StorageCapacity => State.StorageCapacity;
    private void OpenStorage()
    {
        CloseQuests();
        storageBusy = changingStoragePassword = false; storagePage = 0;
        shopMode = null; npc.Hide(); ClearStoragePasswords(); storageAuthError.Text = ""; storageSubmit.Disabled = false;
        storage.Show(); inventory.Position = storage.Position + new Vector2(391, 0); inventory.Show();
        RenderStorage();
        if (!State.StorageUnlocked) storagePassword.GrabFocus();
    }
    private void OpenInventoryStorage()
    {
        if (storage.Visible) { CloseStorage(); return; }
        if (State.User == null || State.Me?.Dead != false || State.Trading) return;
        Send(new C.StorageRequest());
    }
    private void CloseStorage()
    {
        storage.Hide(); ClearStoragePasswords(); changingStoragePassword = false;
        State.StorageActive = State.StorageUnlocked = false;
        storageBusy = false; draggedStorageItem = 0;
    }
    private void ClearStoragePasswords() { storagePassword.Text = storageNewPassword.Text = storageConfirm.Text = ""; }
    private void RenderStorage()
    {
        if (State.User == null) return;
        icons.RemoveAll(pair => storageButtons.Contains(pair.button));
        foreach (var (tab, page) in storageTabs.Select((tab, page) => (tab, page)))
        { tab.Index = tab.HoverIndex = storagePage == page ? 743 : 744; tab.SetPressedNoSignal(storagePage == page); }
        var setup = State.User.RequireStoragePassword && !State.User.HasStoragePassword;
        var authenticating = setup || !State.StorageUnlocked || changingStoragePassword;
        storageAuth.Visible = authenticating; storageGrid.Visible = !authenticating;
        storageAuthTitle.Text = setup ? "首次使用：设置独立仓库密码" : changingStoragePassword ? "修改仓库密码" : "输入仓库密码解锁";
        storagePassword.PlaceholderText = changingStoragePassword ? "当前仓库密码" : setup ? "新仓库密码" : "仓库密码";
        storageNewPassword.Visible = changingStoragePassword;
        storageConfirm.Visible = setup || changingStoragePassword;
        storageSubmit.Text = setup ? "设置并打开" : changingStoragePassword ? "确认修改" : "解锁";
        for (var i = 0; i < storageButtons.Count; i++)
        {
            var index = storagePage * Globals.StorageGridSize + i; var button = storageButtons[i];
            var item = index < StorageCapacity ? State.Storage[index] : null;
            button.Disabled = !CanUseStorage || storageBusy || index >= StorageCapacity;
            button.Icon = null; button.Text = item?.Count > 1 ? item.Count.ToString() : "";
            ItemTooltip(button, item, index >= StorageCapacity ? "仓库未开通" : "空格");
            if (item != null) icons.Add((button, item));
        }
        storageGrid.TooltipText = "双击存取；拖动交换或跨窗口移动";
        storageLockedPage.Visible = storagePage > 0 && storagePage * Globals.StorageGridSize >= StorageCapacity && !authenticating;
        storageLockedHint.Visible = storageLockedPage.Visible;
        storageLockedHint.Text = Mir2.StorageRules.PageName(storagePage) + "未开通\n\n" +
            (Assets.StorageUnlocks.FirstOrDefault(hint => hint.Page == storagePage + 1)?.Description ?? "请向当前版本管理员咨询开通任务。");
    }
    public void SelectStoragePage(int page) { if (page is < 0 or >= Mir2.StorageRules.MaxPages) return; storagePage = page; RenderStorage(); }
    public void ChangeStoragePassword()
    {
        if (!State.StorageUnlocked || storageBusy) return;
        changingStoragePassword = true; ClearStoragePasswords(); storageAuthError.Text = ""; RenderStorage(); storagePassword.GrabFocus();
    }
    public void EnterStoragePassword(string current, string next = "", string confirmation = "")
    {
        storagePassword.Text = current; storageNewPassword.Text = next; storageConfirm.Text = confirmation;
        SubmitStoragePassword();
    }
    public void SubmitStoragePassword()
    {
        if (!State.StorageActive || storageSubmit.Disabled) return;
        var setup = State.User?.HasStoragePassword == false;
        var current = storagePassword.Text; var next = changingStoragePassword ? storageNewPassword.Text : current;
        if (!System.Text.RegularExpressions.Regex.IsMatch(current, "^[A-Za-z0-9]{5,15}$") ||
            ((setup || changingStoragePassword) && (!System.Text.RegularExpressions.Regex.IsMatch(next, "^[A-Za-z0-9]{5,15}$") || next != storageConfirm.Text)))
        { storageAuthError.Text = "密码须为 5–15 位字母或数字，且两次输入一致。"; return; }
        if (setup || changingStoragePassword) Send(new C.SetStoragePassword { CurrentPassword = setup ? "" : current, NewPassword = next });
        else Send(new C.UnlockStorage { Password = current });
        ClearStoragePasswords(); storageSubmit.Disabled = true;
    }
    private void StoragePasswordResult(Packet packet)
    {
        storageSubmit.Disabled = false;
        switch (packet)
        {
            case S.StorageUnlockResult p: storageAuthError.Text = p.Result is 0 or 4 ? "" : p.Result == 2 ? "仓库密码错误。" : "无法解锁，请重新打开仓库。"; break;
            case S.StoragePasswordResult p:
                storageAuthError.Text = p.Result == 4 ? "" : p.Result == 2 ? "当前仓库密码错误。" : "密码未修改，请检查输入及当前状态。";
                if (p.Result == 4) changingStoragePassword = false;
                break;
        }
        RenderStorage();
    }
    public void StoreInventory(int from, int to = -1)
    {
        if (!CanUseStorage || storageBusy || State.User == null) return;
        var items = from < 0 ? State.User.QuestInventory : State.User.Inventory;
        var slot = from < 0 ? ~from : from;
        if ((uint)slot >= items.Length || items[slot] == null || inventoryOrganizer.Locked(items[slot].UniqueID)) return;
        if (to < 0) to = Enumerable.Range(0, StorageCapacity).FirstOrDefault(i => State.Storage[i] == null, -1);
        if ((uint)to >= StorageCapacity || State.Storage[to] != null) { Log("仓库没有可用空位。"); return; }
        storageBusy = true; Send(new C.StoreItem { From = from, To = to }); RenderStorage();
    }
    public void TakeStorage(int from, int to = -1)
    {
        if (!CanUseStorage || storageBusy || State.User == null || (uint)from >= StorageCapacity || State.Storage[from] == null) return;
        var source = State.Storage[from];
        var target = to < 0 ? State.User.Inventory.FirstOrDefault(item => source.CanStackWith(item) && item.Count < item.Info.StackSize) : State.User.Inventory.ElementAtOrDefault(to);
        if (target != null && source.CanStackWith(target) && target.Count < target.Info.StackSize)
        {
            storageBusy = true;
            Send(new C.MergeItem { GridFrom = MirGridType.Storage, GridTo = MirGridType.Inventory, IDFrom = source.UniqueID, IDTo = target.UniqueID });
            RenderStorage(); return;
        }
        if (to < 0) to = Array.IndexOf(State.User.Inventory, null);
        if (to != -1 && ((uint)to >= State.User.Inventory.Length || State.User.Inventory[to] != null)) { Log("背包没有可用空位。"); return; }
        storageBusy = true; Send(new C.TakeBackItem { From = from, To = to }); RenderStorage();
    }
    public void MoveStorage(int from, int to)
    {
        if (!CanUseStorage || storageBusy || (uint)from >= StorageCapacity || (uint)to >= StorageCapacity || from == to || State.Storage[from] == null) return;
        storageBusy = true; Send(new C.MoveItem { Grid = MirGridType.Storage, From = from, To = to }); RenderStorage();
    }
    private int StorageCellAtMouse() => !storageGrid.IsVisibleInTree() ? -1 : storageButtons.FindIndex(button => button.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
    private void StorageCellInput(int visibleSlot, InputEvent input)
    {
        if (!CanUseStorage || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse) return;
        var slot = storagePage * Globals.StorageGridSize + visibleSlot;
        if (mouse.Pressed)
        {
            draggedStorageItem = slot < State.Storage.Length ? State.Storage[slot]?.UniqueID ?? 0 : 0;
            if (mouse.DoubleClick) { TakeStorage(slot); draggedStorageItem = 0; }
        }
        else if (draggedStorageItem != 0)
        {
            var from = Array.FindIndex(State.Storage, item => item?.UniqueID == draggedStorageItem);
            var bag = InventoryDropAt(GetGlobalMousePosition(), State.Storage.ElementAtOrDefault(from));
            if (bag.button != null) TakeStorage(from, bag.slot);
            else if (StorageCellAtMouse() is >= 0 and var destination) MoveStorage(from, storagePage * Globals.StorageGridSize + destination);
            draggedStorageItem = 0;
        }
    }
    private void ProcessStorage()
    {
        if (!storage.Visible) return;
        if (!State.StorageActive || State.User == null || State.Me?.Dead != false || State.Trading) CloseStorage();
    }
}
