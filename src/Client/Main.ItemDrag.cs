using Godot;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private TextureRect itemDragIcon = null!;
    private Vector2 itemDragStart;
    private Vector2 itemDragPosition;
    private int inventoryDragSource = -1;
    private bool inventoryDragShortcut;
    private ConfirmationDialog dropDialog = null!;
    private Label dropHint = null!;
    private SpinBox dropCount = null!;
    private ulong dropCandidate, pendingDrop;
    private bool dropForHero;
    private long dropSentAt;

    private static bool IsItemShortcut(Button button) => button is OriginalItemButton { SlotAssets: not null };

    private (Button button, int slot) InventoryDropAt(Vector2 position, UserItem? item)
    {
        var hovered = GetViewport().GuiGetHoveredControl();
        var cell = inventorySlots.FirstOrDefault(pair => pair.button == hovered && pair.button.IsVisibleInTree() && pair.button.GetGlobalRect().HasPoint(position));
        if (cell.button != null && IsItemShortcut(cell.button) && !ItemPresentation.CanQuickUse(item))
        { Log("物品快捷栏仅可放入使用类道具，装备和材料请放在背包。"); return default; }
        return cell;
    }

    private void UseQuickItem(int slot)
    {
        var item = State.User?.Inventory.ElementAtOrDefault(slot);
        if (ItemPresentation.CanQuickUse(item)) UseItem(item!.UniqueID);
    }

    private void BuildItemDrag()
    {
        var layer = new CanvasLayer { Layer = 90 }; AddChild(layer);
        itemDragIcon = new TextureRect { Name = "ItemDragIcon", MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Nearest, Visible = false };
        layer.AddChild(itemDragIcon);
        dropDialog = new ConfirmationDialog { Name = "DropItemDialog", Title = "丢弃物品", MinSize = new Vector2I(340, 155) };
        game.AddChild(dropDialog); dropDialog.GetOkButton().Text = "确定"; dropDialog.GetCancelButton().Text = "取消";
        // AcceptDialog 会统一安排直接子控件，用单个容器避免提示和数量输入互相覆盖。
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 8); dropDialog.AddChild(content);
        dropHint = new Label { CustomMinimumSize = new Vector2(305, 38), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        dropHint.AddThemeColorOverride("font_color", new Color("eadbb6")); content.AddChild(dropHint);
        dropCount = new SpinBox { CustomMinimumSize = new Vector2(305, 28), MinValue = 1, Step = 1, Prefix = "数量 " }; content.AddChild(dropCount);
        dropDialog.Confirmed += ConfirmItemDrop;
        dropDialog.Canceled += () => dropCandidate = 0;
    }

    private void ProcessItemDrag()
    {
        var item = State.User?.Inventory.FirstOrDefault(item => item?.UniqueID == draggedItem);
        item ??= State.Storage.FirstOrDefault(item => item?.UniqueID == draggedStorageItem);
        item ??= State.TradeItems.FirstOrDefault(item => item?.UniqueID == draggedTradeItem);
        item ??= State.GuildStorage.FirstOrDefault(entry => entry?.Item.UniqueID == draggedGuildItem)?.Item;
        itemDragIcon.Visible = scene == "game" && item?.Info != null && itemDragPosition.DistanceTo(itemDragStart) >= 4;
        if (itemDragIcon.Visible)
        {
            itemDragIcon.Texture = Assets.Sprite("Items", item!.Info.Image)?.Texture;
            itemDragIcon.Position = itemDragPosition - (itemDragIcon.Texture?.GetSize() ?? Vector2.Zero) / 2;
        }
        if (pendingDrop != 0 && Environment.TickCount64 - dropSentAt > 5000)
        { dropSentAt = long.MaxValue / 2; Log("未收到丢弃回执，该物品保持锁定，请重新连接后核对。"); }
    }

    private void CancelItemDrag()
    {
        draggedItem = draggedStorageItem = draggedTradeItem = draggedGuildItem = 0;
        inventoryDragSource = -1;
        itemDragIcon.Hide();
    }

    private void InventoryDragInput(InputEvent input)
    {
        if (input is InputEventMouseMotion motion) itemDragPosition = motion.Position;
        if (SkillPreparationBusy || heroItemPending != null || appraisalPending != null) return;
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse || State.User == null || scene != "game") return;
        itemDragPosition = mouse.Position;
        if (mouse.Pressed)
        {
            var hovered = GetViewport().GuiGetHoveredControl();
            var source = inventorySlots.FirstOrDefault(pair => pair.button == hovered && pair.button.IsVisibleInTree() && pair.button.GetGlobalRect().HasPoint(mouse.Position));
            if (source.button == null) return;
            if (selectingAppraisalScroll != 0)
            { if (!appraisalScrollDialog.Visible) SelectAppraisalEquipment(State.User.Inventory.ElementAtOrDefault(source.slot)); GetViewport().SetInputAsHandled(); return; }
            inventoryDragSource = source.slot; itemDragStart = mouse.Position;
            inventoryDragShortcut = IsItemShortcut(source.button);
            draggedItem = State.User.Inventory.ElementAtOrDefault(source.slot)?.UniqueID ?? 0;
            if (inventoryDragShortcut && !ItemPresentation.CanQuickUse(State.User.Inventory.ElementAtOrDefault(source.slot))) draggedItem = 0;
            if (inventoryOrganizer.Locked(draggedItem) || draggedItem == pendingDrop || draggedItem == automation.PendingItem || draggedItem == recyclePending?.UniqueID) draggedItem = 0;
            if (draggedItem != 0) _ = PlaySound(Client.MirObjects.OriginalSoundRules.Item(State.User.Inventory[source.slot]?.Info?.Type));
            if (!inventoryDragShortcut && mouse.DoubleClick && draggedItem != 0)
            {
                UseItem(draggedItem); draggedItem = 0; inventoryDragSource = -1;
            }
            GetViewport().SetInputAsHandled(); return;
        }
        if (inventoryDragSource < 0) return;
        if (draggedItem != 0) ReleaseInventoryDrag(mouse.Position);
        draggedItem = 0; inventoryDragSource = -1; itemDragIcon.Hide(); GetViewport().SetInputAsHandled();
    }

    private void ReleaseInventoryDrag(Vector2 position)
    {
        var from = Array.FindIndex(State.User!.Inventory, item => item?.UniqueID == draggedItem);
        if (from < 0) return;
        var destinationButton = InventoryDropAt(position, State.User.Inventory[from]);
        var storageSlot = StorageCellAtMouse(); var tradeSlot = TradeCellAtMouse(); var guildSlot = GuildCellAtMouse();
        if (equipmentSlots.IsVisibleInTree() && equipmentSlots.GetGlobalRect().HasPoint(position) && ItemPresentation.Slots(State.User.Inventory[from].Info.Type).Length > 0)
            UseItem(draggedItem);
        else if (repairCell.IsVisibleInTree() && repairCell.GetGlobalRect().HasPoint(position)) SelectRepairItem(draggedItem);
        else if (guildSlot >= 0) StoreGuildItem(from, guildStorageRow * 8 + guildSlot);
        else if (tradeSlot >= 0) OfferTradeItem(from, tradeSlot);
        else if (storageSlot >= 0) StoreInventory(from, storagePage * Globals.StorageGridSize + storageSlot);
        else if (destinationButton.button != null && destinationButton.slot != from)
        {
            var source = State.User.Inventory[from]; var target = State.User.Inventory[destinationButton.slot];
            if (target != null && inventoryOrganizer.Locked(target.UniqueID)) return;
            if (target != null && source.CanStackWith(target) && target.Count < target.Info.StackSize)
                Send(new C.MergeItem { GridFrom = MirGridType.Inventory, GridTo = MirGridType.Inventory, IDFrom = source.UniqueID, IDTo = target.UniqueID });
            else Send(new C.MoveItem { Grid = MirGridType.Inventory, From = from, To = destinationButton.slot });
        }
        else if (inventoryDragShortcut && destinationButton.button != null && destinationButton.slot == from && position.DistanceTo(itemDragStart) < 4) UseQuickItem(from);
        else if (position.DistanceTo(itemDragStart) >= 4) OfferItemDrop(draggedItem, position);
    }

    private void OfferItemDrop(ulong id, Vector2 position)
    {
        if (pendingDrop != 0 || State.Trading || State.Me?.Dead != false || World.Loading || MiniMap.Expanded ||
            position.X < 0 || position.X >= 1024 || position.Y < 0 || position.Y >= 616 || GetViewport().GuiGetHoveredControl() != null) return;
        PrepareItemDrop(id);
    }

    private void PrepareItemDrop(ulong id)
    {
        if (pendingDrop != 0 || heroItemPending != null || appraisalPending != null || State.Trading || State.Me?.Dead != false) return;
        var item = State.User?.Inventory.Concat(State.User.QuestInventory).FirstOrDefault(item => item?.UniqueID == id);
        if (item?.Info == null || automation.PendingItem == id || recyclePending?.UniqueID == id) return;
        if (item.Info.Bind.HasFlag(BindMode.DontDrop) || item.RentalInformation?.BindingFlags.HasFlag(BindMode.DontDrop) == true)
        { Log("该物品不能丢弃。"); return; }
        dropForHero = HeroInventoryContext;
        dropCandidate = id; dropCount.MaxValue = Math.Min(item.Count, ushort.MaxValue); dropCount.Value = 1;
        dropHint.Text = $"丢弃 {item.Info.FriendlyName}？" + (item.Info.Bind.HasFlag(BindMode.DestroyOnDrop) ? "\n丢弃后会销毁。" : "");
        dropDialog.PopupCentered();
    }

    private void ConfirmItemDrop()
    {
        var item = State.User?.Inventory.Concat(State.User.QuestInventory).FirstOrDefault(item => item?.UniqueID == dropCandidate);
        if (dropForHero != HeroInventoryContext) return;
        if (item == null || pendingDrop != 0 || heroItemPending != null || State.Trading || State.Me?.Dead != false || automation.PendingItem == item.UniqueID || recyclePending?.UniqueID == item.UniqueID) return;
        var count = (ushort)Math.Clamp(dropCount.Value, 1, Math.Min(item.Count, ushort.MaxValue));
        pendingDrop = item.UniqueID; dropCandidate = 0; dropSentAt = Environment.TickCount64;
        Send(new C.DropItem { UniqueID = pendingDrop, Count = count, HeroInventory = dropForHero });
    }

    private void ItemDropPacket(Packet packet)
    {
        if (packet is S.DropItem result && result.UniqueID == pendingDrop) pendingDrop = 0;
        if (packet is S.MapChanged or S.Death) { CancelItemDrag(); dropDialog.Hide(); dropCandidate = 0; }
    }
}
