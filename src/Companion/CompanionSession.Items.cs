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
    {"hero-create"=>"创建英雄", "hero-seal"=>"激活英雄", "hero-summon"=>"召唤英雄", "hero-equip"=>"英雄装备", "hero-remove"=>"英雄卸装", "hero-mode"=>"英雄模式", "hero-use"=>"英雄使用", "appraise"=>"装备鉴定", "public-buy"=>"商城购买", "recycle"=>"回收", "buy"=>"购买", "sell"=>"出售", "equip"=>"装备", "remove"=>"卸下", "repair"=>"修理", "use"=>"使用", "quest"=>"任务操作",
        "storage-store"=>"存仓", "storage-take"=>"取回", "storage-split"=>"仓库拆分", "storage-password" or "storage-unlock"=>"仓库认证", "guild-create"=>"建会", "guild-join"=>"入会", "guild-register"=>"攻城报名", _=>"资产操作"};
    private async Task Asset(Packet packet, string kind, ulong id)
    {
        pendingAsset=kind; assetId=id; assetSent=Environment.TickCount64; receivedPurchase=false;purchasePaid=false;
        await connection.Send(packet); nextAction=assetSent+500;
    }
    private async Task ApplyAssetResult(Packet packet)
    {
        if (pendingAsset == null) return;
        if (await PublicServiceResult(packet)) return;
        if(await StorageResult(packet))return;
        bool? success = packet switch
        {
            S.Chat p when pendingAsset=="buy" && !purchasePaid && !receivedPurchase && p.Type==ChatType.System &&
                p.Message.StartsWith("购买失败：",StringComparison.Ordinal) => false,
            S.NPCResponse p when pendingAsset == "guild-register" && p.Page.Contains("攻城报名成功："+assetId) => true,
            S.NPCResponse p when pendingAsset == "guild-register" && p.Page.Contains("攻城报名拒绝："+assetId) => false,
            S.EquipItem p when pendingAsset=="equip" && p.Grid==MirGridType.Inventory && p.UniqueID==assetId => p.Success,
            S.RemoveItem p when pendingAsset=="remove" && p.Grid==MirGridType.Inventory && p.UniqueID==assetId => p.Success,
            S.EquipItem p when pendingAsset=="hero-equip" && p.Grid==MirGridType.HeroInventory && p.UniqueID==assetId => p.Success,
            S.RemoveItem p when pendingAsset=="hero-remove" && p.Grid==MirGridType.HeroInventory && p.UniqueID==assetId => p.Success,
            S.EquipmentAppraisalResult p when pendingAsset=="appraise" && p.UniqueID==assetId => p.Success,
            S.UseItem p when pendingAsset=="hero-use" && p.Grid==MirGridType.HeroInventory && p.UniqueID==assetId => p.Success,
            S.UseItem p when pendingAsset=="hero-seal" && p.Grid==MirGridType.Inventory && p.UniqueID==assetId => p.Success,
            S.UpdateHeroSpawnState { State:HeroSpawnState.Summoned } when pendingAsset=="hero-summon" => true,
            S.SetHeroBehaviour p when pendingAsset=="hero-mode" && p.Behaviour==requestedHeroMode => true,
            S.UseItem p when pendingAsset is "use" or "route-town" && p.Grid==MirGridType.Inventory && p.UniqueID==assetId => p.Success,
            S.SellItem p when pendingAsset=="sell" && p.UniqueID==assetId => p.Success,
            S.ItemRepaired p when pendingAsset=="repair" && p.UniqueID==assetId => true,
            S.ChangeQuest p when pendingAsset=="quest" && p.Quest.Id==(int)assetId => true,
            S.GuildStatus p when pendingAsset is "guild-create" or "guild-join" && p.GuildName.Equals(pendingGuildName,StringComparison.OrdinalIgnoreCase) => true,
            _ => null
        };
        if (pendingAsset=="buy")
        {
            var received = packet switch { S.GainedItem item => item.Item, S.GainedQuestItem material => material.Item, _ => null };
            if (received?.ItemIndex==boughtItem) receivedPurchase=true;
            // 原购买协议没有请求号，只接受该串行请求的指定商品与扣款回执；超时不重发。
            if (packet is S.LoseGold)purchasePaid=true;
            if(receivedPurchase&&purchasePaid)success=true;
        }
        if (success == null) return;
        if (pendingAsset == "guild-register" && success.Value && Profile.Chat && state.InGuild && state.Guild?.MyRankId == 0)
            await connection.Send(new C.Chat { Message="!~已完成本会攻城报名，大家准备药品装备，临近原定战期按集结路线出征！" });
        if (pendingAsset=="appraise" && success.Value &&
            state.User!.Inventory.FirstOrDefault(item=>item?.UniqueID==assetId) is not {Identified:true})
            throw new InvalidDataException("鉴定成功回执与原装备状态不一致，停止资产操作。");
        // RepairItem 是原服先发的解锁包，成功还会跟随 ItemRepaired，不能提前当成拒绝。
        if (!success.Value && pendingAsset!="buy") (pendingAsset.StartsWith("hero-",StringComparison.Ordinal)?heroRefused:refused).Add(assetId);
        var restoreSlot=pendingAsset=="repair" && success.Value?repairSlot:-1;
        if (pendingAsset=="repair" || pendingAsset is "remove" or "hero-remove" && !success.Value) {repairItem=0;repairSlot=-1;}
        var heroWear = pendingAsset == "hero-equip" && success.Value ? state.Hero?.Equipment.FirstOrDefault(item => item?.UniqueID == assetId) : null;
        await report(new("asset",Profile.Id,Text:pendingAsset+":"+(success.Value?"成功":"拒绝"),
            Reason: packet is S.Chat rejected ? rejected.Message : heroWear == null ? "" : $"英雄已装备{heroWear.Info.FriendlyName}（定义 {heroWear.ItemIndex}，编号 {assetId}）"));
        if (pendingAsset=="buy" && !success.Value)
        {
            await report(new("goal-blocked",Profile.Id,Reason:((S.Chat)packet).Message,Text:Knowledge?.Goal?.Key??""));
            goods=null;page=[];pendingNpc=npcId=0;
        }
        pendingAsset=null;
        if(restoreSlot>=0)
            await Asset(new C.EquipItem{Grid=repairGrid,UniqueID=assetId,To=restoreSlot},repairGrid==MirGridType.HeroInventory?"hero-equip":"equip",assetId);
    }
    private long nextRouteTown;
    private async Task<bool> UseTownOnRoute(long now)
    {
        if (now < nextRouteTown || !CompanionSafety.TownAllowed(Knowledge) || Knowledge?.TownOnRoute != true ||
            state.Trading || state.StorageActive || state.Me?.Dead != false || now-monsterAttackedAt<5000 ||
            preparedSkill!=null || pendingSkill!=Spell.None) return false;
        var scrolls = state.User!.Inventory.Where(i => i?.Info is {Type:ItemType.Scroll,Shape:1} &&
            i.Count>0 && !refused.Contains(i.UniqueID) && Usable(i.Info)).ToArray();
        if (scrolls.Sum(i => i!.Count) <= 1) return false;
        var scroll = scrolls[0]!;
        nextRouteTown=now+60000;
        await Asset(new C.UseItem{Grid=MirGridType.Inventory,UniqueID=scroll.UniqueID},"route-town",scroll.UniqueID);
        return true;
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
            if (!Usable(item.Info) || item.UniqueID==Knowledge?.Goal?.AppraisalItemId && EquipmentAppraisal.Pending(item)) continue;
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
