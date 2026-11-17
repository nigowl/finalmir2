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
        var oldMonsters=world.MonsterInfoList;var oldObjects=world.Objects;var oldQuests=world.QuestInfoList;
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
            var goal=Develop();check(goal?.Key==$"develop/{npc.ObjectID}","药品充足时没有主动去真实书店");
            check(Develop(new HashSet<string>{goal!.Key})==null,"主动发展绕过不可达排除");
            player.Account.Gold=1;check(Develop()==null,"主动购书透支金币");player.Account.Gold=10000;
            player.Info.Magics=[new UserMagic(Spell.SoulFireBall)];check(Develop()==null,"已学法术仍反复回书店");player.Info.Magics=[];
            script.Goods=[ring];check(Develop()!=null,"主动发展没有填补可穿装备的空槽");
            player.Stats[Stat.WearWeight]=0;ring.Info.Weight=1;check(Develop()==null,"规划购装忽略原穿戴负重");ring.Info.Weight=0;
            script.Goods=[];world.NPCs=[];
            var chicken=new MonsterInfo{Index=1,Name="低收益近怪",Level=2,Experience=9,Stats=new Stats{[Stat.HP]=5}};
            var better=new MonsterInfo{Index=2,Name="安全练级目标",Level=20,Experience=50,Stats=new Stats{[Stat.HP]=20}};
            world.MonsterInfoList=[chicken,better];map.Info.Respawns=[new RespawnInfo{MonsterIndex=1,Count=1,Location=new(1,2)},new RespawnInfo{MonsterIndex=2,Count=1,Location=new(5,5)}];
            Server.Settings.ExpMobLevelDifference=true;
            check(player.ReduceExp(chicken.Experience,chicken.Level)==1,"练级检查没有复现原等级差经验衰减");
            check(CompanionPlanning.Choose(player,profile)?.MonsterIds?.SequenceEqual(new[]{2})==true,"普通练级只看最近，继续围着1经验怪转");
            map.Info.NoExperience=true;check(CompanionPlanning.Choose(player,profile)==null,"普通练级选择禁止经验地图");map.Info.NoExperience=false;
            world.QuestInfoList=[new QuestInfo{Index=1,KillTasks=[new QuestKillTask{Monster=chicken,Count=1}]}];player.Info.CurrentQuests=[new(1)];
            check(CompanionPlanning.Choose(player,profile)?.QuestId==1,"经验估值抢占真实任务");
            check(player.Info.CurrentQuests[0].KillTaskCount[0].Count==0&&player.CompletedQuests.Count==0&&player.Account.Gold==10000,"发展预检写入真实资产或任务");
        }
        finally
        {
            world.MapInfoList=oldMaps;world.MapList=oldLoaded;world.NPCs=oldNpcs;world.Scripts=oldScripts;world.ScriptIndex=oldIndex;
            world.MagicInfoList=oldMagic;world.MonsterInfoList=oldMonsters;world.Objects=oldObjects;world.QuestInfoList=oldQuests;Server.Settings.ExpMobLevelDifference=exp;
        }
    }
}
