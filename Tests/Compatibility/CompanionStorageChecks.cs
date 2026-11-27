using Crystal.Native;
using Mir2.Companions;
using System.Text.Json;
using S = ServerPackets;

internal static class CompanionStorageChecks
{
    public static void Run(Action<bool,string> check)
    {
        UserItem Material()=>new(new ItemInfo{Index=510,Type=ItemType.CraftingMaterial,Weight=1,Price=10,StackSize=100}){UniqueID=100,Count=40};
        var excluded=new HashSet<int>();
        check(CompanionInventoryRules.StorageMaterial(Material(),excluded,10),"正常配方材料无法暂存");
        excluded.Add(510);check(!CompanionInventoryRules.StorageMaterial(Material(),excluded,10),"全版本任务物被纳入自动仓库");excluded.Clear();
        var protections=new Action<UserItem>[]
        {i=>i.Info.Bind=BindMode.DontStore,i=>i.RentalInformation=new RentalInformation(),i=>i.ExpireInfo=new ExpireInfo(),
            i=>i.SealedInfo=new SealedInfo(),i=>i.SoulBoundId=1,i=>i.GMMade=true,i=>i.AddedStats[Stat.MaxDC]=1,
            i=>i.Info.Grade=ItemGrade.Rare,i=>i.Info.RequiredAmount=11,i=>i.Info.NeedIdentify=true,
            i=>i.Awake.Type=AwakeType.DC,i=>i.GemCount=1,i=>i.Info.Type=ItemType.Weapon,i=>i.Info.Weight=0,
            i=>i.Count=1000,i=>i.Info.Price=0,i=>i.Cursed=true};
        foreach(var protect in protections){var item=Material();protect(item);check(!CompanionInventoryRules.StorageMaterial(item,excluded,10),"仓库放行了受保护/异常/非材料资产");}
        var food=Material();food.Info.Type=ItemType.Food;
        check(CompanionInventoryRules.StorageMaterial(food,excluded,10),"普通食品无法按相同保护暂存");
        check(CompanionInventoryRules.StorageSurplus([Material()],excluded,10,0).Length==0,"满仓仍规划存入");
        check(CompanionInventoryRules.StorageSurplus([Material()],excluded,10,1).Length==0,"只余一格时未预留整叠存入后的拆分格");
        check(CompanionInventoryRules.StorageSurplus([Material()],excluded,10,2).Single().Count==40,"两格可用仍无法整叠暂存再拆回储备");
        var small=Material();small.UniqueID=101;small.Count=5;
        check(CompanionInventoryRules.StorageSurplus([small],excluded,10,2).Length==0,"仅有5份基础材料仍反复存仓");
        check(CompanionInventoryRules.StorageSurplus([Material(),small],excluded,10,1).First().UniqueID==100,"已有5份独立储备时未优先将大叠存入最后一个仓库格");
        SplitChecks(check,Material());
        EquipmentChecks(check);
        var goal=new KnownGoal("service",7,2,3,"原仓库",StorageItems:[new(100,5,true)]);
        var decoded=JsonSerializer.Deserialize<KnownGoal>(JsonSerializer.Serialize(goal));
        check(decoded?.StorageItems?.Single()==new KnownStorage(100,5,true),"私有仓库允许编号/数量/方向在序列化后丢失");
    }
    private static void EquipmentChecks(Action<bool,string> check)
    {
        UserItem Wear(ulong id,ushort dura=8000,ItemType type=ItemType.Necklace)=>new(new ItemInfo{Index=520,Type=type,Price=100,Weight=1,Durability=10000})
            {UniqueID=id,Count=1,MaxDura=10000,CurrentDura=dura};
        var protectedItems=new HashSet<int>();var best=Wear(201,9000);var spare=Wear(202,6000);var third=Wear(203,3000);
        check(CompanionInventoryRules.StorageSurplus([spare,best,third],protectedItems,10,1).Select(i=>i.UniqueID).Order().SequenceEqual(new ulong[]{202,203}),"重复项链未保留最好耐久或不能使用最后一格仓库");
        check(CompanionInventoryRules.StorageSurplus([best],protectedItems,10,3).Length==0,"唯一项链被存仓");
        var other=Wear(204);other.Info.Index=other.ItemIndex=521;
        check(CompanionInventoryRules.StorageSurplus([best,other],protectedItems,10,3).Length==0,"不同定义的唯一备装被存仓");
        foreach(var type in new[]{ItemType.Ring,ItemType.Bracelet})
        {
            var pair=new[]{Wear(301,9000,type),Wear(302,8000,type),Wear(303,7000,type)};
            check(CompanionInventoryRules.StorageSurplus(pair,protectedItems,10,3).Single().UniqueID==303,"戒指/手镯未保留两件最好耐久的同定义备装");
        }
        foreach(var type in new[]{ItemType.Armour,ItemType.Helmet,ItemType.Torch,ItemType.Necklace,ItemType.Belt,ItemType.Boots,ItemType.Stone})
            check(CompanionInventoryRules.StorageSurplus([Wear(1,9000,type),Wear(2,8000,type)],protectedItems,10,3).Single().UniqueID==2,"单槽重复备装未保留最好的一件");
        var protections=new Action<UserItem>[] {i=>i.Info.Bind=BindMode.DontStore,i=>i.RentalInformation=new RentalInformation(),i=>i.ExpireInfo=new ExpireInfo(),
            i=>i.SealedInfo=new SealedInfo(),i=>i.SoulBoundId=1,i=>i.GMMade=true,i=>i.AddedStats[Stat.MaxDC]=1,i=>i.Info.Grade=ItemGrade.Rare,
            i=>i.Info.RequiredAmount=11,i=>i.Info.NeedIdentify=true,i=>i.Awake.Type=AwakeType.DC,i=>i.GemCount=1,i=>i.MaxDura++,
            i=>i.CurrentDura=11000,i=>i.Count=2,i=>i.Cursed=true,i=>i.Info.CanMine=true,i=>i.RefineAdded=1};
        foreach(var protect in protections){var item=Wear(207);protect(item);check(CompanionInventoryRules.StorageSurplus([best,item],protectedItems,10,3).Length==0,"任务/特殊/未来等级或异常备装被存仓");}
        protectedItems.Add(520);check(CompanionInventoryRules.StorageSurplus([best,spare],protectedItems,10,3).Length==0,"全版本任务备装被存仓");protectedItems.Clear();
        foreach(var type in new[]{ItemType.Weapon,ItemType.Amulet,ItemType.Scroll,ItemType.Ore})
            check(CompanionInventoryRules.StorageSurplus([Wear(1,9000,type),Wear(2,8000,type)],protectedItems,10,3).Length==0,"武器/耗材/矿石被扩入备装仓库");
        var light=Wear(209);light.Info.Weight=0;
        check(CompanionInventoryRules.StorageSurplus([best,light],protectedItems,10,1).Single().UniqueID==209,"零重量重复备装无法释放满背包格位");
        check(CompanionInventoryRules.StorageSurplus([best,spare],protectedItems,10,0).Length==0,"满仓仍暂存重复备装");
    }
    private static void SplitChecks(Action<bool,string> check,UserItem material)
    {
        var state=new GameState{User=new S.UserInformation{Inventory=new UserItem[46]}};
        state.Apply(new S.NewItemInfo{Info=material.Info});state.Apply(new S.UserStorage{Storage=new UserItem[160]});
        state.Storage[0]=material;check(state.StorageCapacity==80,"未租用扩展仓库仍允许160格");
        state.Apply(new S.SplitItem1{Grid=MirGridType.Storage,UniqueID=100,Count=5,Success=false});
        check(material.Count==40,"拆分拒绝预扣了源数量");
        state.Apply(new S.SplitItem1{Grid=MirGridType.Storage,UniqueID=100,Count=40,Success=true});
        check(material.Count==40,"异常整叠拆分回执破坏源数量");
        state.Apply(new S.SplitItem1{Grid=MirGridType.Storage,UniqueID=100,Count=5,Success=true});
        var child=material.Clone();child.UniqueID=101;child.Count=5;child.Info=null!;
        state.Apply(new S.SplitItem{Grid=MirGridType.Storage,Item=child});
        check(state.Storage[0].Count==35&&state.Storage[6]?.UniqueID==101&&state.Storage[6].Info==material.Info,"未按原仓库6号格起的顺序绑定拆分物");
        state.Apply(new S.SplitItem{Grid=MirGridType.Storage,Item=child});
        check(state.Storage.Count(i=>i?.UniqueID==101)==1,"重复新编号回执复制了拆分物");
        state.Apply(new S.TakeBackItem{From=6,To=2,Success=false});
        check(state.Storage[6]?.Count==5&&state.User.Inventory[2]==null,"取回拒绝移动了物品");
        state.Apply(new S.TakeBackItem{From=6,To=2,Success=true});
        check(state.Storage[6]==null&&state.User.Inventory[2]?.UniqueID==101&&state.Storage[0].Count+state.User.Inventory[2].Count==40,"拆分取回后的总数量/编号不守恒");
        state.Apply(new S.UserStorage{Storage=new UserItem[160]});state.Storage[0]=material;
        for(var i=2;i<80;i++)state.Storage[i]=material.Clone();
        state.Apply(new S.SplitItem{Grid=MirGridType.Storage,Item=child});
        check(state.Storage[1]?.UniqueID==101&&state.Storage.Skip(80).All(i=>i==null),"拆分物未回填前六格或落入未租用扩展格");
        state.User.HasExpandedStorage=true;check(state.StorageCapacity==160,"有效扩展仓库被限制为80格");
    }
}
