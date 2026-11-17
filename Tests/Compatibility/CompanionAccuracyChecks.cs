using Server.ConsoleHost;
using Server.MirDatabase;

internal static class CompanionAccuracyChecks
{
    public static void Run(Action<bool,string> check)
    {
        var player=new Stats{[Stat.HP]=100,[Stat.MinDC]=10,[Stat.MaxDC]=10};
        var monster=new MonsterInfo{AI=0,AttackSpeed=1000,Stats=new Stats{[Stat.HP]=100,[Stat.Agility]=19}};
        check(!CompanionPlanning.CanHunt(player,24,monster,30),"零准确对高敏捷目标仍按次次命中规划");
        player[Stat.Accuracy]=19;
        check(CompanionPlanning.CanHunt(player,24,monster,30),"准确达到敏捷后仍排除可猎杀目标");
        player[Stat.Accuracy]=0;monster.Stats[Stat.Agility]=0;
        check(CompanionPlanning.CanHunt(player,24,monster,30),"零准确对零敏捷不应被误判为零命中");
        monster.Stats[Stat.HP]=20;monster.Stats[Stat.Agility]=15;
        check(!CompanionPlanning.CanHunt(player,24,monster,0),"没有计入原命中窗口包含0，预期32次普攻仍越过30次限制");
        player[Stat.Accuracy]=1;
        check(CompanionPlanning.CanHunt(player,24,monster,0),"原命中窗口1/16到2/16的边界错误");
        player[Stat.Accuracy]=-1;
        check(!CompanionPlanning.CanHunt(player,24,monster,100),"负准确在原引擎必定未命中却仍选为猎杀目标");
        player[Stat.Accuracy]=int.MaxValue;
        check(CompanionPlanning.CanHunt(player,24,monster,0),"准确加1溢出或大于敏捷时产生超过100%的命中率");
        player[Stat.Accuracy]=0;monster.Stats[Stat.HP]=50;monster.Stats[Stat.Agility]=4;monster.Stats[Stat.MaxDC]=3;
        check(!CompanionPlanning.CanHunt(player,24,monster,30),"只有20%命中率却没有提高战斗时长及预计承伤");
        player[Stat.Accuracy]=4;
        check(CompanionPlanning.CanHunt(player,24,monster,30),"充分准确下的原承伤判断被破坏");
        player[Stat.Accuracy]=0;monster.Stats[Stat.HP]=1;monster.Stats[Stat.Agility]=99;monster.Stats[Stat.MaxDC]=0;
        check(!CompanionPlanning.CanHunt(player,24,monster,100),"一击伤害超过目标生命后摊薄了100次预期攻击的未命中成本");
    }
}
