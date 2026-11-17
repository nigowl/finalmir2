using Mir2.Companions;
using Mir2.Companion;
using System.Text.Json;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CompanionSupplyChecks
{
    public static void Run(Action<bool, string> check)
    {
        var knowledge=new CompanionKnowledge("test",1,false,false,false,[],[],[],[]);
        check(!CompanionSafety.TownAllowed(null)&&!CompanionSafety.TownAllowed(knowledge),"未知地图或未知回城标志仍使用回城卷");
        knowledge=knowledge with{NoTownTeleport=false};
        check(CompanionSafety.TownAllowed(knowledge),"正常地图没有启用回城保护");
        check(!CompanionSafety.TownAllowed(knowledge with{NoTownTeleport=true}),"禁回城地图仍自动使用卷轴");
        check(!CompanionSafety.TownAllowed(knowledge with{InSafeZone=true}),"安全区低血反复消耗回城卷轴");
        check(CompanionSafety.TownAllowed(knowledge with{NoDrug=true,NoEscape=true}),"把禁药/地牢逃脱标志误当成禁止回城");
        check(JsonSerializer.Deserialize<CompanionKnowledge>(JsonSerializer.Serialize(knowledge with{NoTownTeleport=true}))!.NoTownTeleport,"私有知识丢失独立禁回城标志");
        var missing=JsonSerializer.SerializeToNode(knowledge)!.AsObject();missing.Remove(nameof(CompanionKnowledge.NoTownTeleport));
        check(JsonSerializer.Deserialize<CompanionKnowledge>(missing.ToJsonString())!.NoTownTeleport,"缺失禁回城字段时没有默认阻止自动回城");
        var info = new ItemInfo { Type=ItemType.Potion, Shape=0, RequiredClass=RequiredClass.None, RequiredGender=RequiredGender.None,
            RequiredType=RequiredType.Level, Stats=new Stats{[Stat.HP]=30} };
        var red = new UserItem(info){Count=3};
        check(CompanionSupplyRules.Count([null,red],Stat.HP)==3 && CompanionSupplyRules.Count([red],Stat.MP)==0,"补给统计混用了红药/蓝药或忽略堆叠数量");
        info.Shape=2;
        check(CompanionSupplyRules.Count([red],Stat.HP)==0,"把自动保护无法使用的特殊药品当成库存");
        info.Shape=1; info.Stats[Stat.HP]=-5;
        check(!CompanionSupplyRules.IsPotion(red,Stat.HP),"把伤害药品当成回血补给");
        info.Stats[Stat.HP]=30; red.Count=0;
        check(!CompanionSupplyRules.IsPotion(red,Stat.HP),"零数量药品被当成库存");
        info.RequiredClass=RequiredClass.Wizard; info.RequiredGender=RequiredGender.Female; info.RequiredAmount=5;
        check(CompanionSupplyRules.Usable(info,MirClass.Wizard,MirGender.Female,5) &&
            !CompanionSupplyRules.Usable(info,MirClass.Warrior,MirGender.Female,5) &&
            !CompanionSupplyRules.Usable(info,MirClass.Wizard,MirGender.Male,5) &&
            !CompanionSupplyRules.Usable(info,MirClass.Wizard,MirGender.Female,4),"商店补给没有遵守等级/职业/性别");
        info.RequiredType=RequiredType.MaxDC;
        check(!CompanionSupplyRules.Usable(info,MirClass.Wizard,MirGender.Female,5),"补给把属性门槛当成等级门槛");
        check(CompanionSupplyRules.BudgetDivisor(0)==2 && CompanionSupplyRules.BudgetDivisor(100)==6,"补给预算没有保留人格对应的金币余额");
        red.Count=1;
        check(CompanionSupplyRules.PurchaseLimit(red,40,120,50,0)==1,"无红药仍因节俭跳过能买的一瓶红药");
        check(CompanionSupplyRules.PurchaseLimit(red,40,40,100,0)==1,"恰好一瓶药钱被半数金币预算阻止");
        check(CompanionSupplyRules.PurchaseLimit(red,40,39,50,0)==0,"最低保命采购超出实际金币");
        check(CompanionSupplyRules.PurchaseLimit(red,40,0,50,0)==0,"零金币仍允许采购");
        check(CompanionSupplyRules.PurchaseLimit(red,40,120,50,1)==0,"已有红药仍越过人格预算");
        check(CompanionSupplyRules.PurchaseLimit(red,40,1000,50,0)==12,"正常补给的半数金币数量预算改变");
        check(CompanionSupplyRules.PurchaseLimit(red,40,80,0,1)==1,"正常单价预算边界被拒绝");
        check(CompanionSupplyRules.PurchaseLimit(red,50,49,50,0)==0 && CompanionSupplyRules.PurchaseLimit(red,50,50,50,0)==1,"最低采购忽略实际含税单价");
        foreach(var price in new[]{0d,-1,double.NaN,double.PositiveInfinity})
            check(CompanionSupplyRules.PurchaseLimit(red,price,120,50,0)==0,"无效价格进入最低保命采购");
        info.Shape=2;
        check(CompanionSupplyRules.PurchaseLimit(red,40,120,50,0)==0,"特殊药品错误获得最低保命预算");
        info.Shape=0;info.Stats[Stat.HP]=0;info.Stats[Stat.MP]=30;
        check(CompanionSupplyRules.PurchaseLimit(red,40,120,50,0)==0,"蓝药错误获得最低保命预算");
        info.Type=ItemType.Scroll;info.Shape=1;
        check(CompanionSupplyRules.PurchaseLimit(red,40,120,50,0)==0,"卷轴错误获得最低保命预算");
        info.Type=ItemType.Potion;info.Shape=0;info.Stats[Stat.HP]=30;info.Stats[Stat.MP]=0;
        red.Count=0;check(CompanionSupplyRules.PurchaseLimit(red,40,120,50,0)==0,"无库存商品进入最低保命采购");
        red.Count=2;
        check(CompanionSupplyRules.NeedsRefill([red],Stat.HP,MirClass.Warrior,95),"节俭人格仅剩两瓶仍出发进入洞穴");
        red.Count=(ushort)CompanionSupplyRules.Target(Stat.HP,MirClass.Warrior,95);
        check(red.Count>=12&&!CompanionSupplyRules.NeedsRefill([red],Stat.HP,MirClass.Warrior,95),"达到补给目标后仍要求回店");
        check(!CompanionSupplyRules.NeedsRefill([],Stat.MP,MirClass.Warrior,95)&&
            CompanionSupplyRules.NeedsRefill([],Stat.MP,MirClass.Wizard,95),"战士和法师的蓝药储备未区分");
        var townInfo=new ItemInfo { Type=ItemType.Scroll,Shape=1,RequiredType=RequiredType.Level,RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None };
        var town=new UserItem(townInfo){Count=2};
        check(CompanionSupplyRules.IsTownScroll(town)&&CompanionSupplyRules.TownCount([null,town],MirClass.Taoist,MirGender.Female,15)==2,"回城卷统计未按堆叠及本人可用性计算");
        town.Count=0;check(!CompanionSupplyRules.IsTownScroll(town),"零数量回城卷计入库存");town.Count=2;
        townInfo.Shape=2;check(!CompanionSupplyRules.IsTownScroll(town),"随机卷被当成回城卷");townInfo.Shape=0;
        check(CompanionSupplyRules.TownCount([town],MirClass.Taoist,MirGender.Female,15)==0,"地牢逃脱卷被当成回城储备");townInfo.Shape=1;
        townInfo.RequiredClass=RequiredClass.Warrior;check(CompanionSupplyRules.TownCount([town],MirClass.Taoist,MirGender.Female,15)==0,"将其他职业专用卷计为可用");
        townInfo.RequiredClass=RequiredClass.None;townInfo.RequiredGender=RequiredGender.Male;check(CompanionSupplyRules.TownCount([town],MirClass.Taoist,MirGender.Female,15)==0,"将其他性别专用卷计为可用");
        townInfo.RequiredGender=RequiredGender.None;townInfo.RequiredAmount=16;check(CompanionSupplyRules.TownCount([town],MirClass.Taoist,MirGender.Female,15)==0,"将未达等级卷计为可用");
        townInfo.RequiredAmount=0;townInfo.RequiredType=RequiredType.MaxDC;check(CompanionSupplyRules.TownCount([town],MirClass.Taoist,MirGender.Female,15)==0,"将未核实属性门槛卷计为可用");
        townInfo.RequiredType=RequiredType.Level;town.RentalInformation=new RentalInformation{BindingFlags=BindMode.DontSell};
        check(CompanionSupplyRules.TownCount([town],MirClass.Taoist,MirGender.Female,15)==0,"原服禁止使用的受限租赁卷仍计为可用");
        town.RentalInformation.BindingFlags=BindMode.None;check(CompanionSupplyRules.TownCount([town],MirClass.Taoist,MirGender.Female,15)==2,"无使用绑定的原租赁卷被错误排除");
        VendorRoutes(check);
    }

    private static void VendorRoutes(Action<bool,string> check)
    {
        var world=Envir.Main;var oldMaps=world.MapInfoList;var oldNpcs=world.NPCs;var oldScripts=world.Scripts;var oldIndex=world.ScriptIndex;
        try
        {
            var origin=new Map(new MapInfo{Index=701});var near=new Map(new MapInfo{Index=709});
            var middle=new Map(new MapInfo{Index=720});var far=new Map(new MapInfo{Index=799});
            origin.Info.Movements=[new(){MapIndex=720,Source=new(1,1),Destination=new(2,2)},new(){MapIndex=709,Source=new(3,3),Destination=new(10,10)}];
            middle.Info.Movements=[new(){MapIndex=799,Source=new(3,3),Destination=new(10,10)}];
            world.MapInfoList=[origin.Info,near.Info,middle.Info,far.Info];world.NPCs=[];world.Scripts=[];
            var player=new PlayerObject{Info=new CharacterInfo{Level=10},Account=new AccountInfo{Gold=1000},CurrentMap=origin,CurrentLocation=new(4,4),
                Stats=new Stats{[Stat.HP]=100,[Stat.BagWeight]=1000}};
            var red=new ItemInfo{Index=301,Name="RouteRed",Type=ItemType.Potion,Shape=0,Price=40,StackSize=20,
                RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None,Stats=new Stats{[Stat.HP]=30}};
            NPCObject Vendor(int id,Map map,int x,int y)
            {
                var npc=new NPCObject(new NPCInfo{Index=id,Name="药店"+id,FileName="SupplyRoutesAbsent"+id,Rate=100,Location=new(x,y)}){CurrentMap=map};
                var script=NPCScript.Get(npc.ScriptID);script.NPCPages=[new(NPCScript.BuyKey)];script.Goods=[new UserItem(red)];return npc;
            }
            var distant=Vendor(1,far,10,11);var remoteFar=Vendor(2,near,80,80);var nearest=Vendor(3,near,11,10);
            KnownGoal? Choose(ISet<string>? blocked=null)=>CompanionSupplies.Choose(player,new CompanionProfile(),world.NPCs.ToArray(),CompanionPlanning.Routes(701,world.MapInfoList),blocked);
            check(Choose()?.NpcId==nearest.ObjectID,"跨图补给仍按世界列表先选远店，未比较跨图数及目的图入口距离");
            check(Choose()?.NextExit?.DestinationMapId==709,"药店选择没有保留原公开步行首段");
            check(Choose(new HashSet<string>{$"supply/{nearest.ObjectID}"})?.NpcId==remoteFar.ObjectID,"近店暂不可达后跳过同图另一药店而去更远地图");
            var local=Vendor(4,origin,100,100);check(Choose()?.NpcId==local.ObjectID,"跨图药店抢占当前地图可用药店");world.NPCs.Remove(local);
            NPCScript.Get(nearest.ScriptID).NPCPages=[];NPCScript.Get(remoteFar.ScriptID).NPCPages=[];
            check(Choose()?.NpcId==distant.ObjectID,"没有实际购买页面的近店仍被选中");
            NPCScript.Get(nearest.ScriptID).NPCPages=[new(NPCScript.BuyKey)];
            foreach(var mode in new[]{0,1,2,3})
            {
                var exit=origin.Info.Movements[1];exit.NeedHole=mode==0;exit.NeedMove=mode==1;exit.ConquestIndex=mode==2?1:0;near.Info.RequiredGroup=mode==3;
                check(Choose()?.NpcId==distant.ObjectID,"补给距离计算绕过洞口/条件入口/攻城或组队限制");
            }
            check(player.Account.Gold==1000&&player.Info.Inventory.All(item=>item==null),"补给路线排序修改了真实金币或库存");
        }
        finally{world.MapInfoList=oldMaps;world.NPCs=oldNpcs;world.Scripts=oldScripts;world.ScriptIndex=oldIndex;}
    }
}
