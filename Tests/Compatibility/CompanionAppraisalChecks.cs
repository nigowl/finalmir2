using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CompanionAppraisalChecks
{
    public static void Run(Action<bool,string> check)
    {
        var info=new ItemInfo{Index=901,Type=ItemType.Weapon,Price=20000,Durability=1000,
            Stats=new Stats{[Stat.MinDC]=3,[Stat.MaxDC]=5}};
        var item=new UserItem(info){UniqueID=901,EquipmentQuality=EquipmentGrade.Normal,Identified=false,CurrentDura=1000,MaxDura=1000};
        var worn=new UserItem(info){UniqueID=902,CurrentDura=1000};var equipment=new UserItem[14];equipment[0]=worn;
        bool Worth()=>CompanionInventoryRules.WorthAppraising(item,equipment,MirClass.Warrior,MirGender.Male,25,false);
        check(Worth(),"同基础属性的未鉴定装备没有按公开加成评估提升潜力");
        item.EquipmentQuality=EquipmentGrade.Supreme;check(Worth()&&!item.Identified&&item.AddedStats.Values.All(pair=>pair.Value==0),"鉴定规划偷看品质或提前增加属性");
        worn.AddedStats[Stat.MaxDC]=1000;check(!Worth(),"明显弱于已穿装备的未鉴定物仍浪费金币鉴定");worn.AddedStats.Clear();
        item.Identified=true;check(!Worth(),"已鉴定物被重复鉴定");item.Identified=false;
        item.CurrentDura=0;check(!Worth(),"零耐久装备抢占修理");item.CurrentDura=1000;
        check(CompanionHeroRules.CombatMode(35,100,HeroBehaviour.Attack)==HeroBehaviour.Follow,"低血英雄没有转为跟随");
        check(CompanionHeroRules.CombatMode(65,100,HeroBehaviour.Follow)==HeroBehaviour.Follow&&
            CompanionHeroRules.CombatMode(66,100,HeroBehaviour.Follow)==HeroBehaviour.Attack,"英雄模式没有恢复阈值或反复抖动");

        var world=Envir.Main;var scripts=world.Scripts;var index=world.ScriptIndex;
        try
        {
            world.Scripts=[];
            var map=new Map(new MapInfo{Index=701});
            var player=new PlayerObject{Info=new CharacterInfo{Index=88,Level=25},Account=new AccountInfo{Gold=10000},
                Stats=new Stats{[Stat.HP]=100},HP=100,CurrentMap=map};
            player.Info.Equipment[0]=item;
            player.Info.Inventory[0]=new(new ItemInfo{Type=ItemType.Potion,Stats=new Stats{[Stat.HP]=30}}){Count=15};
            var npc=new NPCObject(new NPCInfo{Name="隔离鉴定师",FileName="CompanionAppraisalAbsent"}){CurrentMap=map};
            var script=NPCScript.Get(npc.ScriptID);script.NPCPages=[new(NPCScript.MainKey),new(EquipmentAppraisal.Page)];
            var routes=new Dictionary<int,KnownExit?>{[701]=null};var profile=new CompanionProfile();
            KnownGoal? Choose(ISet<string>? blocked=null)=>CompanionAppraisals.Choose(player,profile,[npc],routes,blocked);
            check(Choose() is {AppraisalItemId:901,NpcId:var id}&&id==npc.ObjectID,"已穿有潜力装备没有找到原鉴定服务");
            check(!item.Identified&&player.Account.Gold==10000,"只读鉴定规划改变了装备或金币");
            check(Choose(new HashSet<string>{"appraise/901"})==null,"暂排除的鉴定请求被重复安排");
            player.Account.Gold=3999;check(Choose()==null,"鉴定没有保留半数金币作为补给预算");
            player.Account.Gold=4000;check(Choose()!=null,"可负担的真实鉴定费用边界错误");
            player.Info.Level=9;npc.Info.Location=new(100,100);
            check(Choose()==null,"新手为鉴定普通基础装备跨图跑长途，抢占升到10级");
            player.Info.Level=25;npc.Info.Location=new(0,0);
            player.Info.Inventory[0]=null;check(Choose()==null,"缺红药时仍先鉴定");
            player.Info.Inventory[0]=new(new ItemInfo{Type=ItemType.Potion,Stats=new Stats{[Stat.HP]=30}}){Count=15};
            player.Info.Inventory[1]=new(new ItemInfo{Type=ItemType.Scroll,Shape=EquipmentAppraisal.ScrollShape}){UniqueID=903};
            player.Account.Gold=0;check(Choose() is {NpcId:0,AppraisalItemId:901},"已有鉴定卷轴仍要求额外金币或跨图");
            player.HP=65;check(Choose()==null,"低血时鉴定抢占保命");player.HP=100;
            item.SoulBoundId=89;check(Choose()==null,"自动鉴定使用他人绑定装备");
        }
        finally {world.Scripts=scripts;world.ScriptIndex=index;}
    }
}
