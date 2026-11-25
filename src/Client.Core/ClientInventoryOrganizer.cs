using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public sealed class ClientInventoryOrganizer
{
    public const int Interval = 30000;
    private long nextScan, sentAt;
    private int checkedRevision = -1;
    private bool organizing;
    private readonly List<Packet> manualRequests = [];
    public C.MergeItem? Pending { get; private set; }
    public bool Paused { get; private set; }
    public bool Locked(ulong id) => Pending is { } request && (request.IDFrom == id || request.IDTo == id);

    public void Reset()
    { nextScan = 0; checkedRevision = -1; organizing = Paused = false; Pending = null; manualRequests.Clear(); }

    public bool Begin(int revision, long now, bool busy)
    {
        if (Pending != null && now - sentAt >= 5000) Paused = true;
        if (Paused || Pending != null || busy || manualRequests.Count > 0) return false;
        if (organizing) return true;
        if (nextScan == 0) { nextScan = now + Interval; return false; }
        if (now < nextScan) return false;
        nextScan = now + Interval;
        if (checkedRevision == revision) return false;
        checkedRevision = revision; organizing = true; return true;
    }

    public C.MergeItem? Next(UserItem[] inventory, int revision, long now)
    {
        checkedRevision = revision;
        // 按定义分桶，仅兼容的小堆之间比较，快捷槽只作目标，保留快捷绑定。
        var groups = new Dictionary<int, List<UserItem>>();
        for (var slot = 0; slot < inventory.Length; slot++)
        {
            var item = inventory[slot];
            if (item?.Info == null || item.Info.IsEquipment || item.Info.StackSize <= 1 || item.Count == 0) continue;
            if (!groups.TryGetValue(item.ItemIndex, out var targets)) groups[item.ItemIndex] = targets = [];
            if (slot >= 6 && targets.FirstOrDefault(target => item.CanStackWith(target)) is { } target)
            {
                Pending = new C.MergeItem { GridFrom = MirGridType.Inventory, GridTo = MirGridType.Inventory, IDFrom = item.UniqueID, IDTo = target.UniqueID };
                sentAt = now; return Pending;
            }
            if (item.Count < item.Info.StackSize) targets.Add(item);
        }
        organizing = false; return null;
    }

    public static int[] DisplayOrder(UserItem[] inventory) => ItemPresentation.BagSlots(inventory)
        .OrderBy(slot => inventory[slot]?.Info == null ? 1 : 0)
        .ThenBy(slot => inventory[slot]?.Info?.Type)
        .ThenBy(slot => inventory[slot]?.Info?.FriendlyName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(slot => inventory[slot]?.ItemIndex)
        .ThenByDescending(slot => inventory[slot]?.Count)
        .ThenBy(slot => slot).ToArray();

    public void Sent(Packet packet)
    {
        if (ReferenceEquals(packet, Pending)) return;
        if (packet is C.MoveItem or C.MergeItem or C.UseItem or C.EquipItem or C.RemoveItem or C.SplitItem)
        {
            if (manualRequests.Count >= 128) { Paused = true; return; }
            manualRequests.Add(packet);
        }
    }

    public void Observe(Packet packet)
    {
        if (packet is S.UserInformation or S.Disconnect or S.ReturnToLogin or S.LogOutSuccess) { Reset(); return; }
        if (packet is S.MergeItem result && Pending is { } request && result.GridFrom == request.GridFrom && result.GridTo == request.GridTo &&
            result.IDFrom == request.IDFrom && result.IDTo == request.IDTo)
        { Pending = null; if (!result.Success) organizing = false; }
        manualRequests.RemoveAll(request => (request, packet) switch
        {
            (C.MoveItem a, S.MoveItem b) => a.Grid == b.Grid && a.From == b.From && a.To == b.To,
            (C.MergeItem a, S.MergeItem b) => a.GridFrom == b.GridFrom && a.GridTo == b.GridTo && a.IDFrom == b.IDFrom && a.IDTo == b.IDTo,
            (C.UseItem a, S.UseItem b) => a.Grid == b.Grid && a.UniqueID == b.UniqueID,
            (C.EquipItem a, S.EquipItem b) => a.Grid == b.Grid && a.UniqueID == b.UniqueID && a.To == b.To,
            (C.RemoveItem a, S.RemoveItem b) => a.Grid == b.Grid && a.UniqueID == b.UniqueID && a.To == b.To,
            (C.SplitItem a, S.SplitItem1 b) => a.Grid == b.Grid && a.UniqueID == b.UniqueID && a.Count == b.Count,
            _ => false
        });
    }
}
