using Server.ConsoleHost;
using Server.MirDatabase;
using Mir2.Companions;

internal static class CompanionPlanningChecks
{
    public static void Run(Action<bool, string> check)
    {
        var item = new ItemInfo { Index = 42 };
        var drop = new DropInfo { Chance = 1, Item = item };
        check(CompanionPlanning.CanDrop([drop], new HashSet<int> { 42 }), "任务物品没有关联到真实掉落怪物");
        check(!CompanionPlanning.CanDrop([drop], new HashSet<int> { 43 }), "掉落规划混用了不同物品编号");
        var grouped = new DropInfo { Chance = 1, GroupedDrop = new GroupDropInfo { drop } };
        check(CompanionPlanning.CanDrop([grouped], new HashSet<int> { 42 }), "遗漏分组掉落的任务物品");
        drop.Chance = 0;
        check(!CompanionPlanning.CanDrop([grouped], new HashSet<int> { 42 }), "把零概率掉落当成可获取来源");
        grouped.GroupedDrop.Add(grouped);
        check(!CompanionPlanning.CanDrop([grouped], new HashSet<int> { 42 }), "递归掉落没有深度边界");
        var first = new MapInfo { Index = 7 }; var second = new MapInfo { Index = 19 }; var hidden = new MapInfo { Index = 31 };
        first.Movements.Add(new MovementInfo { MapIndex = 19, Source = new(3, 4), ShowOnBigMap = true });
        second.Movements.Add(new MovementInfo { MapIndex = 7, ShowOnBigMap = true });
        first.Movements.Add(new MovementInfo { MapIndex = 31, ShowOnBigMap = false });
        var routes = CompanionPlanning.Routes(7, [first, second, hidden]);
        check(routes[19]?.X == 3 && routes[19]?.Y == 4 && routes.ContainsKey(31), "步行首步或不带大地图图标的房屋出口错误");
        first.Movements[1].NeedMove = true;
        check(!CompanionPlanning.Routes(7,[first,second,hidden]).ContainsKey(31),"NPC条件入口被当成普通步行入口");
        second.RequiredGroup = true; second.RequiredGroupSize = 3;
        check(!CompanionPlanning.Routes(7,[first,second]).ContainsKey(19)&&CompanionPlanning.Routes(7,[first,second],3).ContainsKey(19),"路线未遵守目的地组队人数限制");
        second.RequiredGroup = false;
        first.Movements[0].NeedHole = true;
        check(!CompanionPlanning.Routes(7, [first, second]).ContainsKey(19), "特殊洞口被当成普通步行入口");
        check(!CompanionPlanning.Routes(107, [first, second]).ContainsKey(19), "地图改号后复用了旧路线");
        var player = new Stats { [Stat.HP]=20, [Stat.MinDC]=3, [Stat.MaxDC]=5 };
        var monster = new MonsterInfo { Level=12, Stats=new Stats { [Stat.HP]=25, [Stat.MaxDC]=1 } };
        check(CompanionPlanning.CanHunt(player,1,monster,30),"只按定义等级排除了可击杀的新手怪");
        monster.Stats[Stat.MaxDC]=100;
        check(!CompanionPlanning.CanHunt(player,1,monster,30),"战力评估允许无法承受的伤害");
        monster.Stats[Stat.MaxDC]=0;monster.Stats[Stat.HP]=100000;
        check(!CompanionPlanning.CanHunt(player,1,monster,30),"战力评估没有击杀耗时上限");
        var assassin=Server.Settings.AllowCreateAssassin;var archer=Server.Settings.AllowCreateArcher;
        try
        {
            Server.Settings.AllowCreateAssassin=Server.Settings.AllowCreateArcher=false;
            check(CompanionValidation.AvailableClasses.SequenceEqual(new[]{0,1,2}),"随机职业包含未开放的刺客/弓箭手");
            Server.Settings.AllowCreateArcher=true;
check(!CompanionValidation.CanCreateClass(4)&&!CompanionValidation.CanCreateClass(3),"旧职业开关绕过三职业限制");
        }
        finally{Server.Settings.AllowCreateAssassin=assassin;Server.Settings.AllowCreateArcher=archer;}
        var quest=new QuestInfo{Index=1,RequiredQuest=2,RequiredMaxLevel=100,RequiredClass=RequiredClass.None};
        var prerequisite=new QuestInfo{Index=2,RequiredMaxLevel=100,RequiredClass=RequiredClass.None};
        var quests=new Dictionary<int,QuestInfo>{{1,quest},{2,prerequisite}};
        check(CompanionWorldChecks.PrerequisiteWarnings(quest,quests).Length==0,"正常任务前置被误报");
        prerequisite.RequiredQuest=1;
        check(CompanionWorldChecks.PrerequisiteWarnings(quest,quests).Single().Contains("循环"),"未识别循环任务链");
        prerequisite.RequiredQuest=3;
        check(CompanionWorldChecks.PrerequisiteWarnings(quest,quests).Single().Contains("不存在"),"未识别缺失前置");
        prerequisite.RequiredQuest=0;prerequisite.RequiredMinLevel=101;
        check(CompanionWorldChecks.PrerequisiteWarnings(quest,quests).Single().Contains("等级"),"未识别无法满足的等级前置");
        var meat=new ItemInfo{Index=99,Type=ItemType.Meat,Price=100};var meatItem=new UserItem(meat);
        check(CompanionInventoryRules.PlainMeat(meatItem,new HashSet<int>(),1),"普通肉类无法出售筹资");
        check(!CompanionInventoryRules.PlainMeat(meatItem,new HashSet<int>{99},1),"任务肉类被出售");
        meat.Bind=BindMode.DontSell;
        check(!CompanionInventoryRules.PlainMeat(meatItem,new HashSet<int>(),1),"禁售肉类被出售");
    }
}
