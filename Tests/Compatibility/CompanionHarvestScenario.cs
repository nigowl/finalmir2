using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionHarvestScenario
{
    public static void Prepare(ProjectPaths paths,int characterId,bool needed)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(role=>role.Index==characterId);
        character.Level=10;character.Experience=0;character.HP=1000;character.AccountInfo.Gold=0;character.Magics.Clear();
        character.CurrentMapIndex=character.BindMapIndex;character.CurrentLocation=character.BindLocation;
        character.Inventory=new UserItem[46];character.Equipment=new UserItem[14];character.QuestInventory=new UserItem[40];
        character.CurrentQuests.Clear();character.CompletedQuests.Clear();
        foreach(var map in world.MapInfoList)map.Respawns.Clear();world.NPCInfoList.Clear();world.QuestInfoList.Clear();
        ItemInfo Item(string name,ItemType type,byte weight=0)
        {var item=new ItemInfo{Index=++world.ItemIndex,Name=name,Type=type,Weight=weight,StackSize=1};world.ItemInfoList.Add(item);return item;}
        var token=Item("NativeHarvestToken"+(needed?"Needed":"Full"),ItemType.Quest);
        var missing=Item("NativeHarvestMissing"+(needed?"Needed":"Full"),ItemType.Quest);
        var junk=Item("NativeHarvestByproduct"+(needed?"Needed":"Full"),ItemType.Ore,1);
        var load=Item("NativeHarvestLoad"+(needed?"Needed":"Full"),ItemType.Ore,255);
        for(var i=0;i<4;i++)character.Inventory[i]=world.CreateFreshItem(load);
        var blade=Item("NativeHarvestBlade"+(needed?"Needed":"Full"),ItemType.Weapon);blade.Durability=5000;
        blade.Stats=new Stats{[Stat.MinDC]=100,[Stat.MaxDC]=100,[Stat.Accuracy]=100};character.Equipment[0]=world.CreateFreshItem(blade);
        if(!needed)character.QuestInventory[0]=world.CreateFreshItem(token);
        var quest=new QuestInfo{Index=++world.QuestIndex,Name="Native Harvest Partial Quest",FileName="NativeHarvestQuest"+(needed?"Needed":"Full")};
        world.QuestInfoList.Add(quest);
        File.WriteAllText(Path.Combine(Settings.QuestPath,quest.FileName+".txt"),$"[@DESCRIPTION]\n只按未完成物品判断必要采集。\n[@ITEMTASKS]\n{token.Name} 1\n{missing.Name} 1\n");
        quest.LoadInfo();var progress=new QuestProgressInfo(quest.Index){StartDateTime=DateTime.UtcNow};
        progress.ItemTaskCount[0].Count=needed?0:1;character.CurrentQuests.Add(progress);
        var name="NativeHarvestCorpse"+(needed?"Needed":"Full");
        var monster=new MonsterInfo{Index=++world.MonsterIndex,Name=name,AI=9,Image=Monster.Hen,Level=1,Experience=10,
            Stats=new Stats{[Stat.HP]=1},ViewRange=0,MoveSpeed=ushort.MaxValue};world.MonsterInfoList.Add(monster);
        File.WriteAllText(Path.Combine(Settings.DropPath,name+".txt"),$"1/1 {token.Name} Q\n1/1 {junk.Name}\n");
        world.MapInfoList.Single(map=>map.Index==character.BindMapIndex).Respawns.Add(new RespawnInfo{RespawnIndex=++world.RespawnIndex,
            MonsterIndex=monster.Index,Location=Functions.PointMove(character.BindLocation,MirDirection.Down,1),Count=1,Delay=600});
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-harvest-fixture.json"),JsonSerializer.Serialize(new{
            token=token.Index,junk=junk.Index,quest=quest.Index,items=character.Inventory.Concat(character.Equipment).Concat(character.QuestInventory).Where(item=>item!=null).Select(item=>item.UniqueID)}));
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine(needed?"已设置超重但真实缺少尸体任务物的隔离场景。":"已设置超重、此尸体任务物已齐而另一任务物仍缺的隔离场景。");
    }
}
