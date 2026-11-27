using Crystal.Native;

public partial class Main
{
    private readonly ClientInventoryOrganizer inventoryOrganizer = new();
    private bool inventoryOrganizerNotice;

    private void ProcessInventoryOrganization(long now)
    {
        var busy = State.User == null || State.Me?.Dead != false || World.Loading || State.Trading || State.StorageActive || GuildStorageOpen ||
            appraisalPending != null || selectingAppraisalScroll != 0 || npc.Visible || publicShop.Visible || publicPurchases.Pending || npcTransactionPending || SkillPreparationBusy || automation.PendingItem != 0 ||
            recyclePending != null || heroItemPending != null || social.Visible || HeroInventoryContext || pendingDrop != 0 || draggedItem != 0 || dropDialog.Visible || storageBusy || tradeBusy || pendingRepair != 0;
        if (inventoryOrganizer.Begin(State.InventoryRevision, now, busy))
        {
            SortInventoryDisplay();
            if (inventoryOrganizer.Next(State.User!.Inventory, State.InventoryRevision, now) is { } merge) Send(merge);
        }
        if (inventoryOrganizer.Paused && !inventoryOrganizerNotice)
        { inventoryOrganizerNotice = true; Log("自动整理回执未知，已暂停且不会重发；请重连后核对物品。", ChatType.Hint); }
        if (!inventoryOrganizer.Paused) inventoryOrganizerNotice = false;
    }

    private void SortInventoryDisplay()
    {
        if (State.User == null) return;
        ResizeInventoryGrid(State.User.Inventory);
        var order = ClientInventoryOrganizer.DisplayOrder(State.User.Inventory);
        var positions = new int[State.User.Inventory.Length];
        for (var index = 0; index < order.Length; index++) positions[order[index]] = index;
        foreach (var (button, slot) in inventorySlots)
            if (button.GetParent() == inventoryGrid)
                button.Position = new Godot.Vector2(positions[slot] % ClientUi.InventoryColumns * 37, positions[slot] / ClientUi.InventoryColumns * 33);
    }
}
