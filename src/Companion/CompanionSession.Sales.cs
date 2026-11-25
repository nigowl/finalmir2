using C = ClientPackets;
using Mir2.Companions;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private async Task<bool> SellSurplus()
    {
        var item=SaleItem(true);
        if(item==null)
        {
            sellPage=false;
            return false;
        }
        await Status("出售中",item.Info.Type==ItemType.Meat?"出售非任务普通肉类以补充装备和药品资金":"出售普通余货，保留备装、任务/配方用品及基础材料储备");
        await Asset(new C.SellItem{UniqueID=item.UniqueID,Count=item.Count},"sell",item.UniqueID);
        return true;
    }

    private UserItem? SaleItem(bool atNpc=false)
    {
        var user=state.User!;
        var protectedItems=Knowledge?.Quests.Where(quest=>state.Quests.ContainsKey(quest.Id)).SelectMany(quest=>quest.ItemIds).ToHashSet()??[];
        ProtectHeroItems(protectedItems);
        var caster=user.Magics.Any(magic=>Crystal.Native.ClientAutomation.IsSingleSkill(magic.Spell)&&magic.Spell!=Spell.None&&!Crystal.Native.ClientAutomation.IsAttackSkill(magic.Spell));
        var spare=CompanionInventoryRules.SurplusArmour(user.Inventory,user.Equipment[1],protectedItems,user.Level)
            .Concat(CompanionInventoryRules.SurplusWeapons(user.Inventory,user.Equipment[0],user.Class,user.Gender,user.Level,caster,protectedItems))
            .Select(item=>new KnownSale(item.UniqueID,item.Count)).Concat(CompanionInventoryRules.SurplusMaterials(user.Inventory,protectedItems,user.Level))
            .Concat(user.Inventory.Where(item=>CompanionInventoryRules.PlainMeat(item,protectedItems,user.Level)).Select(item=>new KnownSale(item.UniqueID,item.Count)))
            .ToDictionary(sale=>sale.UniqueId,sale=>sale.Count);
        foreach(var sale in Knowledge?.Goal?.SellItems??[])
        {
            var item=user.Inventory.FirstOrDefault(item=>item?.UniqueID==sale.UniqueId);
            if(item==null||item.UniqueID==repairItem||refused.Contains(item.UniqueID)||atNpc&&!Trades(item.Info)||!spare.TryGetValue(item.UniqueID,out var count))continue;
            var copy=item.Clone();copy.Count=Math.Min(count,sale.Count);if(copy.Count>0)return copy;
        }
        if(unloadOnly)return null;
        return user.Inventory.FirstOrDefault(item=>CompanionInventoryRules.PlainMeat(item,protectedItems,user.Level)&&
            !refused.Contains(item.UniqueID)&&(!atNpc||Trades(item.Info)));
    }
}
