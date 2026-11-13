using Mir2.Companions;

internal static class CompanionInventoryChecks
{
    public static void Run(Action<bool,string> check)
    {
        UserItem Weapon(ulong id,int power=3) => new(new ItemInfo{Index=(int)id,Type=ItemType.Weapon,Price=100,Durability=5000,
            RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None,Stats=new Stats{[Stat.MaxDC]=power},Weight=5})
            {UniqueID=id,CurrentDura=5000,MaxDura=5000};
        var equipped=Weapon(1,8);var backup=Weapon(2,5);var spare=Weapon(3);var upgrade=Weapon(4,10);
        var protectedItems=new HashSet<int>();
        UserItem[] Surplus(params UserItem[] items)=>CompanionInventoryRules.SurplusWeapons(items,equipped,MirClass.Warrior,MirGender.Male,10,false,protectedItems);
        check(Surplus(backup,spare).Select(item=>item.UniqueID).SequenceEqual(new ulong[]{3}),"减负没有保留一把可用备装");
        check(!Surplus(upgrade,backup,spare).Contains(upgrade),"减负卖掉了当前可升级的装备");
        check(CompanionInventoryRules.SurplusWeapons([spare],null,MirClass.Warrior,MirGender.Male,10,false,protectedItems).Length==0,"未装备武器时仍出售库存武器");
        check(Surplus(spare).Length==0,"卖掉了唯一备装");
        protectedItems.Add(spare.ItemIndex);
        check(Surplus(backup,spare).Length==0,"任务收集/携带物品被当成普通余货");protectedItems.Clear();
        var protections=new Action<UserItem>[]
        {
            item=>item.Info.Bind=BindMode.DontSell,item=>item.Info.RequiredAmount=11,item=>item.Info.Grade=ItemGrade.Rare,
            item=>item.SoulBoundId=1,item=>item.RentalInformation=new RentalInformation(),item=>item.AddedStats[Stat.MaxDC]=1,
            item=>item.GemCount=1,item=>item.RefinedValue=RefinedValue.DC,item=>item.RefineAdded=1,
            item=>item.Awake.Type=AwakeType.DC,item=>item.Slots=[Weapon(55)],item=>item.GMMade=true,
            item=>item.Cursed=true,item=>item.Info.NeedIdentify=true,item=>item.Info.CanMine=true,
            item=>item.ExpireInfo=new ExpireInfo(),item=>item.SealedInfo=new SealedInfo(),item=>item.WeddingRing=2,
            item=>item.RefineSuccessChance=10
        };
        foreach(var protect in protections)
        {
            var item=Weapon(6);protect(item);
            check(Surplus(backup,item).Length==0,"减负没有保留绑定/未来等级/稀有/租赁/强化/特殊物品");
        }
        var otherRole=Weapon(7);otherRole.Info.RequiredClass=RequiredClass.Archer;
        check(Surplus(backup,otherRole).Contains(otherRole),"无法出售不适用本职业的普通多余武器");
        check(CompanionInventoryRules.NeedsSpace(80,100,10)&&!CompanionInventoryRules.NeedsSpace(79,100,10)&&
            CompanionInventoryRules.NeedsSpace(0,100,2),"减负阈值或空格检查错误");
        var spellWeapon=Weapon(8);spellWeapon.Info.Stats[Stat.MaxMC]=5;
        check(!CompanionInventoryRules.SurplusWeapons([backup,spellWeapon],equipped,MirClass.Wizard,MirGender.Male,10,true,protectedItems).Contains(spellWeapon),"法师备装未保留魔法攻击武器");
        var material=Weapon(10);material.Info.Type=ItemType.CraftingMaterial;material.Info.StackSize=50;material.Count=11;
        var sales=CompanionInventoryRules.SurplusMaterials([material],protectedItems,10);
        check(sales.Single()==new KnownSale(10,6)&&material.Count==11,"材料未保留5份或规划时直接扣了库存");
        var second=material.Clone();second.UniqueID=11;material.Count=2;second.Count=9;
        check(CompanionInventoryRules.SurplusMaterials([material,second],protectedItems,10).Single()==new KnownSale(11,6),"跨堆叠材料储备计算错误");
        protectedItems.Add(material.ItemIndex);
        check(CompanionInventoryRules.SurplusMaterials([second],protectedItems,10).Length==0,"出售了任务/配方保护材料");protectedItems.Clear();
        foreach(var protect in protections)
        {
            var entry=Weapon(12);entry.Info.Type=ItemType.CraftingMaterial;entry.Count=20;protect(entry);
            check(CompanionInventoryRules.SurplusMaterials([entry],protectedItems,10).Length==0,"普通余料未沿用特殊资产保护");
        }
        material.Info.Type=ItemType.Potion;material.Count=20;
        check(CompanionInventoryRules.SurplusMaterials([material],protectedItems,10).Length==0,"药品被当成普通余料出售");
        check(CompanionInventoryRules.CanCollect(75,100,5,5,false,false)&&!CompanionInventoryRules.CanCollect(75,100,5,6,false,false),"普通拾取未预留20%药品负重");
        check(!CompanionInventoryRules.CanCollect(10,100,2,1,false,false)&&CompanionInventoryRules.CanCollect(10,100,0,1,true,false),"空格储备阻止了可堆叠任务物或放行普通物品");
        check(CompanionInventoryRules.CanCollect(90,100,3,10,true,false)&&!CompanionInventoryRules.CanCollect(90,100,3,11,true,false),"任务拾取没有检查实际最大负重");
        check(CompanionInventoryRules.CanCollect(200,100,0,0,false,true)&&!CompanionInventoryRules.CanCollect(200,100,3,0,false,false),"超重/满包时不能拾金或仍拾普通物品");
        check(!CompanionInventoryRules.CanCollect(0,0,5,0,false,false)&&!CompanionInventoryRules.CanCollect(0,100,5,-1,false,false),"无效拾取容量或负重量没有拒绝");
        check(!CompanionInventoryRules.CanHarvest(80,100,10,false)&&CompanionInventoryRules.CanHarvest(79,100,10,false),"非任务尸体采集绕过了减负阈值");
        check(CompanionInventoryRules.CanHarvest(80,100,2,true)&&!CompanionInventoryRules.CanHarvest(80,100,0,true),"必要任务采集被余量保护阻断或没有空格仍采集");
        check(!CompanionInventoryRules.CanHarvest(0,0,10,true)&&!CompanionInventoryRules.CanHarvest(-1,100,10,false),"未知或无效采集容量未拒绝");
        ArmourChecks(check,protections);
        MeatChecks(check,protections);
    }

