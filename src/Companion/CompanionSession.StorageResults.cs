using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private async Task<bool> StorageResult(Packet packet)
    {
        var kind=pendingAsset;
        if(kind?.StartsWith("storage-",StringComparison.Ordinal)!=true)return false;
        bool? success=packet switch
        {
            S.StorageUnlockResult p when kind=="storage-unlock"=>p.Result is 0 or 4,
            S.StoragePasswordResult p when kind=="storage-password"&&!p.Removing=>p.Result==4,
            S.StoreItem p when kind=="storage-store"&&p.From==storageFrom&&p.To==storageTo=>p.Success,
            S.TakeBackItem p when kind=="storage-take"&&p.From==storageFrom&&p.To==storageTo=>p.Success,
            _=>null
        };
        if(kind=="storage-split")return await StorageSplitResult(packet);
        if(success==null)return true;
        if(!success.Value)throw new InvalidOperationException(kind is "storage-unlock" or "storage-password"
            ?"原服拒绝仓库认证，已停止；请人工核对密码与NPC会话，未重置已有密码。":"原服拒绝仓库存取，已停止核对物品与格位，不会重发。");
        if(kind is "storage-store" or "storage-take")
        {
            var source=kind=="storage-store"?state.User!.Inventory:state.Storage;
            var target=kind=="storage-store"?state.Storage:state.User!.Inventory;
            if(source[storageFrom]!=null||target[storageTo]?.UniqueID!=assetId||target[storageTo]?.Count!=storageCount)
                throw new InvalidDataException("仓库成功回执与物品编号/数量不一致，已停止核对，不会重发。");
        }
        pendingAsset=null;StorageActivity();
        await report(new("asset",Profile.Id,Text:kind+":成功"));return true;
    }
    private async Task<bool> StorageSplitResult(Packet packet)
    {
        if(packet is S.SplitItem1 acknowledgement && acknowledgement.Grid==MirGridType.Storage&&acknowledgement.UniqueID==assetId&&acknowledgement.Count==storageCount)
        {
            if(!acknowledgement.Success)throw new InvalidOperationException("原服拒绝仓库拆分，已停止核对，不会重发。");
            splitAcknowledged=true;return true;
        }
        if(packet is not S.SplitItem {Grid:MirGridType.Storage,Item:{ } item})return true;
        var original=state.Storage.FirstOrDefault(i=>i?.UniqueID==assetId);
        var added=state.Storage.FirstOrDefault(i=>i?.UniqueID==item.UniqueID);
        if(!splitAcknowledged||item.UniqueID==assetId||item.ItemIndex!=storageDefinition||item.Count!=storageCount||
            original?.Count!=storageOriginalCount-storageCount||added?.Count!=storageCount)
            throw new InvalidDataException("仓库拆分回执不完整或数量/编号不一致，已停止核对，不会重发。");
        pendingAsset=null;StorageActivity();await report(new("asset",Profile.Id,Text:"storage-split:成功"));
        if(!await StorageTransfer(added,true))throw new InvalidOperationException("仓库拆分已确认，取回条件已变化，已停止并保留仓库物品。");
        return true;
    }
}
