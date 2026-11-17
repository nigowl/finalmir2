using System.Text.Json;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirObjects;

internal static class CompanionCombatReaderChecks
{
    public static void Run(Action<bool,string> check)
    {
        var info=new MagicInfo{Name="Healing",Spell=Spell.Healing,Need1=50,Need2=100,Need3=200};
        var magic=new UserMagic(Spell.Healing){Info=info,Level=1,Experience=67,IsTempSpell=true};
        var player=new PlayerObject{Info=new CharacterInfo{Magics=[magic]},Stats=new Stats{[Stat.MinDC]=7,[Stat.MaxDC]=19,[Stat.Accuracy]=12,[Stat.AttackSpeed]=-2}};
        var snapshot=CompanionCombatReader.Read(player);
        check(snapshot.Stats[Stat.MinDC]==7&&snapshot.Stats[Stat.MaxDC]==19&&snapshot.Stats[Stat.Accuracy]==12&&snapshot.Stats[Stat.AttackSpeed]==-2,
            "陪玩战斗查看没有沿用原引擎当前属性或丢失负数");
        check(snapshot.Stats[Stat.Agility]==0,"未设置的原属性没有保持0");
        var skill=snapshot.Skills.Single();
        check(skill.Spell==Spell.Healing&&skill.Name==ChineseText.Name("Healing")&&skill.Level==1&&skill.Experience==67&&skill.RequiredExperience==100&&skill.Temporary,
            "陪玩技能查看丢失原技能编号、名称、等级、熟练度或临时标志");
        player.Stats[Stat.Accuracy]=99;magic.Level=2;magic.Experience=88;
        check(snapshot.Stats[Stat.Accuracy]==12&&snapshot.Skills[0].Level==1&&snapshot.Skills[0].Experience==67,"管理响应仍引用可变引擎集合");
        check(CompanionCombatReader.Read(player).Skills[0].RequiredExperience==200,"二级技能没有读取原Need3");
        magic.Level=0;check(CompanionCombatReader.Read(player).Skills[0].RequiredExperience==50,"零级技能没有读取原Need1");
        magic.Level=3;check(CompanionCombatReader.Read(player).Skills[0].RequiredExperience==0,"三级技能仍显示下一等级所需熟练度");
        magic.Info=null;
        check(CompanionCombatReader.Read(player).Skills[0].Name==ChineseText.Name("Healing"),"缺失技能定义时管理查看崩溃或丢失原技能标识");
        var restored=JsonSerializer.Deserialize<Mir2.Companions.CompanionCombatState>(JsonSerializer.Serialize(snapshot))!;
        check(restored.Stats.Equals(snapshot.Stats)&&restored.Skills.SequenceEqual(snapshot.Skills),"管理战斗快照JSON往返改变原属性或技能数据");
        player.Info.Magics.Clear();
        check(CompanionCombatReader.Read(player).Skills.Length==0,"无技能角色继承了旧角色技能");
        check(player.Stats[Stat.Accuracy]==99&&magic.Level==3&&magic.Experience==88,"只读查看修改了引擎属性或技能");
    }
}
