using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    private readonly Dictionary<uint, string?> itemOwnerNames = new();
    public int ItemOwnerNameRevision { get; private set; }

    public string ItemOwnerName(int id) => id > 0 && User is { } user && user.RealId == (uint)id ? user.Name :
        id > 0 && itemOwnerNames.GetValueOrDefault((uint)id) is { Length: > 0 } name ? name : "未知角色";

    public bool RequestItemOwnerName(int id) => User != null && id > 0 && User.RealId != (uint)id && itemOwnerNames.TryAdd((uint)id, null);

    private void ItemOwnerNameReceived(S.UserName packet)
    {
        // 原UserName使用角色数据库编号；只接受本次角色已查询的名称，不改场景身份。
        if (User == null || !itemOwnerNames.TryGetValue(packet.Id, out var oldName) || oldName == packet.Name) return;
        itemOwnerNames[packet.Id] = packet.Name;
        ItemOwnerNameRevision++; InventoryRevision++;
    }

    private void ResetItemOwnerNames()
    {
        itemOwnerNames.Clear(); ItemOwnerNameRevision++;
    }
}
