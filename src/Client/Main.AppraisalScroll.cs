using Crystal.Native;
using Godot;
using C = ClientPackets;

public partial class Main
{
    private ulong selectingAppraisalScroll, selectedAppraisalEquipment;
    private ConfirmationDialog appraisalScrollDialog = null!;

    private void BuildAppraisalScroll()
    {
        appraisalScrollDialog = new ConfirmationDialog { Name = "AppraisalScrollConfirmation", Title = "装备鉴定", MinSize = new Vector2I(360, 150) };
        game.AddChild(appraisalScrollDialog);
        appraisalScrollDialog.GetOkButton().Text = "鉴定"; appraisalScrollDialog.GetCancelButton().Text = "取消";
        appraisalScrollDialog.Canceled += CancelAppraisalSelection; appraisalScrollDialog.CloseRequested += CancelAppraisalSelection;
        appraisalScrollDialog.Confirmed += () =>
        {
            var scroll = selectingAppraisalScroll; var target = selectedAppraisalEquipment;
            CancelAppraisalSelection();
            if (AppraisalBusy() || State.User?.Inventory.Any(item => item?.UniqueID == scroll && item.Count > 0 && EquipmentAppraisal.IsScroll(item.Info)) != true ||
                State.User.Inventory.Any(item => item?.UniqueID == target && EquipmentAppraisal.Pending(item)) != true) return;
            appraisalPending = new C.AppraiseEquipment { ScrollID = scroll, ItemID = target }; appraisalItem = target;
            appraisalSent = Environment.TickCount64; appraisalUnknown = false; Send(appraisalPending);
        };
    }

    private bool AppraisalBusy() => appraisalPending != null || heroItemPending != null || inventoryOrganizer.Pending != null ||
        recyclePending != null || automation.PendingItem != 0 || pendingDrop != 0 || pendingRepair != 0 || npcTransactionPending ||
        publicPurchases.Pending || State.Trading || State.StorageActive || GuildStorageOpen || State.Me?.Dead != false || SkillPreparationBusy || World.Loading;

    private void BeginAppraisalSelection(UserItem scroll)
    {
        if (AppraisalBusy() || shopMode != null || npc.Visible || publicShop.Visible) { Log("请先结束其他操作，再使用鉴定卷轴。", ChatType.Hint); return; }
        CancelAppraisalSelection(); inventory.Show(); CancelItemDrag();
        selectingAppraisalScroll = scroll.UniqueID; SetAppraisalCursor(true);
        Log("请选择背包中待鉴定的装备；右键或Esc取消。", ChatType.Hint);
    }

    private void SelectAppraisalEquipment(UserItem? item)
    {
        if (item == null || !EquipmentAppraisal.Pending(item)) { Log("请选择未鉴定装备。", ChatType.Hint); return; }
        selectedAppraisalEquipment = item.UniqueID; SetAppraisalCursor(false);
        appraisalScrollDialog.DialogText = $"是否鉴定「{item.Info.FriendlyName}」？\n成功后消耗1张装备鉴定卷轴。";
        appraisalScrollDialog.PopupCentered();
    }

    private void SetAppraisalCursor(bool selecting)
    {
        Input.SetDefaultCursorShape(selecting ? Input.CursorShape.Cross : Input.CursorShape.Arrow);
        foreach (var (button, _) in inventorySlots) button.MouseDefaultCursorShape = selecting ? CursorShape.Cross : CursorShape.Arrow;
    }

    private void CancelAppraisalSelection()
    {
        selectingAppraisalScroll = selectedAppraisalEquipment = 0;
        appraisalScrollDialog?.Hide(); SetAppraisalCursor(false);
    }

    private bool AppraisalSelectionInput(InputEvent input)
    {
        if (selectingAppraisalScroll == 0) return false;
        if (input is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape } or InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        { CancelAppraisalSelection(); GetViewport().SetInputAsHandled(); return true; }
        return false;
    }
}
