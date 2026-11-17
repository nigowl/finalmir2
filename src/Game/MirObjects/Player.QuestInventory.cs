using Server.Helpers;
using S = ServerPackets;

namespace Server.MirObjects;

public partial class PlayerObject
{
    internal bool QuestInventoryDirty;

    public void OrganizeQuestInventory(bool notify = true)
    {
        QuestInventoryDirty = false;
        var moved = false;
        for (var index = 0; index < Info.Inventory.Length; index++)
        {
            var item = Info.Inventory[index];
            if (item?.Info?.Type != ItemType.Quest) continue;
            if (!InventoryStacks.CanAdd(Info.QuestInventory, new[] { item }))
            { ReceiveChat("任务物品栏已满，原任务物品保留在背包，请完成任务后再整理。", ChatType.System); continue; }
            var gained = item.Clone();
            AddQuestItem(item); Info.Inventory[index] = null; moved = true;
            if (!notify) continue;
            // 先让原仓库/交易回执归约，再移动到任务栏，避免客户端同时显示两个副本。
            Enqueue(new S.DeleteItem { UniqueID = gained.UniqueID, Count = gained.Count });
            Enqueue(new S.GainedQuestItem { Item = gained });
        }
        if (moved) RefreshBagWeight();
        QuestInventoryDirty = false;
    }
}
