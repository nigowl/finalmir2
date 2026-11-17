using System.Drawing;
using Crystal.Native;
using Mir2.Companion;
using Mir2.Companions;
using S = ServerPackets;

internal static class CompanionMovementChecks
{
    public static void Run(Action<bool,string> check)
    {
        TravelPolicy(check);
        var path=new CompanionPath();var occupied=new HashSet<Point>();var calls=0;
        bool Clear(Point p){calls++;return p.X>=0&&p.X<30&&p.Y==5;}
        check(path.Next(new(5,5),new(15,5),0,Clear,occupied,run:true)==new Point(7,5),"直线路线没有合并两格");
        calls=0;
        check(path.Next(new(7,5),new(15,5),0,Clear,occupied,run:true)==new Point(9,5)&&calls<=2,"跑步回执后重新搜索整条路线");
        occupied.Add(new(10,5));
        check(path.Next(new(9,5),new(15,5),0,Clear,occupied,run:true)==null,"跑步下一格被占仍进入");
        occupied.Clear();path.Clear();path.Next(new(5,5),new(15,5),0,Clear,occupied);
        occupied.Add(new(7,5));
        check(path.Next(new(5,5),new(15,5),0,Clear,occupied,run:true)==new Point(6,5),"跑步第二格占用时没有退回单步");
        occupied.Clear();path.Clear();
        check(path.Next(new(5,5),new(7,5),1,Clear,occupied,run:true)==new Point(6,5),"跑过NPC交互范围的路线终点");
        path.Clear();var corridor=new HashSet<Point>{new(5,5),new(6,5),new(7,6),new(8,6)};
        check(path.Next(new(5,5),new(8,6),0,corridor.Contains,occupied,run:true)==new Point(6,5),"两格跑步跨过路线弯角");
        path.Clear();path.Next(new(5,5),new(15,5),0,Clear,occupied,run:true);
        check(path.Next(new(5,5),new(15,5),0,Clear,occupied)==new Point(6,5),"被拒的移动被提前推进缓存");
        var state=new GameState{BaseStats=new BaseStats(MirClass.Warrior){Stats=[new(Stat.HP){Base=100},new(Stat.BagWeight){Base=10}]}};
        check(!CompanionSafety.CanRun(state,10000,9500),"未登录时允许跑步");
        state.Apply(new S.UserInformation{ObjectID=1,Class=MirClass.Warrior,HP=100,Inventory=new UserItem[46],Equipment=new UserItem[14],QuestInventory=[]});
        check(CompanionSafety.CanRun(state,10000,9500),"健康轻装且已走起步仍禁止跑步");
        check(!CompanionSafety.CanRun(state,10000,0)&&!CompanionSafety.CanRun(state,10000,8000),"没有原服近期移动确认仍跑步");
        check(!CompanionSafety.CanRun(state,10000,11000),"未来时间戳被当作近期确认");
        state.User!.HP=65;check(!CompanionSafety.CanRun(state,10000,9500),"低血仍跑步消耗生命");state.User.HP=100;
        state.User.Inventory[0]=new(new ItemInfo{Type=ItemType.Weapon,Weight=11});check(!CompanionSafety.CanRun(state,10000,9500),"超重仍跑步");
        state.User.Inventory[0]=new(new ItemInfo{Type=ItemType.Weapon,Weight=10});check(CompanionSafety.CanRun(state,10000,9500),"恰好满负重被误判为超重");state.User.Inventory[0]=null!;
        state.Me!.Dead=true;check(!CompanionSafety.CanRun(state,10000,9500),"死亡仍跑步");state.Me.Dead=false;
        state.Me.Hidden=true;check(!CompanionSafety.CanRun(state,10000,9500),"隐身时自动跑步破除状态");state.Me.Hidden=false;
        foreach(var poison in new[]{PoisonType.Slow,PoisonType.Paralysis,PoisonType.LRParalysis,PoisonType.Frozen})
        {state.Me.Poison=poison;check(!CompanionSafety.CanRun(state,10000,9500),"异常状态仍跑步："+poison);}state.Me.Poison=PoisonType.None;
        state.User.Equipment[(int)EquipmentSlot.Mount]=new(new ItemInfo{Type=ItemType.Mount});check(!CompanionSafety.CanRun(state,10000,9500),"骑乘三格状态被当两格跑步");state.User.Equipment[(int)EquipmentSlot.Mount]=null!;
        state.UserBuffs[BuffType.SwiftFeet]=new ClientBuff();check(!CompanionSafety.CanRun(state,10000,9500),"疾风步三格状态被当两格跑步");state.UserBuffs.Clear();
        state.StorageActive=true;check(!CompanionSafety.CanRun(state,10000,9500),"仓库交互仍跑步");state.StorageActive=false;
        state.Apply(new S.TradeAccept{Name="队友"});check(!CompanionSafety.CanRun(state,10000,9500),"交易交互仍跑步");
    }

    private static void TravelPolicy(Action<bool,string> check)
    {
        var hunt=new KnownGoal("hunt",7,8,8,"任务目标",QuestId:42,MonsterIds:[101]);
        CompanionKnowledge Knowledge(KnownGoal? goal)=>new("",7,false,false,false,[],[],[],[],Goal:goal);
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(null),99,false),"无目标仍绕开攻击者赶路");
        foreach(var kind in new[]{"service","accept","turn-in"})
            check(CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{Kind=kind}),99,false),"赶路被无关攻击者打断："+kind);
        check(CompanionSafety.TravelBeforeCombat(Knowledge(hunt),99,false),"同图任务被已知无关攻击者抢占");
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(hunt),101,false),"任务所需攻击者被当成无关怪物");
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(hunt),0,false),"未知怪物被直接当成无关攻击者");
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{MonsterIds=null}),99,false),"未知任务来源仍绕开攻击者");
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{MonsterIds=[]}),99,false),"空任务来源仍绕开攻击者");
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{QuestId=0}),99,false),"普通练级被强制当任务赶路");
        check(CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{MapId=9}),101,false),"跨图目标途中仍先清怪");
        check(CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{QuestId=0,Key="fund/7"}),101,false),"原筹资赶路优先被移除");
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{Kind="guild-siege"}),99,false),"攻城被当成普通赶路");
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(hunt),99,true),"低血仍沿任务路线深入而没有保留原撤退");
        check(!CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{Kind="turn-in"}),99,true),"低血交付赶路抢占原撤退");
        check(CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{Kind="service"}),99,true),"低血补给途中原优先撤离被移除");
        check(CompanionSafety.TravelBeforeCombat(Knowledge(hunt with{Key="fund/7"}),99,true),"低血筹资途中原路线优先被移除");
    }
}
