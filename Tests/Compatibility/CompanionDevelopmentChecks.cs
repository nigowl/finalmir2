using Mir2.Companions;
using Mir2.Companion;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CompanionDevelopmentChecks
{
    public static void Run(Action<bool,string> check)
    {
        var ring = new UserItem(new ItemInfo{Index=301,Type=ItemType.Ring,Price=100,Durability=1000,Stats=new Stats{[Stat.MaxAC]=3}}){CurrentDura=1000};
        var equipment = new UserItem?[14];
        equipment[7]=new(new ItemInfo{Type=ItemType.Ring,Stats=new Stats{[Stat.MaxAC]=5}});
        equipment[8]=new(new ItemInfo{Type=ItemType.Ring,Stats=new Stats{[Stat.MaxAC]=1}});
        check(CompanionInventoryRules.UpgradeSlot(ring,equipment,MirClass.Warrior,false)==8,"右槽较差却只比较左戒指");
        equipment[8]=null;check(CompanionInventoryRules.UpgradeSlot(ring,equipment,MirClass.Warrior,false)==8,"没有先补空戒指槽");
        equipment[8]=equipment[7];check(CompanionInventoryRules.UpgradeSlot(ring,equipment,MirClass.Warrior,false)==-1,"同分或更差装备反复替换");
        var book=new UserItem(new ItemInfo{Index=302,Type=ItemType.Book,Shape=(short)Spell.SoulFireBall,RequiredAmount=18});
        var learned=new HashSet<Spell>();
        int Need(UserItem item,IEnumerable<UserItem?>? bag=null)=>CompanionInventoryRules.DevelopmentCount(item,bag??[],equipment,MirClass.Taoist,MirGender.Male,23,learned);
        check(Need(book)==1,"有等级条件的未学技能书未被纳入主动采购");
        check(Need(book,[book])==0,"已在背包的书被重复采购");
        learned.Add(Spell.SoulFireBall);check(Need(book)==0,"已学技能书仍采购");
        var amulet=new UserItem(new ItemInfo{Index=303,Type=ItemType.Amulet,Shape=0,StackSize=100});
        check(Need(amulet)==50,"学会火符后没有安排护符储备");amulet.Count=20;
        check(Need(amulet,[amulet])==30,"护符储备没有累计包内数量");equipment[9]=amulet;
        check(Need(amulet,[amulet])==10,"护符储备没有累计装备数量");equipment[9]=null;
        amulet.Info.Shape=1;check(Need(amulet)==0,"未支持的毒粉采购抢占火符补给");amulet.Info.Shape=0;
        book.Info.RequiredAmount=24;learned.Clear();check(Need(book)==0,"主动发展绕过技能书等级");book.Info.RequiredAmount=18;
        book.Info.RequiredClass=RequiredClass.Wizard;check(Need(book)==0,"主动发展绕过技能书职业");book.Info.RequiredClass=RequiredClass.None;
        check(CompanionSafety.LowHealth(50,100,15)&&!CompanionSafety.LowHealth(55,100,15),"低风险角色没有提前撤退");
        check(CompanionSafety.LowHealth(45,100,100)&&!CompanionSafety.LowHealth(46,100,100),"高风险角色低血阈值失去下界");
        check(CompanionSafety.LowHealth(50000000,100000000,15),"大生命值撤退计算溢出");
        var bow=new UserItem(new ItemInfo{Index=351,Type=ItemType.Weapon,Price=100,Durability=1000,Stats=new Stats{[Stat.MaxMC]=10}}){UniqueID=351,CurrentDura=1000};
        var backup=new UserItem(new ItemInfo{Index=352,Type=ItemType.Weapon,Price=100,Durability=1000,Stats=new Stats{[Stat.MaxMC]=9}}){UniqueID=352,CurrentDura=1000};
        var sword=new UserItem(new ItemInfo{Index=353,Type=ItemType.Weapon,Price=100,Durability=1000,Stats=new Stats{[Stat.MaxDC]=20}}){UniqueID=353,CurrentDura=1000};
        check(CompanionInventoryRules.SurplusWeapons([backup,sword],bow,MirClass.Archer,MirGender.Male,18,true,new HashSet<int>()).Select(i=>i.UniqueID).SequenceEqual(new[]{353UL}),
            "弓箭手原射击使用MC，减负却保留DC武器并出售正确备弓");
        Planner(check,book,ring);
    }

    private static void Planner(Action<bool,string> check,UserItem book,UserItem ring)
    {
        var world=Envir.Main;var oldMaps=world.MapInfoList;var oldLoaded=world.MapList;var oldNpcs=world.NPCs;
        var oldScripts=world.Scripts;var oldIndex=world.ScriptIndex;var oldMagic=world.MagicInfoList;
        var oldMonsters=world.MonsterInfoList;var oldObjects=world.Objects;var oldQuests=world.QuestInfoList;var oldCharacters=world.CharacterList;
        var exp=Server.Settings.ExpMobLevelDifference;
        try
        {
            var map=new Map(new MapInfo{Index=701}){Width=8,Height=8,Cells=new Cell[8,8],WalkableCells=[]};
            for(var x=0;x<8;x++)for(var y=0;y<8;y++){map.Cells[x,y]=new Cell{Attribute=CellAttribute.Walk};map.WalkableCells.Add(new(x,y));}
            world.MapInfoList=[map.Info];world.MapList=[map];world.Scripts=[];world.NPCs=[];world.Objects=[];world.QuestInfoList=[];
            var player=new PlayerObject{Info=new CharacterInfo{Level=23,Class=MirClass.Taoist},Account=new AccountInfo{Gold=10000},
                CurrentMap=map,CurrentLocation=new(1,1),Stats=new Stats{[Stat.HP]=200,[Stat.MinDC]=20,[Stat.MaxDC]=20,[Stat.BagWeight]=1000,[Stat.WearWeight]=100}};
            var npc=new NPCObject(new NPCInfo{Index=1,Name="发展商人",FileName="DevelopmentChecksAbsent",Location=new(3,3)}){CurrentMap=map};
            var script=NPCScript.Get(npc.ScriptID);script.NPCPages=[new(NPCScript.BuySellKey)];book.Info.Price=100;script.Goods=[book];world.NPCs=[npc];
            world.MagicInfoList=[new MagicInfo{Spell=Spell.SoulFireBall,Level1=18}];
            var profile=new CompanionProfile();var routes=CompanionPlanning.Routes(701,world.MapInfoList);
            KnownGoal? Develop(ISet<string>? blocked=null)=>CompanionDevelopment.Choose(player,profile,world.NPCs.ToArray(),routes,blocked);
            var red=new UserItem(new ItemInfo{Index=304,Type=ItemType.Potion,Shape=0,Price=40,StackSize=100,Stats=new Stats{[Stat.HP]=30}}){Count=20};
            player.Info.Inventory[0]=red;
            var goal=Develop();check(goal?.Key==$"develop/{npc.ObjectID}","药品充足时没有主动去真实书店");
            check(Develop(new HashSet<string>{goal!.Key})==null,"主动发展绕过不可达排除");
            var fallback=new NPCObject(new NPCInfo{Index=2,Name="替代商店",FileName="DevelopmentFallbackAbsent",Location=new(7,7)}){CurrentMap=map};
            var fallbackScript=NPCScript.Get(fallback.ScriptID);fallbackScript.NPCPages=[new(NPCScript.BuySellKey)];fallbackScript.Goods=[book];world.NPCs=[npc,fallback];
            var alternate=CompanionDevelopment.Choose(player,profile,world.NPCs.ToArray(),routes,new HashSet<string>{goal!.Key});
            check(alternate?.NpcId==fallback.ObjectID,"原店不可达时未选择合法替代商店");
            check(CompanionPlanning.Choose(player,profile,previousGoal:alternate)?.NpcId==fallback.ObjectID,"旧店排除到期抢占尚未完成的替代商店路线");
            check(CompanionPlanning.Choose(player,profile,new HashSet<string>{alternate!.Key},previousGoal:alternate)?.NpcId==npc.ObjectID,"保持采购目标绕过新的不可达排除");
            fallbackScript.Goods=[];check(CompanionPlanning.Choose(player,profile,previousGoal:alternate)?.NpcId==npc.ObjectID,"保持目标后仍去已经没有所需商品的商店");
            fallbackScript.Goods=[ring];check(CompanionPlanning.Choose(player,profile,previousGoal:alternate)?.NpcId==npc.ObjectID,"购装路线抢占未学职业技能书");
            player.Info.Index=88;player.Info.Player=player;world.CharacterList=[player.Info];fallbackScript.Goods=[book];
            check(CompanionKnowledgeReader.Read(new CompanionRecord{CharacterId=88,Profile=profile},previousGoal:alternate)?.Goal?.NpcId==fallback.ObjectID,"目标快照没有传递原采购行程");
            world.NPCs=[npc];
            player.Account.Gold=1;check(Develop()==null,"主动购书透支金币");player.Account.Gold=10000;
            player.Info.Magics=[new UserMagic(Spell.SoulFireBall)];check(Develop()==null,"已学法术仍反复回书店");
            var charmInfo=new ItemInfo{Index=305,Type=ItemType.Amulet,Price=1,StackSize=100};
            script.Goods=[new UserItem(charmInfo){Count=100}];
            player.Info.Equipment[9]=new(charmInfo){Count=24};player.Info.Inventory[1]=new(charmInfo){Count=25};
            check(Develop()==null,"剩49张护符仍专程返店");
            player.Info.Inventory[1]!.Count=1;check(Develop()==null,"还剩25张护符就专程返店");
            player.Info.Inventory[1]=null;check(Develop()?.Key==$"develop/{npc.ObjectID}","低于25张护符没有主动补给");
            player.Info.Equipment[9]=null;player.Info.Magics=[];
            script.Goods=[ring];check(Develop()!=null,"主动发展没有填补可穿装备的空槽");
            player.Stats[Stat.WearWeight]=0;ring.Info.Weight=1;check(Develop()==null,"规划购装忽略原穿戴负重");ring.Info.Weight=0;
            player.Stats[Stat.WearWeight]=100;script.Goods=[book,red];player.Info.Inventory[0]=null;
            var blocked=new HashSet<string>{$"supply/{npc.ObjectID}"};
            check(CompanionPlanning.Choose(player,profile,blocked)?.Kind=="wait","药店暂不可达时缺红药仍去购书或装备");
            check(CompanionPlanning.Choose(player,profile)?.Key==$"supply/{npc.ObjectID}","红药短缺时没有保留正常补给");
            player.Info.Index=88;player.Info.Player=player;player.AllowGroup=true;player.GroupMembers=[new PlayerObject{Info=new CharacterInfo{Name="原队长"},CurrentMap=map},player];world.CharacterList=[player.Info];
            check(CompanionKnowledgeReader.Read(new CompanionRecord{CharacterId=88,Profile=profile},blocked)?.Goal?.Key=="supply/wait","跟队覆盖了缺红药补给等待");
            player.Info.Inventory[0]=red;script.Goods=[];
            check(CompanionKnowledgeReader.Read(new CompanionRecord{CharacterId=88,Profile=profile},blocked)?.Goal?.Kind=="party","红药恢复后没有恢复原跟队");
            player.GroupMembers=null;player.Info.Inventory[0]=null;script.Goods=[book,red];
            var completed=new QuestInfo{Index=88,Name="已齐交付",FinishNpcIndex=npc.ObjectID,GoldReward=17};world.QuestInfoList=[completed];
            player.Info.CurrentQuests=[new(88){EndDateTime=DateTime.UtcNow}];
            check(CompanionPlanning.Choose(player,profile,blocked)?.Kind=="turn-in","药店暂不可达时阻止同图已完成交付");
            var remote=new Map(new MapInfo{Index=702});world.MapInfoList.Add(remote.Info);world.MapList.Add(remote);map.Info.Movements.Add(new(){MapIndex=702});npc.CurrentMap=remote;
            check(CompanionPlanning.Choose(player,profile,blocked)?.Kind=="wait","药店暂不可达时缺红药仍跨图交付");
            npc.CurrentMap=map;map.Info.Movements.Clear();world.MapInfoList.Remove(remote.Info);world.MapList.Remove(remote);world.QuestInfoList=[];player.Info.CurrentQuests=[];
            player.Info.Inventory[0]=red;
            check(CompanionPlanning.Choose(player,profile,blocked)?.Key==$"develop/{npc.ObjectID}","红药足够后发展仍被阻止");
            script.Goods=[];world.NPCs=[];
            var chicken=new MonsterInfo{Index=1,Name="低收益近怪",Level=2,Experience=9,Stats=new Stats{[Stat.HP]=5}};
            var better=new MonsterInfo{Index=2,Name="安全练级目标",Level=20,Experience=50,Stats=new Stats{[Stat.HP]=20}};
            world.MonsterInfoList=[chicken,better];map.Info.Respawns=[new RespawnInfo{MonsterIndex=1,Count=1,Location=new(1,2)},new RespawnInfo{MonsterIndex=2,Count=1,Location=new(5,5)}];
            Server.Settings.ExpMobLevelDifference=true;
            check(player.ReduceExp(chicken.Experience,chicken.Level)==1,"练级检查没有复现原等级差经验衰减");
            check(CompanionPlanning.Choose(player,profile)?.MonsterIds?.SequenceEqual(new[]{2})==true,"普通练级只看最近，继续围着1经验怪转");
            check(CompanionPlanning.Choose(player,profile,new HashSet<string>{"combat/2"})?.MonsterIds?.SequenceEqual(new[]{1})==true,
                "实战不适合的目标没有跨刷新点排除并退回低级怪");
            check(CompanionPlanning.Choose(player,profile)?.MonsterIds?.SequenceEqual(new[]{2})==true,"排除到期后没有恢复高收益练级目标");
            var normalSpawns=map.Info.Respawns;
            map.Info.Respawns=Enumerable.Repeat(new RespawnInfo{MonsterIndex=2,Count=1,Location=new(1,2)},32)
                .Append(new RespawnInfo{MonsterIndex=2,Count=1,Location=new(5,5)}).ToList();
            check(CompanionPlanning.Choose(player,profile,new HashSet<string>{"hunt/701/1/2/2/0"}) is {Key:"hunt/701/5/5/2/0"},
                "前32个已排除刷新点仍占满候选预算，遗漏后续可行目标");
            map.Info.Respawns=normalSpawns;
            map.Info.NoExperience=true;check(CompanionPlanning.Choose(player,profile)==null,"普通练级选择禁止经验地图");map.Info.NoExperience=false;
            world.MapInfoList.Add(remote.Info);world.MapList.Add(remote);
            map.Info.Movements.Add(new(){MapIndex=remote.Info.Index,Source=new(6,6)});
            remote.Width=map.Width;remote.Height=map.Height;remote.Cells=map.Cells;remote.WalkableCells=map.WalkableCells;
            remote.Info.Respawns=[map.Info.Respawns[1]];
            map.Info.Respawns.RemoveAt(1);
            player.HP=player.Stats[Stat.HP];
            check(CompanionPlanning.Choose(player,profile) is { MapId:702, NextExit.DestinationMapId:702 },"同图弱怪抢占更高收益的可达练级地图");
            player.HP=1;
            check(CompanionPlanning.Choose(player,profile)?.MapId==701,"低血角色仍跨图寻找普通练级目标");
            player.HP=player.Stats[Stat.HP];
            remote.Info.NoExperience=true;
            check(CompanionPlanning.Choose(player,profile)?.MapId==701,"跨图练级跳过禁经验条件");
            remote.Info.NoExperience=false;
            world.QuestInfoList=[new QuestInfo{Index=1,KillTasks=[new QuestKillTask{Monster=chicken,Count=1}]}];player.Info.CurrentQuests=[new(1)];
            check(CompanionPlanning.Choose(player,profile)?.QuestId==1,"经验估值抢占真实任务");
            var leader=new PlayerObject{Info=new CharacterInfo{Index=89,Name="不同地图的队长"},CurrentMap=remote};
            player.GroupMembers=[leader,player];
            var knowledge=CompanionKnowledgeReader.Read(new CompanionRecord{CharacterId=88,Profile=profile});
            check(knowledge?.Party?.Goal?.MapId==remote.Info.Index,"公开入口的组队候选丢失");
            check(knowledge?.Goal is {Kind:"hunt",QuestId:1,MapId:701},"跟随队长覆盖了还能完成的个人任务");
            check(player.Info.CurrentQuests[0].KillTaskCount[0].Count==0&&player.CompletedQuests.Count==0&&player.Account.Gold==10000,"发展预检写入真实资产或任务");
        }
        finally
        {
            world.MapInfoList=oldMaps;world.MapList=oldLoaded;world.NPCs=oldNpcs;world.Scripts=oldScripts;world.ScriptIndex=oldIndex;
            world.MagicInfoList=oldMagic;world.MonsterInfoList=oldMonsters;world.Objects=oldObjects;world.QuestInfoList=oldQuests;world.CharacterList=oldCharacters;Server.Settings.ExpMobLevelDifference=exp;
        }
    }
}
