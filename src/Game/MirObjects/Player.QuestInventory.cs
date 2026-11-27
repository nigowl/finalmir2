using Server.Helpers;
using S = ServerPackets;

namespace Server.MirObjects;

public partial class PlayerObject
{
    internal bool QuestInventoryDirty;
    public IEnumerable<UserItem> CarriedItems => Info.QuestInventory.Concat(Info.Inventory).Where(item => item != null);

    public void ConsumeCarriedItem(UserItem item, ushort count)
    {
        var bag = Info.QuestInventory.Contains(item) ? Info.QuestInventory : Info.Inventory;
        var slot = Array.IndexOf(bag, item);
        if (slot < 0 || count == 0 || count > item.Count) throw new InvalidOperationException("物品消耗与实际库存不一致。");
        Enqueue(bag == Info.QuestInventory ? new S.DeleteQuestItem { UniqueID = item.UniqueID, Count = count } :
            new S.DeleteItem { UniqueID = item.UniqueID, Count = count });
        if (count == item.Count) bag[slot] = null; else item.Count -= count;
        RefreshBagWeight();
    }

    public void OrganizeQuestInventory(bool notify = true)
    {
        QuestInventoryDirty = false;
        if (!notify) InventoryStacks.Compact(Info.QuestInventory);
        var moved = false;
        // 配方引用的普通装备/药品仍放普通包；旧分类变更后只迁回能容纳的原物品。
        for (var index = 0; index < Info.QuestInventory.Length; index++)
        {
            var item = Info.QuestInventory[index];
            if (item == null || MaterialInventoryRules.Contains(item.Info, Envir)) continue;
            if (!InventoryStacks.CanAdd(Info.Inventory, new[] { item }))
            { ReceiveChat("背包已满，材料栏中的普通物品暂时保留，请腾出背包空间后重登整理。", ChatType.System); continue; }
            var gained = item.Clone();
            AddItem(item); Info.QuestInventory[index] = null; moved = true;
            if (!notify) continue;
            Enqueue(new S.DeleteQuestItem { UniqueID = gained.UniqueID, Count = gained.Count });
            Enqueue(new S.GainedItem { Item = gained });
        }
        for (var index = 0; index < Info.Inventory.Length; index++)
        {
            var item = Info.Inventory[index];
            if (!MaterialInventoryRules.Contains(item?.Info, Envir)) continue;
            if (!InventoryStacks.CanAdd(Info.QuestInventory, new[] { item }))
            { ReceiveChat("材料物品栏已满，原物品保留在背包，请腾出材料栏空间后再整理。", ChatType.System); continue; }
            var gained = item.Clone();
            AddQuestItem(item); Info.Inventory[index] = null; moved = true;
            if (!notify) continue;
            // 先让原仓库/交易回执归约，再归类，避免客户端同时显示两个副本。
            Enqueue(new S.DeleteItem { UniqueID = gained.UniqueID, Count = gained.Count });
            Enqueue(new S.GainedQuestItem { Item = gained });
        }
        if (moved) RefreshBagWeight();
        if (notify)
            foreach (var quest in CurrentQuests.Where(quest => quest.ItemTaskCount.Count > 0))
            {
                var before = quest.ItemTaskCount.Select(task => task.Count).ToArray();
                RefreshQuestMaterials(quest);
                if (!before.SequenceEqual(quest.ItemTaskCount.Select(task => task.Count))) SendUpdateQuest(quest, QuestState.Update);
            }
        QuestInventoryDirty = false;
    }

    private void RefreshQuestMaterials(Server.MirDatabase.QuestProgressInfo quest)
    {
        quest.ProcessItem(CarriedItems.ToArray());
        if (quest.ItemTaskCount.Any(task => !task.Complete)) quest.EndDateTime = DateTime.MaxValue;
    }
}
