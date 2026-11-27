using C = ClientPackets;
using S = ServerPackets;

namespace Server.MirObjects;

public partial class PlayerObject
{
    private S.RecycleItemResult lastItemRecycle;

    public void RecycleItem(C.RecycleItem request)
    {
        if (lastItemRecycle != null && request.RequestId == lastItemRecycle.RequestId &&
            request.UniqueID == lastItemRecycle.UniqueID && request.Count == lastItemRecycle.Count)
        { Enqueue(lastItemRecycle); return; }
        var result = new S.RecycleItemResult { RequestId = request.RequestId, UniqueID = request.UniqueID, Count = request.Count };
        void Reject(string message) { result.Message = "回收失败：" + message; Enqueue(result); }
        if (request.RequestId <= 0 || lastItemRecycle != null && request.RequestId <= lastItemRecycle.RequestId)
        { Reject("请求已过期，请重新连接后核对。"); return; }
        lastItemRecycle = result;
        if (Dead || TradePartner != null || ItemRentalPartner != null) { Reject("当前状态不能回收。"); return; }
        var inventory = Info.QuestInventory.Any(item => item?.UniqueID == request.UniqueID) ? Info.QuestInventory : Info.Inventory;
        var index = Array.FindIndex(inventory, item => item?.UniqueID == request.UniqueID);
        if (index < 0 || request.Count == 0 || request.Count > inventory[index].Count) { Reject("物品或数量无效。"); return; }
        var item = inventory[index];
        var reason = ItemRecycling.Rejection(item, allowUnidentified: true);
        if (reason.Length > 0) { Reject(reason); return; }
        if (CurrentQuests.Any(quest => quest.Info.ItemTasks.Concat(quest.Info.CarryItems).Any(task => task.Item.Index == item.ItemIndex)))
        { Reject("当前任务需要此物品，已保留。"); return; }
        var portion = item.Clone(); portion.Count = request.Count;
        var gold = portion.RecyclePrice();
        if (gold == 0 || !CanGainGold(gold)) { Reject("回收价值无效或金币已达上限。"); return; }
        ConsumeCarriedItem(item, request.Count);
        GainGold(gold); RefreshBagWeight();
        result.Success = true; result.Gold = gold;
        result.Message = $"回收成功：{item.Info.FriendlyName} ×{request.Count}，获得 {gold:N0} 金币。";
        Enqueue(result);
    }
}
