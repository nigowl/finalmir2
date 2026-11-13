using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    private void ApplyMerge(S.MergeItem packet)
    {
        if (!packet.Success) { NotifyMessage("合堆失败，请检查数量、品质及绑定条件。", ChatType.Hint); return; }
        UserItem[]? Grid(MirGridType type) => type switch
        { MirGridType.Inventory => User?.Inventory, MirGridType.Storage => Storage, MirGridType.Equipment => User?.Equipment, _ => null };
        var from = Grid(packet.GridFrom); var to = Grid(packet.GridTo);
        var source = from?.FirstOrDefault(item => item?.UniqueID == packet.IDFrom);
        var target = to?.FirstOrDefault(item => item?.UniqueID == packet.IDTo);
        if (source == null || target == null || source == target || !source.CanStackWith(target)) return;
        var count = Math.Min(source.Count, target.Info.StackSize - target.Count);
        if (count <= 0) return;
        source.Count -= (ushort)count; target.Count += (ushort)count;
        if (source.Count == 0) from![Array.IndexOf(from, source)] = null!;
        InventoryRevision++;
    }
}
