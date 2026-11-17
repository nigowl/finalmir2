using Server.MirDatabase;
using Server.MirEnvir;

internal static class QuestParsingChecks
{
    public static void Run(Action<bool,string> check)
    {
        var world=Envir.Main;var items=world.ItemInfoList;var monsters=world.MonsterInfoList;
        try
        {
            var item=new ItemInfo{Index=901,Name="ParsingItem"};
            var male=new ItemInfo{Index=902,Name="ParsingDress(M)"};
            var female=new ItemInfo{Index=903,Name="ParsingDress(F)"};
            var monster=new MonsterInfo{Index=901,Name="ParsingMonster"};
            world.ItemInfoList=[item,male,female];world.MonsterInfoList=[monster];
            var quest=new QuestInfo();
            foreach(var (suffix,count) in new[]{("",1),(" ",1),("   ",1),("\t",1),("  3  ",3),("\t3\t",3)})
            {
                var rewards=new List<QuestItemReward>();quest.ParseReward(rewards,item.Name+suffix);
                check(rewards.Count==1&&rewards[0].Item==item&&rewards[0].Count==count,"奖励空白分隔误改数量："+suffix.Replace("\t","TAB"));
                var carry=quest.ParseItem(item.Name+suffix);var kill=quest.ParseKill(monster.Name+suffix);
                check(carry?.Item==item&&carry.Count==count,"携带/收集物空白分隔误改数量");
                check(kill?.Monster==monster&&kill.Count==count,"击杀任务空白分隔误改数量");
            }
            var genders=new List<QuestItemReward>();quest.ParseReward(genders," \tParsingDress  2 \t");
            check(genders.Count==2&&genders[0].Item==male&&genders[1].Item==female&&genders.All(r=>r.Count==2),"空白修复破坏原男女双奖励");
            foreach(var blank in new[]{""," ","\t","  \t "})
            {
                var rewards=new List<QuestItemReward>();quest.ParseReward(rewards,blank);
                check(rewards.Count==0&&quest.ParseItem(blank)==null&&quest.ParseKill(blank)==null,"空白任务行未跳过");
            }
            foreach(var count in new[]{"0","bad","65536"})
            {
                var rewards=new List<QuestItemReward>();quest.ParseReward(rewards,"ParsingItem "+count);
                check(rewards.Count==1&&rewards[0].Count==0,"空白修复擅自将显式零或畸形奖励变成1份");
            }
            var parsed=new QuestInfo();parsed.ParseFile(["[@FIXEDREWARDS]","ParsingItem \t","[@ITEMTASKS]","ParsingItem\t3\t","[@KILLTASKS]","ParsingMonster  2  "]);
            check(parsed.FixedRewards.Single().Count==1&&parsed.ItemTasks.Single().Count==3&&parsed.KillTasks.Single().Count==2,"实际脚本段未使用修正后的数量解析");
            KillSummaryChecks(check,monster);
        }
        finally{world.ItemInfoList=items;world.MonsterInfoList=monsters;}
    }

    private static void KillSummaryChecks(Action<bool,string> check,MonsterInfo monster)
    {
        var world=Envir.Main;var original=world.QuestInfoList;
        try
        {
            var other=new MonsterInfo{Index=902,Name="OtherMonster"};
            var quest=new QuestInfo{Index=90001,KillMessage="清理矿洞中的怪物",KillTasks=[new(){Monster=monster,Count=5},new(){Monster=other,Count=5}]};
            world.QuestInfoList=[quest];var progress=new QuestProgressInfo(quest.Index);
            foreach(var count in new ushort[]{0,1,4,5})
            {
                progress.KillTaskCount[0].Count=count;progress.KillTaskCount[1].Count=5;progress.UpdateTasks();
                check(progress.TaskList.Count==3&&progress.TaskList[0].Contains(quest.KillMessage)&&
                    progress.TaskList[1].Contains(count+"/5")&&progress.TaskList[2].Contains("5/5"),"汇总说明遮住了真实分项击杀计数");
                check(progress.KillTaskCount[0].Count==count&&progress.KillTaskCount[1].Count==5&&!progress.Completed,"显示进度改变了计数或伪造已交付任务");
            }
        }
        finally{world.QuestInfoList=original;}
    }
}
