using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CompanionFundingChecks
{
    public static void Run(Action<bool,string> check)
    {
        var world=Envir.Main;
        var oldMaps=world.MapInfoList;var oldLoaded=world.MapList;var oldNpcs=world.NPCs;var oldScripts=world.Scripts;
        var oldMonsters=world.MonsterInfoList;var oldQuests=world.QuestInfoList;var oldObjects=world.Objects;var oldIndex=world.ScriptIndex;var oldCharacters=world.CharacterList;
        try
        {
            var map=new Map(new MapInfo{Index=701}){Width=5,Height=5,Cells=new Cell[5,5],WalkableCells=[]};
            for(var x=0;x<5;x++)for(var y=0;y<5;y++){map.Cells[x,y]=new Cell{Attribute=CellAttribute.Walk};map.WalkableCells.Add(new(x,y));}
            world.MapInfoList=[map.Info];world.MapList=[map];world.NPCs=[];world.Scripts=[];world.Objects=[];
            var task=new MonsterInfo{Index=1,Name="FundingQuest",Experience=10,Stats=new Stats{[Stat.HP]=100,[Stat.MaxDC]=5},AttackSpeed=1000};
            var weak=new MonsterInfo{Index=2,Name="FundingWeak",Experience=0,Stats=new Stats{[Stat.HP]=1},Drops=[new DropInfo{Chance=1,Gold=100}]};
            world.MonsterInfoList=[task,weak];
            map.Info.Respawns=[new RespawnInfo{MonsterIndex=1,Location=new(1,2),Count=1},new RespawnInfo{MonsterIndex=2,Location=new(3,3),Count=1}];
            world.QuestInfoList=[new QuestInfo{Index=1,Name="药费不足的原任务",KillTasks=[new QuestKillTask{Monster=task,Count=1}]}];
            var player=new PlayerObject{Info=new CharacterInfo{Level=10},Account=new AccountInfo{Gold=38},
                Stats=new Stats{[Stat.HP]=100,[Stat.MinDC]=10,[Stat.MaxDC]=10,[Stat.BagWeight]=1000}};
            player.CurrentMap=map;player.CurrentLocation=new(1,1);player.Info.CurrentQuests=[new(1)];
            player.Info.Index=88;player.Info.Player=player;player.Node=new(player);world.CharacterList=[player.Info];
            player.Info.Equipment[0]=new UserItem(new ItemInfo{Type=ItemType.Weapon});
            var npc=new NPCObject(new NPCInfo{Index=1,Name="筹资检查商人",FileName="FundingChecksAbsent",Rate=100,Location=new(0,0)}){CurrentMap=map};
            var script=NPCScript.Get(npc.ScriptID);script.NPCPages=[new(NPCScript.BuySellKey)];
            var red=new UserItem(new ItemInfo{Index=301,Name="FundingRed",Type=ItemType.Potion,Shape=0,Price=40,StackSize=20,
                RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None,Stats=new Stats{[Stat.HP]=30}});
            script.Goods=[red];var profile=new CompanionProfile();
            KnownGoal? Choose(ISet<string>? blocked=null)=>CompanionPlanning.Choose(player,profile,blocked);
            var goal=Choose();
            check(goal?.QuestId==0&&goal.MonsterIds!.SequenceEqual(new[]{2}),"缺红药且金币不足时仍选择高消耗任务，没有低风险筹资");
            check(player.Account.Gold==38&&player.CurrentQuests.Single().KillTaskCount.Single().Count==0,"筹资规划修改了真实资产或任务");
            foreach(var ai in new byte[]{1,2,4,5,7,9,28})
            {
                weak.AI=ai;
                check(MonsterObject.GetMonster(weak) is Server.MirObjects.Monsters.HarvestMonster,"采集AI的原引擎类型已改变，请重新核对金币来源");
                check(Choose()?.MonsterIds?.SequenceEqual(new[]{2})==true,"采集怪已正常死亡落金，但筹资规划仍排除真实来源");
            }
            weak.AI=3;check(Choose()?.MonsterIds?.SequenceEqual(new[]{2})==true,"原Tree正常死亡掉落被误当成不出金币的采集怪");weak.AI=0;
            player.Account.Gold=0;check(Choose()?.MonsterIds?.SequenceEqual(new[]{2})==true,"零金币没有筹资恢复路径");player.Account.Gold=38;
            check(Choose(new HashSet<string>{goal!.Key})?.Kind=="wait","筹资目标暂不可达时回退到高消耗猎杀");
            weak.Drops[0].Chance=0;check(Choose()?.Kind=="wait","零概率金币被当成筹资来源");weak.Drops[0].Chance=1;
            weak.Drops[0].QuestRequired=true;check(Choose()?.Kind=="wait","任务专属掉落被当成普通筹资来源");weak.Drops[0].QuestRequired=false;
            weak.Drops[0].Gold=1;check(Choose()?.Kind=="wait","原随机下界为零的单金币定义被当成可靠来源");weak.Drops[0].Gold=100;
            weak.Drops=[new DropInfo{Chance=1,GroupedDrop=new GroupDropInfo{new DropInfo{Chance=1,Gold=100}}}];
            check(Choose()?.MonsterIds?.SequenceEqual(new[]{2})==true,"筹资遗漏真实分组金币掉落");
            weak.Drops[0].GroupedDrop.First=true;weak.Drops[0].GroupedDrop.Insert(0,new DropInfo{Chance=1,Item=red.Info});
            check(Choose()?.Kind=="wait","首个成功即终止的分组将不可达后置金币误判为来源");
            weak.Drops[0].GroupedDrop.RemoveAt(0);weak.Drops[0].GroupedDrop.First=false;
            weak.Drops[0].GroupedDrop.Add(weak.Drops[0]);weak.Drops[0].GroupedDrop[0].Gold=0;
            check(Choose()?.Kind=="wait","循环分组金币没有有界退出");weak.Drops=[new DropInfo{Chance=1,Gold=100}];
            map.Info.NoDropMonster=true;check(Choose()?.Kind=="wait","禁怪物掉落地图被当成筹资来源");map.Info.NoDropMonster=false;
            map.Info.NoFight=true;check(Choose()?.Kind=="wait","禁战地图仍安排筹资战斗");map.Info.NoFight=false;
            weak.IsBoss=true;check(Choose()?.Kind=="wait","缺药筹资仍选择首领");weak.IsBoss=false;
            weak.Stats[Stat.HP]=200;check(Choose()?.MonsterIds?.SequenceEqual(new[]{2})==true,"预计不掉血的普通小怪因超过8次攻击而无法筹资");
            weak.Stats[Stat.HP]=400;check(Choose()?.Kind=="wait","缺药筹资仍选择过长战斗");weak.Stats[Stat.HP]=1;
            weak.Stats[Stat.MaxDC]=11;check(Choose()?.Kind=="wait","缺药筹资没有限制预计承伤");weak.Stats[Stat.MaxDC]=0;
            var threat=MonsterObject.GetMonster(new MonsterInfo{AI=0,ViewRange=8,Stats=new Stats{[Stat.HP]=1000,[Stat.MaxDC]=100}});
            threat.CurrentMap=map;threat.CurrentLocation=new(3,2);threat.Node=world.Objects.AddLast(threat);
            check(Choose()?.Kind=="wait","只看筹资弱怪，忽略周围真实强怪围攻");
            world.Objects.Remove(threat);check(Choose()?.Key.StartsWith("fund/")==true,"威胁离开后仍永久阻止原安全筹资点");
            foreach(var ai in new byte[]{1,2})
            {
                var passive=MonsterObject.GetMonster(new MonsterInfo{AI=ai,ViewRange=8,Stats=new Stats{[Stat.HP]=10000,[Stat.MaxDC]=1000}});
                var fleeing=typeof(Server.MirObjects.Monsters.Deer).GetField("_runAway",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                fleeing.SetValue(passive,false);
                passive.CurrentMap=map;passive.CurrentLocation=new(3,2);passive.Node=world.Objects.AddLast(passive);
                check(Choose()?.MonsterIds?.SequenceEqual(new[]{2})==true,$"没有攻击本人的被动AI{ai}封死新手筹资区域");
                passive.Target=player;check(Choose()?.Kind=="wait",$"被动AI{ai}已经攻击本人仍忽略危险");
                fleeing.SetValue(passive,true);check(Choose()?.MonsterIds?.SequenceEqual(new[]{2})==true,"逃跑鹿的注视目标被误判为真实攻击目标");
                world.Objects.Remove(passive);
            }
            foreach(var ai in new byte[]{6,57,113})
            {
                var guard=MonsterObject.GetMonster(new MonsterInfo{AI=ai,ViewRange=8,Stats=new Stats{[Stat.HP]=10000,[Stat.MaxDC]=10000}});
                guard.CurrentMap=map;guard.CurrentLocation=new(3,2);guard.Node=world.Objects.AddLast(guard);
                var record=new CompanionRecord{CharacterId=88,Profile=profile with{AcceptGroup=false}};
                var white=CompanionKnowledgeReader.Read(record);
                check(Choose() is {Kind:"hunt",Key:var fundingKey}&&fundingKey.StartsWith("fund/"),$"白名被不会攻击本人的AI{ai}城镇守卫阻断筹资");
                check(white?.Enemies?.All(enemy=>enemy.ObjectId!=guard.ObjectID)==true,$"友好AI{ai}守卫仍被下发为避险敌人");
                player.PKPoints=200;
                check(Choose()?.Kind=="wait",$"红名忽略AI{ai}守卫的真实攻击权限");
                check(CompanionKnowledgeReader.Read(record)?.Enemies?.Any(enemy=>enemy.ObjectId==guard.ObjectID&&!enemy.Suitable)==true,$"红名的AI{ai}守卫威胁未下发");
                player.PKPoints=0;world.Objects.Remove(guard);
            }
            player.Account.Gold=40;check(Choose()?.Key.StartsWith("supply/")==true,"筹资抢占无红药最低一瓶保命采购");
            npc.Info.Rate=200;check(Choose()?.Key.StartsWith("fund/")==true,"筹资忽略当前商人的真实价格倍率");npc.Info.Rate=100;
            player.Account.Gold=1000;check(Choose()?.Key.StartsWith("supply/")==true,"资金足够时仍筹资而不补给");
            player.Account.Gold=38;player.Info.Inventory[0]=red.Clone();player.Info.Inventory[0].Count=10;
            check(Choose()?.QuestId==1,"红药达到储备线后没有恢复原任务");player.Info.Inventory[0]=null;
            var weapon=player.Info.Equipment[0];player.Info.Equipment[0]=null;
            check(Choose()?.Key.StartsWith("fund/")==true,"无武器且买不起药仍回到危险任务，没有按真实徒手战力筹资");player.Info.Equipment[0]=weapon;
            player.CurrentQuests[0].EndDateTime=DateTime.UtcNow;player.CurrentQuests[0].Info.FinishNpcIndex=npc.ObjectID;
            check(Choose()?.Kind=="turn-in","筹资抢占已完成任务的真实交付");
            var remote=new Map(new MapInfo{Index=702});world.MapInfoList.Add(remote.Info);world.MapList.Add(remote);
            map.Info.Movements.Add(new MovementInfo{MapIndex=702});npc.CurrentMap=remote;
            check(Choose()?.Key.StartsWith("fund/")==true,"零红药且买不起药仍为了已完成任务跨图送死");
            var localSpawns=map.Info.Respawns;map.Info.Respawns=[];
            remote.Info.Respawns=[new RespawnInfo{MonsterIndex=2,Location=new(1,1),Count=1}];
            var remoteWeak=MonsterObject.GetMonster(weak);remoteWeak.CurrentMap=remote;remoteWeak.CurrentLocation=new(1,1);world.Objects.AddLast(remoteWeak);
            check(Choose()?.MapId==remote.Info.Index,"安全出口不能正常前往远处低风险筹资点");
            threat.CurrentLocation=new(0,0);threat.Node=world.Objects.AddLast(threat);
            check(Choose()?.Kind=="wait","远处金币源安全，却仍穿过当前被强怪围住的出口");
            world.Objects.Remove(threat);check(Choose()?.MapId==remote.Info.Index,"出口威胁离开后仍永久阻断筹资");
            world.Objects.Remove(remoteWeak);map.Info.Respawns=localSpawns;
            npc.CurrentMap=map;map.Info.Movements.Clear();world.MapInfoList.Remove(remote.Info);world.MapList.Remove(remote);
            player.CurrentQuests[0].EndDateTime=DateTime.MinValue;
            red.Info.RequiredClass=RequiredClass.Wizard;check(Choose()?.QuestId==1,"不可用药品货架触发错误筹资");
        }
        finally
        {
            world.MapInfoList=oldMaps;world.MapList=oldLoaded;world.NPCs=oldNpcs;world.Scripts=oldScripts;world.ScriptIndex=oldIndex;
            world.MonsterInfoList=oldMonsters;world.QuestInfoList=oldQuests;world.Objects=oldObjects;world.CharacterList=oldCharacters;
        }
    }
}