    private static void MeatChecks(Action<bool,string> check,Action<UserItem>[] protections)
    {
        UserItem Meat()=>new(new ItemInfo{Index=910,Type=ItemType.Meat,Price=100,Durability=8000})
            {UniqueID=100,CurrentDura=11000,MaxDura=8000};
        var protectedItems=new HashSet<int>();
        check(CompanionInventoryRules.PlainMeat(Meat(),protectedItems,10),"原肉品质超过定义耐久时被误当强化品");
        protectedItems.Add(910);
        check(!CompanionInventoryRules.PlainMeat(Meat(),protectedItems,10),"全版本任务/配方保护肉被列入减负");protectedItems.Clear();
        check(!CompanionInventoryRules.PlainMeat(null,protectedItems,10),"空格被当成可售普通肉");
        var stack=Meat();stack.Count=101;
        check(CompanionInventoryRules.PlainMeat(stack,protectedItems,10),"自动合堆后正常肉无法按原数量减负");
        stack.Count=1000;
        check(!CompanionInventoryRules.PlainMeat(stack,protectedItems,10),"超过定义上限的异常肉堆被列入减负");
        var free=Meat();free.Info.Price=0;
        check(!CompanionInventoryRules.PlainMeat(free,protectedItems,10),"无价格肉被反复规划出售");
        var other=Meat();other.Info.Type=ItemType.Quest;
        check(!CompanionInventoryRules.PlainMeat(other,protectedItems,10),"任务专用物被当普通肉出售");
        foreach(var protect in protections)
        {
            var item=Meat();protect(item);
            check(!CompanionInventoryRules.PlainMeat(item,protectedItems,10),"普通肉未沿用绑定/租赁/强化等特殊保护");
        }
    }

    private static void ArmourChecks(Action<bool,string> check,Action<UserItem>[] protections)
    {
        UserItem Armour(ulong id,ushort dura=5000,int index=900)=>new(new ItemInfo{Index=index,Type=ItemType.Armour,Price=100,Durability=5000,Weight=3})
            {UniqueID=id,CurrentDura=dura,MaxDura=5000};
        var equipped=Armour(1);var backup=Armour(2);var duplicate=Armour(3,3000);var third=Armour(4,4000);
        var protectedItems=new HashSet<int>();
        UserItem[] Surplus(params UserItem?[] items)=>CompanionInventoryRules.SurplusArmour(items,equipped,protectedItems,10);
        check(CompanionInventoryRules.SurplusArmour([backup,duplicate],null,protectedItems,10).Length==0,"未穿衣服时出售了重复衣服");
        check(Surplus(duplicate,backup,third).Select(item=>item.UniqueID).ToHashSet().SetEquals([3ul,4ul]),"重复衣服没有保留耐久最好的一件");
        check(!Surplus(equipped,backup,duplicate).Contains(equipped),"已穿衣服被列入出售");
        check(Surplus(backup).Length==0,"出售了唯一库存衣服");
        check(Surplus(backup,Armour(5,index:901)).Length==0,"把不同定义的衣服当成重复件");
        check(Surplus(backup,duplicate).Contains(duplicate)&&duplicate.Count==1&&duplicate.CurrentDura==3000,"规划出售直接修改了原物品");
        protectedItems.Add(900);check(Surplus(backup,duplicate).Length==0,"任务/配方衣服被列入出售");protectedItems.Clear();
        var changed=Armour(6);changed.MaxDura=6000;
        check(Surplus(backup,changed).Length==0,"额外耐久衣服没有保留");
        var stack=Armour(7);stack.Count=2;
        check(Surplus(backup,stack).Length==0,"未知堆叠衣服被拆成普通重复件出售");
        var helmet=Armour(8);helmet.Info.Type=ItemType.Helmet;
        check(Surplus(null,backup,helmet).Length==0,"出售范围扩展到头盔或空槽处理失败");
        foreach(var protect in protections)
        {
            var item=Armour(9);protect(item);
            check(Surplus(backup,item).Length==0,"重复衣服未沿用绑定/未来等级/稀有/租赁/强化等保护");
        }
    }
}
