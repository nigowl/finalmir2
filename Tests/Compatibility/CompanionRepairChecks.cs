using Mir2.Companions;

internal static class CompanionRepairChecks
{
    public static void Run(Action<bool,string> check)
    {
        var info=new ItemInfo{Type=ItemType.Weapon,Price=1000,Durability=10000};
        var item=new UserItem(info){CurrentDura=1000,MaxDura=10000};
        check(CompanionSupplyRules.NeedsRepair(item,10000),"低耐久已穿装备未触发修理");
        item.CurrentDura=0;
        check(CompanionSupplyRules.NeedsRepair(item,10000),"零耐久装备没有恢复路径");
        item.CurrentDura=5000;
        check(!CompanionSupplyRules.NeedsRepair(item,10000),"半耐久仍反复修理");
        item.CurrentDura=10000;
        check(!CompanionSupplyRules.NeedsRepair(item,10000),"完整耐久重复支出修理费");
        item.CurrentDura=1000;info.Bind=BindMode.DontRepair;
        check(!CompanionSupplyRules.NeedsRepair(item,10000),"禁修理装备仍规划修理");
        info.Bind=BindMode.None;
        check(!CompanionSupplyRules.NeedsRepair(item,0),"无金币仍规划修理");
        var price=item.RepairPrice();
        check(price>0&&!CompanionSupplyRules.NeedsRepair(item,price*2),"修理没有保留半数金币预算");
        check(CompanionSupplyRules.NeedsRepair(item,price*2+2),"预算足够时仍阻止修理");
        item.MaxDura=0;
        check(!CompanionSupplyRules.NeedsRepair(item,10000),"无耐久物品误作破损装备");
        check(!CompanionSupplyRules.NeedsRepair(null,10000),"空装备格触发修理");
    }
}
