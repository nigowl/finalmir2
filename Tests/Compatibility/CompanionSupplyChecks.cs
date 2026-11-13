using Mir2.Companions;
using Mir2.Companion;
using System.Text.Json;

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
    }
}
