using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionFundingScenario
{
    public static void Prepare(ProjectPaths paths,int characterId,bool noSource,bool harvestSource=false,bool evasiveSource=false,bool harvestGround=false,bool remoteDelivery=false,bool unarmed=false,bool crowdedSource=false)
    {
        if(!paths.Data.Contains("mir2-companions-funding-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本筹资测试的隔离版本。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();world.LoadGuilds();
        var character=world.CharacterList.Single(role=>role.Index==characterId);var start=character.BindLocation;
        character.CurrentMapIndex=character.BindMapIndex;character.CurrentLocation=start;character.Level=10;character.HP=character.MP=1000;
        character.AccountInfo.Gold=38;character.Experience=0;character.CompletedQuests.Clear();character.Magics.Clear();
        character.Inventory=new UserItem[46];character.QuestInventory=new UserItem[40];character.Equipment=new UserItem[14];
        foreach(var map in world.MapInfoList){map.Respawns.Clear();map.NoFight=false;}
        var local=world.MapInfoList.Single(map=>map.Index==character.BindMapIndex);
        var remote=world.MapInfoList.Single(map=>map.Index!=local.Index);
        var red=new ItemInfo{Index=++world.ItemIndex,Name="NativeFundingRed",Type=ItemType.Potion,Shape=0,Price=40,StackSize=20,
            RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None,Stats=new Stats{[Stat.HP]=30}};
        var blade=new ItemInfo{Index=++world.ItemIndex,Name="NativeFundingBlade",Type=ItemType.Weapon,Durability=10000,
            Stats=new Stats{[Stat.MinDC]=100,[Stat.MaxDC]=100,[Stat.Accuracy]=100}};
        var token=new ItemInfo{Index=++world.ItemIndex,Name="NativeFundingProtectedOre",Type=ItemType.Ore,StackSize=1};
        world.ItemInfoList.AddRange([red,blade,token]);character.Equipment[0]=world.CreateFreshItem(blade);character.Inventory[0]=world.CreateFreshItem(token);
        if(unarmed)character.Equipment[0]=null;
        world.MonsterInfoList=[new MonsterInfo{Index=1,Name="NativeFundingWeak",Image=Monster.Hen,Level=1,AI=(byte)(harvestSource||harvestGround?9:0),ViewRange=0,
            MoveSpeed=ushort.MaxValue,AttackSpeed=2500,Experience=0,Stats=new Stats{[Stat.HP]=1},DropPath="NativeFundingWeak"},
            new MonsterInfo{Index=2,Name="NativeFundingQuest",Image=Monster.Hen,Level=1,AI=0,ViewRange=0,
                MoveSpeed=ushort.MaxValue,AttackSpeed=2500,Experience=10,Stats=new Stats{[Stat.HP]=400,[Stat.MinDC]=5,[Stat.MaxDC]=5},DropPath="NativeFundingQuest"}];
        world.MonsterIndex=2;
        if(crowdedSource)
        {
            world.MonsterInfoList.Add(new MonsterInfo{Index=++world.MonsterIndex,Name="隔离筹资围攻威胁",Image=Monster.ClZombie,AI=0,Level=30,ViewRange=0,
                MoveSpeed=ushort.MaxValue,AttackSpeed=1000,Stats=new Stats{[Stat.HP]=10000,[Stat.MinDC]=1000,[Stat.MaxDC]=1000}});
            local.Respawns.Add(new RespawnInfo{MonsterIndex=3,Location=Functions.PointMove(start,MirDirection.DownLeft,3),Count=1,Delay=600});
            world.MonsterInfoList.Add(new MonsterInfo{Index=++world.MonsterIndex,Name="隔离安全筹资目标",Image=Monster.Hen,AI=0,Level=1,ViewRange=0,
                MoveSpeed=ushort.MaxValue,AttackSpeed=2500,Stats=new Stats{[Stat.HP]=1},DropPath="NativeFundingWeak"});
            local.Respawns.Add(new RespawnInfo{MonsterIndex=4,Location=Functions.PointMove(start,MirDirection.DownRight,6),Count=1,Delay=600});
        }
        if(evasiveSource)
        {
            world.MonsterInfoList[0].Stats[Stat.Agility]=10000;
            world.MonsterInfoList.Add(new MonsterInfo{Index=++world.MonsterIndex,Name="NativeFundingReliable",Image=Monster.Hen,
                Level=1,AI=0,ViewRange=0,MoveSpeed=ushort.MaxValue,AttackSpeed=2500,Experience=0,
                Stats=new Stats{[Stat.HP]=1},DropPath="NativeFundingWeak"});
            local.Respawns.Add(new RespawnInfo{RespawnIndex=3,MonsterIndex=3,Location=Functions.PointMove(start,MirDirection.DownLeft,4),Count=1,Delay=600});
        }
        local.Respawns.Add(new RespawnInfo{RespawnIndex=1,MonsterIndex=1,Location=Functions.PointMove(start,MirDirection.DownLeft,2),Count=1,Delay=600});
        remote.Respawns.Add(new RespawnInfo{RespawnIndex=2,MonsterIndex=2,Location=Functions.PointMove(start,MirDirection.DownRight,2),Count=1,Delay=600});
        File.WriteAllText(Path.Combine(Settings.DropPath,"NativeFundingWeak.txt"),noSource?"":harvestSource?"1/1 GROUP\n{\n1/1 Gold 2000\n}\n":"1/1 Gold 2000\n");
        File.WriteAllText(Path.Combine(Settings.DropPath,"NativeFundingQuest.txt"),"");
        var quest=new QuestInfo{Index=1,Name="先筹药钱再完成的原任务",FileName="NativeFundingQuest",RequiredMinLevel=1,RequiredMaxLevel=255,RequiredClass=RequiredClass.None};
        world.QuestInfoList=[quest];world.QuestIndex=1;
        File.WriteAllText(Path.Combine(Settings.QuestPath,quest.FileName+".txt"),"[@DESCRIPTION]\n筹资恢复任务检查。\n[@KILLTASKS]\nNativeFundingQuest 1\n[@GOLDREWARD]\n333\n");
        quest.LoadInfo();character.CurrentQuests=[new(1)];character.CurrentQuests[0].StartDateTime=DateTime.UtcNow;
        var npc=new NPCInfo{Index=1,Name="Native Funding Shop",FileName="NativeFundingShop",MapIndex=local.Index,Rate=150,
            Location=Functions.PointMove(start,MirDirection.Left,3)};
        world.NPCInfoList=[npc];world.NPCIndex=1;
        File.WriteAllText(Path.Combine(Settings.NPCPath,npc.FileName+".txt"),"[@MAIN]\n#SAY\n先补药再冒险。\n<购买/@BUYSELL>\n[@BUYSELL]\n#SAY\n含税采购。\n[Quests]\n1\n[Types]\n"+(int)ItemType.Potion+"\n[Trade]\nNativeFundingRed 20\n");
        if(remoteDelivery)
        {
            character.CurrentQuests[0].KillTaskCount[0].Count=1;
            var finish=new NPCInfo{Index=2,Name="Native Funding Delivery",FileName="NativeFundingDelivery",MapIndex=remote.Index,Location=Functions.PointMove(start,MirDirection.Left,3)};
            world.NPCInfoList.Add(finish);world.NPCIndex=2;
            File.WriteAllText(Path.Combine(Settings.NPCPath,finish.FileName+".txt"),"[@MAIN]\n#SAY\n补足药品之后才跨图交付。\n[Quests]\n-1\n");
        }
        world.SaveDB();world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-funding-fixture.json"),JsonSerializer.Serialize(new{origin=local.Index,remote=remote.Index,red=red.Index,
            tokenId=character.Inventory[0].UniqueID,weaponId=character.Equipment[0]?.UniqueID??0,gold=38,price=60,reward=333,evasiveSource,remoteDelivery,unarmed}));
        Console.WriteLine(harvestSource?"隔离筹资：采集AI9的分组金币仍只走原割取而不发放，不得误作死亡落金来源。":
            noSource?"隔离筹资：38金币/无红药且无金币来源，应保命等待而不硬冲远图任务。":
            "隔离筹资：38金币/无红药，原零经验弱怪掉金币、150%药价，补药后跨图完成原任务并交付333金币。");
    }
}
