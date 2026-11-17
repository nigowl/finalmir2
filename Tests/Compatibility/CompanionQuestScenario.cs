using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionQuestScenario
{
    public static void Prepare(ProjectPaths paths,int characterId,bool travelThreat=false,bool fullBag=false,bool blockedReward=false,bool rewardRoom=false)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(role=>role.Index==characterId);var start=character.BindLocation;
        if(rewardRoom)
        {
            if(character.Inventory[45]?.Info.Name!="NativeQuestProtectedOre")throw new InvalidDataException("不是奖励满包夹具，拒绝腾格。");
            character.AccountInfo.Storage[1]=character.Inventory[45];character.Inventory[45]=null;
            foreach(var map in world.MapInfoList)map.NoFight=false;
            world.SaveDB();world.SaveAccounts();Console.WriteLine("隔离夹具将同编号矿石移入仓库，腾出1格奖励空间。");return;
        }
        character.CurrentMapIndex=character.BindMapIndex;character.CurrentLocation=start;character.Level=10;
        character.AccountInfo.Gold=0;character.Experience=0;character.CompletedQuests.Clear();
        character.Inventory=new UserItem[46];character.QuestInventory=new UserItem[40];
        foreach(var map in world.MapInfoList){map.Respawns.Clear();map.NoFight=blockedReward;}
        var local=world.MapInfoList.Single(map=>map.Index==character.BindMapIndex);
        var remote=world.MapInfoList.Single(map=>map.Index!=local.Index);
        var token=new ItemInfo{Index=++world.ItemIndex,Name="NativeQuestRemainder",Type=ItemType.Quest,StackSize=20};world.ItemInfoList.Add(token);
        character.QuestInventory[0]=world.CreateFreshItem(token);character.QuestInventory[0].Count=9;
        var reserve=new ItemInfo{Index=++world.ItemIndex,Name="NativeQuestReserve",Type=ItemType.CraftingMaterial,StackSize=100,Weight=1,Price=10};
        world.ItemInfoList.Add(reserve);character.Inventory[0]=world.CreateFreshItem(reserve);character.Inventory[0].Count=2;
        var reward=new ItemInfo{Index=++world.ItemIndex,Name="NativeQuestWhitespaceReward",Type=ItemType.Boots,Durability=5000,StackSize=1,Weight=1};
        world.ItemInfoList.Add(reward);
        var filler=new ItemInfo{Index=++world.ItemIndex,Name="NativeQuestProtectedOre",Type=ItemType.Ore,StackSize=1,Weight=1,Bind=BindMode.DontStore};world.ItemInfoList.Add(filler);
        for(var slot=1;slot<45;slot++)character.Inventory[slot]=world.CreateFreshItem(filler);
        var spareIds=new List<ulong>();
        if(fullBag||blockedReward)
        {
            character.HP=1000;
            if(blockedReward)character.Inventory[45]=world.CreateFreshItem(filler);
            else
            {
            var spare=new ItemInfo{Index=++world.ItemIndex,Name="NativeQuestSpareHelmet",Type=ItemType.Helmet,Durability=5000,
                StackSize=1,Weight=1,Price=10,RequiredClass=RequiredClass.Wizard};world.ItemInfoList.Add(spare);
            for(var slot=42;slot<46;slot++)
            {character.Inventory[slot]=world.CreateFreshItem(spare);spareIds.Add(character.Inventory[slot].UniqueID);}
            }
            character.QuestInventory[0].Count=10;
        }
        character.AccountInfo.Storage=new UserItem[160];character.AccountInfo.Storage[0]=world.CreateFreshItem(reserve);character.AccountInfo.Storage[0].Count=40;
        world.MonsterInfoList.Clear();
        var monsters=Enumerable.Range(1,2).Select(id=>new MonsterInfo{Index=id,Name="NativeQuestTarget"+id,Image=Monster.Hen,
            Level=1,AI=0,ViewRange=0,MoveSpeed=ushort.MaxValue,AttackSpeed=2500,Experience=10,Stats=new Stats{[Stat.HP]=1},DropPath="NativeQuestTarget"+id}).ToArray();
        world.MonsterInfoList.AddRange(monsters);world.MonsterIndex=2;
        if(travelThreat)
        {
            character.HP=character.MP=1000;character.Magics.Clear();character.Equipment=new UserItem[14];
            var blade=new ItemInfo{Index=++world.ItemIndex,Name="NativeTravelBlade",Type=ItemType.Weapon,Durability=5000,
                Stats=new Stats{[Stat.MinDC]=5,[Stat.MaxDC]=5,[Stat.Accuracy]=100}};
            world.ItemInfoList.Add(blade);character.Equipment[0]=world.CreateFreshItem(blade);
            world.MonsterInfoList.Add(new MonsterInfo{Index=++world.MonsterIndex,Name="NativeTravelDistraction",Image=Monster.Hen,
                AI=0,Level=1,ViewRange=16,MoveSpeed=1000,AttackSpeed=1000,Experience=1000,
                Stats=new Stats{[Stat.HP]=200,[Stat.MinDC]=1,[Stat.MaxDC]=1,[Stat.Accuracy]=100}});
            local.Respawns.Add(new RespawnInfo{RespawnIndex=3,MonsterIndex=world.MonsterIndex,Location=Functions.PointMove(start,MirDirection.Down,1),Count=1,Delay=600});
        }
        local.Respawns.Add(new RespawnInfo{RespawnIndex=1,MonsterIndex=2,Location=Functions.PointMove(start,MirDirection.DownRight,2),Count=1,Delay=1});
        remote.Respawns.Add(new RespawnInfo{RespawnIndex=2,MonsterIndex=1,Location=Functions.PointMove(start,MirDirection.DownRight,2),Count=1,Delay=1});
        File.WriteAllText(Path.Combine(Settings.DropPath,"NativeQuestTarget1.txt"),"");
        File.WriteAllText(Path.Combine(Settings.DropPath,"NativeQuestTarget2.txt"),"1/1 NativeQuestRemainder Q\n");
        world.QuestInfoList.Clear();
        foreach(var id in new[]{1,2})
        {
            var quest=new QuestInfo{Index=id,Name=id==1?"远图任务":"本图收尾",FileName="NativeQuestOrder"+id,RequiredMinLevel=1,RequiredMaxLevel=255,RequiredClass=RequiredClass.None};
            world.QuestInfoList.Add(quest);
            File.WriteAllText(Path.Combine(Settings.QuestPath,quest.FileName+".txt"),"[@DESCRIPTION]\n多任务排程回归。\n"+
                (id==1?"[@KILLTASKS]\nNativeQuestTarget1 1\n":"[@ITEMTASKS]\nNativeQuestRemainder 10\n[@FIXEDREWARDS]\nNativeQuestWhitespaceReward \t\n")+$"[@GOLDREWARD]\n{id*111}\n");
            quest.LoadInfo();
        }
        world.QuestIndex=2;character.CurrentQuests=[new(1),new(2)];character.CurrentQuests[1].ItemTaskCount[0].Count=9;
        foreach(var progress in character.CurrentQuests)progress.StartDateTime=DateTime.UtcNow;
        if(fullBag||blockedReward){character.CurrentQuests[1].ItemTaskCount[0].Count=10;character.CurrentQuests[1].EndDateTime=DateTime.UtcNow;}
        world.NPCInfoList=[new NPCInfo{Index=1,Name="Native Quest Clerk",FileName="NativeQuestClerk",MapIndex=local.Index,Location=Functions.PointMove(start,MirDirection.Left,4)}];
        world.NPCIndex=1;
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeQuestClerk.txt"),"[@MAIN]\n#SAY\n任务排程回归。\n[Quests]\n1\n2\n");
        world.NPCInfoList.Add(new NPCInfo{Index=2,Name="Native Reserve Warehouse",FileName="NativeQuestWarehouse",MapIndex=remote.Index,Location=Functions.PointMove(start,MirDirection.Left,4)});
        world.NPCIndex=2;
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeQuestWarehouse.txt"),"[@MAIN]\n#SAY\n闲时储备仓库。\n<仓库/@STORAGE>\n[@STORAGE]\n#SAY\n原仓库。\n");
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-quests-fixture.json"),JsonSerializer.Serialize(new{origin=local.Index,remote=remote.Index,token=token.Index,tokenId=character.QuestInventory[0].UniqueID,
            reserve=reserve.Index,reserveId=character.Inventory[0].UniqueID,storedId=character.AccountInfo.Storage[0].UniqueID,reward=reward.Index,travelThreat,fullBag,blockedReward,spareIds,
            blockedItemId=blockedReward?character.Inventory[45].UniqueID:0}));
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine(fullBag?"已设置满46格及本图任务可交：必须先按服务器计划存仓减负，再交付。":"已设置先接远图任务、本图收集9/10：必须先真实收集交付本图任务，再跨图完成原任务。");
    }
}
