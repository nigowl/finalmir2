using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionStorageScenario
{
    public static void Prepare(ProjectPaths paths,int cid,string mode)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许本测试的隔离版本。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();var c=world.CharacterList.Single(c=>c.Index==cid);
        if(mode=="verify-password")
        {if(!c.AccountInfo.ValidateStoragePassword("Manual12345"))throw new InvalidDataException("原人工密码被自动覆盖。");Console.WriteLine("PASS 人工仓库密码仍有效，未被陪玩覆盖。");return;}
        ulong[] equipmentAllowed=[];
        if(mode=="prepare")Configure(world,c);
        else if(mode=="equipment")equipmentAllowed=ConfigureEquipment(world,c);
        else
        {
            var carried=c.Inventory.First(i=>i?.Info.Name=="NativeStorageMaterial");carried.Count=(ushort)(mode=="copy"?2:200);
            if(mode=="copy")
            {
                foreach(var map in world.MapInfoList){map.Index+=70;foreach(var exit in map.Movements)exit.MapIndex+=70;}
                foreach(var npc in world.NPCInfoList)npc.MapIndex+=70;
                c.CurrentMapIndex+=70;c.BindMapIndex+=70;world.MapIndex+=70;
            }
            else if(mode is "full" or "wrong" or "optional")
            {
                for(var i=0;i<c.Inventory.Length;i++)
                    if(c.Inventory[i]?.ItemIndex==carried.ItemIndex&&c.Inventory[i]!=carried)c.Inventory[i]=null;
                c.AccountInfo.Storage=new UserItem[160];
                if(mode=="full")
                {
                    var filler=new ItemInfo{Index=++world.ItemIndex,Name="NativeStorageProtectedFiller",Type=ItemType.Ore};world.ItemInfoList.Add(filler);
                    for(var i=0;i<80;i++)c.AccountInfo.Storage[i]=world.CreateFreshItem(filler);
                }
                else if(mode=="wrong"){c.AccountInfo.StoragePassword="Manual12345";c.AccountInfo.StoragePasswordLastSet=DateTime.Now;}
                else c.AccountInfo.ClearStoragePassword();
                new InIReader(Path.Combine("Configs","Setup.ini")).Write("Permission","RequireStoragePassword",mode!="optional");
            }
            else throw new InvalidDataException("未知测试模式。");
        }
        var material=c.Inventory.Single(i=>i?.Info.Name=="NativeStorageMaterial");
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-storage-fixture.json"),JsonSerializer.Serialize(new{
            mode,material=material.ItemIndex,materialId=material.UniqueID,materialCount=material.Count,gold=c.AccountInfo.Gold,map=c.CurrentMapIndex,equipmentAllowed,
            red=world.ItemInfoList.Single(i=>i.Name=="NativeStorageRed").Index,blue=world.ItemInfoList.Single(i=>i.Name=="NativeStorageBlue").Index,
            original=c.Inventory.Concat(c.Equipment).Where(i=>i!=null).Select(i=>new{id=i.UniqueID,count=i.Count,index=i.ItemIndex}),
            stored=c.AccountInfo.Storage.Where(i=>i!=null).Select(i=>new{id=i.UniqueID,count=i.Count,index=i.ItemIndex}),
            equipped=c.Equipment.Select((item,slot)=>(item,slot)).Where(p=>p.item!=null).Select(p=>new{id=p.item.UniqueID,slot=p.slot})}));
        world.SaveDB();world.SaveAccounts();Console.WriteLine("已准备原仓库隔离场景："+mode);
    }
    private static ulong[] ConfigureEquipment(Envir world,CharacterInfo c)
    {
        c.Inventory=new UserItem[46];c.Equipment=new UserItem[14];c.QuestInventory=new UserItem[40];c.Magics.Clear();
        c.CurrentQuests.Clear();c.CompletedQuests.Clear();c.AccountInfo.Gold=1000;c.AccountInfo.Storage=new UserItem[160];
        c.CurrentMapIndex=c.BindMapIndex;c.CurrentLocation=c.BindLocation;c.HP=c.MP=1000;
        ItemInfo Add(string name,ItemType type,byte weight=1)
        {var i=new ItemInfo{Index=++world.ItemIndex,Name=name,Type=type,Price=100,Weight=weight,Durability=10000};world.ItemInfoList.Add(i);return i;}
        UserItem Put(ItemInfo info,ushort count=1,ushort dura=8000)
        {var item=world.CreateFreshItem(info);item.Count=count;item.CurrentDura=dura;c.Inventory[Array.IndexOf(c.Inventory,null)]=item;return item;}
        var blade=world.ItemInfoList.Single(i=>i.Name=="NativeStorageBlade");c.Equipment[0]=world.CreateFreshItem(blade);
        var worn=Add("NativeStorageWorn",ItemType.Necklace,0);worn.Stats[Stat.MaxDC]=100;c.Equipment[4]=world.CreateFreshItem(worn);
        var wornRing=Add("NativeStorageWornRing",ItemType.Ring,0);wornRing.Stats[Stat.MaxDC]=100;
        foreach(var slot in new[]{7,8})c.Equipment[slot]=world.CreateFreshItem(wornRing);
        var material=world.ItemInfoList.Single(i=>i.Name=="NativeStorageMaterial");Put(material,5,0);
        foreach(var name in new[]{"NativeStorageRed","NativeStorageBlue"})Put(world.ItemInfoList.Single(i=>i.Name==name),20,0);
        var necklace=Add("NativeStorageNecklace",ItemType.Necklace,2);Put(necklace,1,9000);
        var allowed=new List<ulong>{Put(necklace,1,6000).UniqueID,Put(necklace,1,3000).UniqueID};
        Put(necklace).AddedStats[Stat.MaxAC]=1;Put(necklace).MaxDura=12000;
        var ring=Add("NativeStorageRing",ItemType.Ring);Put(ring,1,9000);Put(ring,1,8000);allowed.Add(Put(ring,1,7000).UniqueID);
        var future=Add("NativeStorageFutureNecklace",ItemType.Necklace,2);for(var i=0;i<3;i++)Put(future);
        var quest=new QuestInfo{Index=++world.QuestIndex,Name="Native Equipment Future Quest",FileName="NativeStorageEquipmentFuture"};
        File.WriteAllText(Path.Combine(Settings.QuestPath,quest.FileName+".txt"),$"[@ITEMTASKS]\n{future.Name} 3\n");quest.LoadInfo();world.QuestInfoList.Add(quest);
        var filler=Add("NativeStorageInventoryFiller",ItemType.Ore,0);while(c.Inventory.Any(i=>i==null))Put(filler);
        // 本场景专测重复备装；矿石现可自动存仓，另以任务保护隔离填充物。
        File.AppendAllText(Path.Combine(Settings.QuestPath,quest.FileName+".txt"),$"{filler.Name} 46\n");quest.LoadInfo(true);
        return allowed.ToArray();
    }
    private static void Configure(Envir world,CharacterInfo c)
    {
        c.Level=10;c.HP=c.MP=1000;c.Experience=0;c.AccountInfo.Gold=1000;c.Magics.Clear();
        c.Inventory=new UserItem[46];c.Equipment=new UserItem[14];c.QuestInventory=new UserItem[40];
        c.CurrentQuests.Clear();c.CompletedQuests.Clear();c.CurrentMapIndex=c.BindMapIndex;c.CurrentLocation=c.BindLocation;
        c.AccountInfo.Storage=new UserItem[160];foreach(var map in world.MapInfoList)map.Respawns.Clear();world.QuestInfoList.Clear();world.NPCInfoList.Clear();
        ItemInfo Add(string name,ItemType type,byte weight=1,uint price=10)
        {var i=new ItemInfo{Index=++world.ItemIndex,Name=name,Type=type,Weight=weight,Price=price,StackSize=999};world.ItemInfoList.Add(i);return i;}
        UserItem Put(ItemInfo i,ushort count)
        {var item=world.CreateFreshItem(i);item.Count=count;c.Inventory[Array.IndexOf(c.Inventory,null)]=item;return item;}
        var material=Add("NativeStorageMaterial",ItemType.CraftingMaterial);Put(material,200);
        var red=Add("NativeStorageRed",ItemType.Potion,1,20);red.Stats[Stat.HP]=30;red.StackSize=20;Put(red,14);
        var blue=Add("NativeStorageBlue",ItemType.Potion,1,30);blue.Stats[Stat.MP]=30;blue.StackSize=20;Put(blue,3);
        var future=Add("NativeStorageFutureToken",ItemType.CraftingMaterial);Put(future,20);
        var special=Add("NativeStorageSpecial",ItemType.CraftingMaterial);Put(special,10).AddedStats[Stat.MaxDC]=1;
        var bound=Add("NativeStorageBound",ItemType.CraftingMaterial);bound.Bind=BindMode.DontStore;Put(bound,10);
        var tool=Add("NativeStorageBlade",ItemType.Weapon,0,0);tool.Shape=1;tool.Durability=5000;tool.Stats[Stat.BagWeight]=100;c.Equipment[0]=world.CreateFreshItem(tool);
        var product=Add("NativeStorageRecipe",ItemType.Script,0,0);Directory.CreateDirectory(Settings.RecipePath);
        File.WriteAllText(Path.Combine(Settings.RecipePath,product.Name+".txt"),$"[Ingredients]\n{material.Name} 1\n");
        world.QuestInfoList.Add(new QuestInfo{Index=1,Name="Native Future Quest",FileName="NativeStorageFuture"});world.QuestIndex=1;
        File.WriteAllText(Path.Combine(Settings.QuestPath,"NativeStorageFuture.txt"),$"[@ITEMTASKS]\n{future.Name} 3\n");world.QuestInfoList[0].LoadInfo();
        var origin=c.BindMapIndex;var remote=world.MapInfoList.Single(m=>m.Index!=origin).Index;
        world.NPCInfoList.Add(new NPCInfo{Index=1,Name="Native False Warehouse",FileName="NativeFalseWarehouse",MapIndex=origin,Location=Functions.PointMove(c.BindLocation,MirDirection.Left,2)});
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeFalseWarehouse.txt"),"[@MAIN]\n#IF\nLEVEL > 99\n#SAY\n<仓库/@STORAGE>\n#ELSESAY\n没有仓库链接。\n<返回/@MAIN>\n[@STORAGE]\n#SAY\n不可绕过实际链接。\n");
        world.NPCInfoList.Add(new NPCInfo{Index=2,Name="Native Warehouse",FileName="NativeWarehouse",MapIndex=remote,Location=Functions.PointMove(c.BindLocation,MirDirection.Left,2)});world.NPCIndex=2;
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeWarehouse.txt"),$"[@MAIN]\n#SAY\n原仓库与补给。\n<仓库/@STORAGE> <购买/@BUY>\n[@STORAGE]\n#SAY\n原密码校验。\n[@BUY]\n#SAY\n原服药品。\n[Types]\n{(int)ItemType.Potion}\n[Trade]\n{red.Name} 1\n{blue.Name} 1\n");
        var ini=new InIReader(Path.Combine("Configs","Setup.ini"));ini.Write("Permission","RequireStoragePassword",true);
    }
}
