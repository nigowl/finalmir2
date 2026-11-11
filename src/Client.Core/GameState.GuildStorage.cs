using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    public GuildStorageItem[] GuildStorage = new GuildStorageItem[112];
    public bool GuildStorageLoaded;
    public int GuildStorageRevision;

    private void ClearGuildStorage()
    {
        GuildStorage = new GuildStorageItem[112]; GuildStorageLoaded = false;
        GuildStorageRevision++;
    }
    private void ApplyGuildStorage(Packet packet)
    {
        if (!InGuild) return;
        switch (packet)
        {
            case S.GuildStorageList p:
                GuildStorage = p.Items ?? new GuildStorageItem[112]; GuildStorageLoaded = true;
                foreach (var entry in GuildStorage) Bind(entry?.Item);
                GuildStorageRevision++; break;
            case S.GuildStorageItemChange p:
                var own = p.User == User?.RealId;
                switch (p.Type)
                {
                    case 0 when (uint)p.To < GuildStorage.Length && p.Item?.Item != null:
                        Bind(p.Item.Item); GuildStorage[p.To] = p.Item;
                        if (own && ValidSlot(User?.Inventory, p.From)) User!.Inventory[p.From] = null!;
                        break;
                    case 1 when (uint)p.From < GuildStorage.Length:
                        if (own && ValidSlot(User?.Inventory, p.To)) User!.Inventory[p.To] = GuildStorage[p.From]?.Item!;
                        GuildStorage[p.From] = null!; break;
                    case 2 when (uint)p.From < GuildStorage.Length && (uint)p.To < GuildStorage.Length && p.From != p.To && p.Item?.Item != null:
                        Bind(p.Item.Item); GuildStorage[p.From] = GuildStorage[p.To]; GuildStorage[p.To] = p.Item; break;
                    case >= 3: Message?.Invoke("行会仓库操作未完成，请检查安全区、职位权限、物品限制及目标空位。"); break;
                }
                GuildStorageRevision++; InventoryRevision++; break;
            case S.GuildStorageGoldChange p when Guild != null:
                Guild.Gold = p.Type is 0 or 3 ? (uint)Math.Min(uint.MaxValue, (ulong)Guild.Gold + p.Amount) : Guild.Gold - Math.Min(Guild.Gold, p.Amount);
                GuildRevision++; break;
        }
    }
}
