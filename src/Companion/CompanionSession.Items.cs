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
    {"buy"=>"购买", "sell"=>"出售", "equip"=>"装备", "remove"=>"卸下", "repair"=>"修理", "use"=>"使用", "quest"=>"任务操作", _=>"资产操作"};
    private async Task Asset(Packet packet, string kind, ulong id)
    {
        pendingAsset=kind; assetId=id; assetSent=Environment.TickCount64; receivedPurchase=false;purchasePaid=false;
        await connection.Send(packet); nextAction=assetSent+500;
    }
    private async Task ApplyAssetResult(Packet packet)
    {
        if (pendingAsset == null) return;
        bool? success = packet switch
        {
            S.EquipItem p when pendingAsset=="equip" && p.UniqueID==assetId => p.Success,
            S.RemoveItem p when pendingAsset=="remove" && p.UniqueID==assetId => p.Success,
            S.UseItem p when pendingAsset=="use" && p.UniqueID==assetId => p.Success,
            S.SellItem p when pendingAsset=="sell" && p.UniqueID==assetId => p.Success,
            S.ItemRepaired p when pendingAsset=="repair" && p.UniqueID==assetId => true,
            S.ChangeQuest p when pendingAsset=="quest" && p.Quest.Id==(int)assetId => true,
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
        if (pendingAsset=="repair" || pendingAsset=="remove" && !success.Value) repairItem=0;
        await report(new("asset",Profile.Id,Text:pendingAsset+":"+(success.Value?"成功":"拒绝")));
        pendingAsset=null;
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
        var caster=state.User!.Magics.Any(m=>ClientAutomation.IsSingleSkill(m.Spell)&&m.Spell!=Spell.None&&!ClientAutomation.IsAttackSkill(m.Spell));
        var attack = caster && state.User.Class is MirClass.Wizard ? Stat.MaxMC : caster && state.User.Class is MirClass.Taoist ? Stat.MaxSC : Stat.MaxDC;
        return item.Info.Stats[attack]*5 + item.AddedStats[attack]*5 + item.Info.Stats[Stat.MaxAC]*2 + item.Info.Stats[Stat.MaxMAC]*2 + item.Info.Stats[Stat.HP]/5;
    }
    private async Task<bool> ImproveEquipment()
    {
        var user=state.User!;
        foreach (var item in user.Inventory.Where(i=>i?.Info!=null && i.UniqueID!=repairItem && !refused.Contains(i.UniqueID)))
        {
            if (!Usable(item.Info)) continue;
            var slot=Slot(item.Info.Type);
            if (slot>=0 && slot<user.Equipment.Length)
            {
                if (slot==5 && user.Equipment[5]!=null && user.Equipment[6]==null) slot=6;
                if (slot==7 && user.Equipment[7]!=null && user.Equipment[8]==null) slot=8;
                if (user.Equipment[slot]!=null && EquipmentValue(item)<=EquipmentValue(user.Equipment[slot])) continue;
                await Asset(new C.EquipItem{Grid=MirGridType.Inventory,UniqueID=item.UniqueID,To=slot},"equip",item.UniqueID); return true;
            }
            if (item.Info.Type==ItemType.Book && !user.Magics.Any(m=>(int)m.Spell==item.Info.Shape))
            { await Asset(new C.UseItem{Grid=MirGridType.Inventory,UniqueID=item.UniqueID},"use",item.UniqueID); return true; }
        }
        return false;
    }
}
