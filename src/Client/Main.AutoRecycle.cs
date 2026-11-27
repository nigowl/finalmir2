using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private C.RecycleItem? recyclePending;
    private int recycleSequence;
    private long recycleSentAt, nextRecycleScan;
    private bool recyclePaused;
    private readonly HashSet<ulong> recycleRejected = new();

    private void ProcessAutoRecycle(long now)
    {
        if (recyclePending != null)
        {
            if (!recyclePaused && now - recycleSentAt >= 5000)
            { recyclePaused = true; Log("自动回收回执未知，已暂停且不会重发；请重连后核对背包和金币。", ChatType.Hint); }
            return;
        }
        if (!preferences.AutoSell || recyclePaused || now < nextRecycleScan || State.User == null || State.Me?.Dead != false ||
            !Assets.SupportsItemRecycling || State.Trading || State.StorageActive || GuildStorageOpen || npc.Visible || publicShop.Visible ||
            appraisalPending != null || selectingAppraisalScroll != 0 || publicPurchases.Pending || npcTransactionPending || heroItemPending != null || social.Visible || HeroInventoryContext || SkillPreparationBusy || automation.PendingItem != 0 || pendingDrop != 0 || draggedItem != 0 || dropDialog.Visible) return;
        nextRecycleScan = now + 500;
        var item = ClientAutoSell.Next(State, preferences, recycleRejected);
        if (item == null) return;
        recyclePending = new C.RecycleItem { RequestId = ++recycleSequence, UniqueID = item.UniqueID, Count = item.Count };
        recycleSentAt = now; Send(recyclePending);
    }

    private void AutoRecyclePacket(Packet packet)
    {
        if (packet is S.RecycleItemResult result && recyclePending is { } request && result.RequestId == request.RequestId && result.UniqueID == request.UniqueID)
        {
            if (!result.Success)
            {
                if (recycleRejected.Count >= 128) recycleRejected.Clear();
                recycleRejected.Add(result.UniqueID);
            }
            recyclePending = null;
        }
        if (packet is S.UserInformation or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin)
        { recyclePending = null; recyclePaused = false; recycleRejected.Clear(); nextRecycleScan = 0; }
    }
}
