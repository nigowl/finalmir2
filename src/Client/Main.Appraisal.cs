using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Packet? appraisalPending;
    private ulong appraisalItem;
    private long appraisalSent;
    private bool appraisalUnknown;

    private void CallNpcLink(string key)
    {
        const string prefix = "[@APPRAISECONFIRM_";
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) { Send(new C.CallNPC { ObjectID = npcDialogueId, Key = key }); return; }
        if (appraisalPending != null || heroItemPending != null || inventoryOrganizer.Pending != null || recyclePending != null ||
            automation.PendingItem != 0 || pendingDrop != 0 || pendingRepair != 0 || npcTransactionPending || publicPurchases.Pending ||
            State.Trading || State.StorageActive || GuildStorageOpen || State.Me?.Dead != false || !key.EndsWith(']') ||
            !ulong.TryParse(key[prefix.Length..^1], out var id) || State.User?.Inventory.Any(item => item?.UniqueID == id && EquipmentAppraisal.Pending(item)) != true)
        { Log("当前无法鉴定，请先等待物品操作完成。", ChatType.Hint); return; }
        appraisalPending = new C.CallNPC { ObjectID = npcDialogueId, Key = key }; appraisalItem = id;
        appraisalSent = Environment.TickCount64; appraisalUnknown = false;
        Send(appraisalPending);
    }

    private bool BlockAppraisalConflict(Packet packet) => appraisalPending != null && !ReferenceEquals(packet, appraisalPending) &&
        packet is not (C.KeepAlive or C.LogOut or C.Chat or C.ClientVersion or C.Login or C.StartGame);

    private void AppraisalPacket(Packet packet)
    {
        if (packet is S.MapChanged or S.Death or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin || packet is S.UserInformation and not S.HeroInformation)
            CancelAppraisalSelection();
        if (packet is S.EquipmentAppraisalResult result && appraisalPending != null && result.UniqueID == appraisalItem)
        { appraisalPending = null; appraisalUnknown = false; if (!result.Success) Log(result.Message, ChatType.Hint); }
        if (packet is S.UserInformation and not S.HeroInformation or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin)
        { appraisalPending = null; appraisalUnknown = false; }
    }

    private void ProcessAppraisal()
    {
        if (selectingAppraisalScroll != 0 && (State.Me?.Dead != false || !inventory.Visible || State.Trading || State.StorageActive ||
            GuildStorageOpen || State.User?.Inventory.Any(item => item?.UniqueID == selectingAppraisalScroll && item.Count > 0) != true)) CancelAppraisalSelection();
        if (appraisalPending == null || appraisalUnknown || Environment.TickCount64 - appraisalSent < 5000) return;
        appraisalUnknown = true;
        Log("鉴定回执未知，已保留操作锁且不会重发；请重新登录核对装备和金币。", ChatType.Hint);
    }
}
