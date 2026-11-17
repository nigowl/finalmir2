using C = ClientPackets;
using S = ServerPackets;
using Crystal.Native;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private string? pendingAsset;
    private ulong assetId;
    private int boughtItem;
    private bool receivedPurchase;
    private bool purchasePaid;
    private long assetSent;
    private readonly HashSet<ulong> refused = [];
    private static string AssetName(string kind) => kind switch
    {"buy"=>"购买", "sell"=>"出售", "equip"=>"装备", "remove"=>"卸下", "repair"=>"修理", "use"=>"使用", "quest"=>"任务操作",
        "storage-store"=>"存仓", "storage-take"=>"取回", "storage-split"=>"仓库拆分", "storage-password" or "storage-unlock"=>"仓库认证", "guild-create"=>"建会", "guild-join"=>"入会", _=>"资产操作"};
    private async Task Asset(Packet packet, string kind, ulong id)
    {
        pendingAsset=kind; assetId=id; assetSent=Environment.TickCount64; receivedPurchase=false;purchasePaid=false;
        await connection.Send(packet); nextAction=assetSent+500;
    }
    private async Task ApplyAssetResult(Packet packet)
    {
        if (pendingAsset == null) return;
        if(await StorageResult(packet))return;
        bool? success = packet switch
        {
            S.EquipItem p when pendingAsset=="equip" && p.UniqueID==assetId => p.Success,
            S.RemoveItem p when pendingAsset=="remove" && p.UniqueID==assetId => p.Success,
            S.UseItem p when pendingAsset=="use" && p.UniqueID==assetId => p.Success,
            S.SellItem p when pendingAsset=="sell" && p.UniqueID==assetId => p.Success,
            S.ItemRepaired p when pendingAsset=="repair" && p.UniqueID==assetId => true,
            S.ChangeQuest p when pendingAsset=="quest" && p.Quest.Id==(int)assetId => true,
            S.GuildStatus p when pendingAsset is "guild-create" or "guild-join" && p.GuildName.Equals(pendingGuildName,StringComparison.OrdinalIgnoreCase) => true,
            _ => null
        };
        if (pendingAsset=="buy")
        {
            if (packet is S.GainedItem item && item.Item.ItemIndex==boughtItem) receivedPurchase=true;
            // 原购买协议没有请求号，只接受该串行请求的指定商品与扣款回执；超时不重发。
            if (packet is S.LoseGold)purchasePaid=true;
            if(receivedPurchase&&purchasePaid)success=true;
        }
        if (success == null) return;
        // RepairItem 是原服先发的解锁包，成功还会跟随 ItemRepaired，不能提前当成拒绝。
        if (!success.Value) refused.Add(assetId);
        var restoreSlot=pendingAsset=="repair" && success.Value?repairSlot:-1;
        if (pendingAsset=="repair" || pendingAsset=="remove" && !success.Value) {repairItem=0;repairSlot=-1;}
        await report(new("asset",Profile.Id,Text:pendingAsset+":"+(success.Value?"成功":"拒绝")));
        pendingAsset=null;
        if(restoreSlot>=0)
            await Asset(new C.EquipItem{Grid=MirGridType.Inventory,UniqueID=assetId,To=restoreSlot},"equip",assetId);
    }
    private static int Slot(ItemType type) => type switch
    { ItemType.Weapon=>0, ItemType.Armour=>1, ItemType.Helmet=>2, ItemType.Torch=>3, ItemType.Necklace=>4,
      ItemType.Bracelet=>5, ItemType.Ring=>7, ItemType.Amulet=>9, ItemType.Belt=>10, ItemType.Boots=>11, ItemType.Stone=>12, _=>-1 };
    private bool MatchesIdentity(ItemInfo info)
    {
        var user=state.User!;
        return (info.RequiredClass & (RequiredClass)(1<<(int)user.Class))!=0 && (info.RequiredGender & (RequiredGender)(1<<(int)user.Gender))!=0;
    }
    private bool Usable(ItemInfo info) => Mir2.Companions.CompanionSupplyRules.Usable(info,state.User!.Class,state.User.Gender,state.User.Level);
    private int EquipmentValue(UserItem item)
    {
        var caster=Mir2.Companions.CompanionInventoryRules.Caster(state.User!.Magics.Select(m=>m.Spell));
        return Mir2.Companions.CompanionInventoryRules.EquipmentValue(item,state.User.Class,caster);
    }
    private async Task<bool> ImproveEquipment()
    {
        // 卸装到修理回执之间不能穿上备装，否则会顺修备装并丢失原装备槽位。
        if(repairItem!=0)return false;
        var user=state.User!;
        foreach (var item in user.Inventory.Where(i=>i?.Info!=null && i.UniqueID!=repairItem && !refused.Contains(i.UniqueID)))
        {
            if (!Usable(item.Info)) continue;
            var slot=Mir2.Companions.CompanionInventoryRules.UpgradeSlot(item,user.Equipment,user.Class,
                Mir2.Companions.CompanionInventoryRules.Caster(user.Magics.Select(m=>m.Spell)));
            if (slot>=0 && slot<user.Equipment.Length)
            {
                await Asset(new C.EquipItem{Grid=MirGridType.Inventory,UniqueID=item.UniqueID,To=slot},"equip",item.UniqueID); return true;
            }
            if (item.Info.Type==ItemType.Book && !user.Magics.Any(m=>(int)m.Spell==item.Info.Shape))
            { await Asset(new C.UseItem{Grid=MirGridType.Inventory,UniqueID=item.UniqueID},"use",item.UniqueID); return true; }
        }
        return false;
    }
}
