using System.Text.Json;
using Mir2.Companions;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionMeatScenario
{
    public static void Prepare(ProjectPaths paths,int characterId)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(role=>role.Index==characterId);
        var supplies=character.Inventory.Where(item=>CompanionSupplyRules.IsPotion(item,Stat.HP)||CompanionSupplyRules.IsPotion(item,Stat.MP)||CompanionSupplyRules.IsTownScroll(item)).ToArray();
        character.Inventory=new UserItem[46];Array.Copy(supplies,character.Inventory,supplies.Length);
        character.CurrentQuests.Clear();character.CompletedQuests=world.QuestInfoList.Select(quest=>quest.Index).ToList();
        foreach(var map in world.MapInfoList)map.Respawns.Clear();
        var remote=world.MapInfoList.Single(map=>map.Index==2);
        character.CurrentMapIndex=remote.Index;character.CurrentLocation=remote.Movements[0].Source;
        var definitions=new[]{"NativeSaleMeat","NativeFutureMeat","NativeRecipeMeat"}.Select(name=>new ItemInfo{
            Index=++world.ItemIndex,Name=name,Type=ItemType.Meat,Price=100,Durability=8000,Weight=1}).ToArray();
        world.ItemInfoList.AddRange(definitions);
        var protectedIds=new List<ulong>();var saleIds=new List<ulong>();
        foreach(var info in definitions.Skip(1))
        {
            var item=world.CreateFreshItem(info);character.Inventory[Array.IndexOf(character.Inventory,null)]=item;protectedIds.Add(item.UniqueID);
        }
        foreach(var enhanced in new[]{false,true})
        {
            var item=world.CreateFreshItem(definitions[0]);
            if(enhanced)item.AddedStats[Stat.HP]=1;else item.SoulBoundId=character.Index;
            character.Inventory[Array.IndexOf(character.Inventory,null)]=item;protectedIds.Add(item.UniqueID);
        }
        for(var slot=0;slot<character.Inventory.Length;slot++)
        {
            if(character.Inventory[slot]!=null)continue;
            var item=world.CreateFreshItem(definitions[0]);item.CurrentDura=(ushort)(11000+slot);
            if(saleIds.Count==0)item.Count=3;
            character.Inventory[slot]=item;saleIds.Add(item.UniqueID);
        }
        world.QuestInfoList.Add(new QuestInfo{Index=++world.QuestIndex,Name="未接取肉类保护",FileName="NativeFutureMeat",RequiredMaxLevel=255});
        File.WriteAllText(Path.Combine("Envir","Quests","NativeFutureMeat.txt"),"[@ITEMTASKS]\nNativeFutureMeat 1\n");
        File.AppendAllText(Path.Combine(Settings.RecipePath,"NativeBackup.txt"),"NativeRecipeMeat 1\n");
        var shop=Path.Combine("Envir","NPCs","NativeShop.txt");
        File.WriteAllText(shop,File.ReadAllText(shop).Replace("[Types]\n",$"[Types]\n{(int)ItemType.Meat}\n"));
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-meat-fixture.json"),JsonSerializer.Serialize(new{
            saleIds,protectedIds,origin=remote.Index,destination=world.NPCInfoList.Single(npc=>npc.Index==1).MapIndex,
            gold=character.AccountInfo.Gold,weapon=character.Equipment[0].UniqueID,armour=character.Equipment[1].UniqueID}));
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine("已设置仅普通肉可减负、满包、无任务或怪物引路的跨图场景，保留未接任务/配方/绑定/强化肉。");
    }
}
