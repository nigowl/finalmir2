using Mir2.Hosting;
using Client.MirObjects;
using Server;
using Server.ConsoleHost;
using Server.MirEnvir;
using Mir2.Companions;
using System.Text.Json;

internal static class CompanionScenario
{
    public static void PrepareStockedSale(ProjectPaths paths, int characterId)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(Server.MirDatabase.BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(c=>c.Index==characterId);
        if(character.Equipment[0]==null||CompanionSupplyRules.Count(character.Inventory,Stat.HP)<CompanionSupplyRules.Target(Stat.HP,character.Class,50)||
            CompanionSupplyRules.TownCount(character.Inventory,character.Class,character.Gender,character.Level)==0)
            throw new InvalidDataException("仅出售场景须已装备武器、药品和回城卷齐全。");
        var info=world.ItemInfoList.Single(item=>item.Name=="NativeDuplicateArmour");
        var sold=new List<ulong>();
        for(var slot=0;slot<character.Inventory.Length;slot++)
        {
            if(character.Inventory[slot]!=null)continue;
            var item=world.CreateFreshItem(info);item.CurrentDura=1000;
            character.Inventory[slot]=item;sold.Add(item.UniqueID);
        }
        var shop=Path.Combine("Envir","NPCs","NativeShop.txt");
        var tempting=new ItemInfo{Index=++world.ItemIndex,Name="NativeTemptingArmour",Type=ItemType.Armour,Price=10,Weight=1,Durability=5000,
            Stats=new Stats{[Stat.MaxAC]=50}};
        world.ItemInfoList.Add(tempting);
        File.WriteAllText(shop,File.ReadAllText(shop).Replace("<Buy/@BUY> <Sell/@SELL>","<查看/@BuySell>").Replace("[@SELL]","[@BUYSELL]")
            .Replace("[Trade]\n",$"[Trade]\n{tempting.Name} 1\n"));
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-stocked-sale-fixture.json"),JsonSerializer.Serialize(new{sold}));
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine("已设置药卷齐全、仅有BUYSELL入口的满包普通重复衣服减负场景。");
    }

    public static void PrepareTown(ProjectPaths paths, int characterId, bool denied)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(Server.MirDatabase.BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(c=>c.Index==characterId);
        var remote=world.MapInfoList.Single(map=>map.Index==2);
        remote.NoDrug=true;remote.NoEscape=true;remote.NoTownTeleport=denied;
        character.CurrentMapIndex=remote.Index;
        character.CurrentLocation=Functions.PointMove(character.BindLocation,MirDirection.Right,3);
        character.HP=10;character.MP=0;character.Magics.Clear();
        character.Inventory=new UserItem[46];
        var info=world.ItemInfoList.First(item=>item.Type==ItemType.Scroll&&item.Shape==1);
        info.StackSize=20;info.RequiredClass=RequiredClass.None;info.RequiredGender=RequiredGender.None;
        var scroll=world.CreateFreshItem(info);scroll.Count=2;character.Inventory[0]=scroll;
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine($"已设置隔离回城场景：10HP、卷轴2张、禁药/禁地牢逃脱、禁回城={denied}。");
    }

    public static void PrepareHealing(ProjectPaths paths, int characterId, bool retirement=false)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(Server.MirDatabase.BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(c=>c.Index==characterId);
        if(character.Class!=MirClass.Taoist)throw new InvalidDataException("自疗场景必须使用道士。");
        character.Level=15;character.HP=25;character.MP=74;
        character.CurrentMapIndex=character.BindMapIndex;
        character.CurrentLocation=Functions.PointMove(character.BindLocation,MirDirection.Right,3);
        character.Inventory=new UserItem[46];character.Equipment=new UserItem[14];
        var healing=world.MagicInfoList.Single(magic=>magic.Spell==Spell.Healing);
        healing.BaseCost=4;healing.LevelCost=0;healing.PowerBase=15;healing.PowerBonus=0;healing.DelayBase=1800;
        if(retirement)
        {
            var reserve=new ItemInfo{Index=++world.ItemIndex,Name="NativeHealingReserve",Type=ItemType.Weapon,Durability=5000,Stats=new Stats{[Stat.HP]=2000}};
            world.ItemInfoList.Add(reserve);character.Equipment[0]=world.CreateFreshItem(reserve);
            world.GetMapInfo(character.CurrentMapIndex).NoDrug=true;
        }
        character.Magics=[new Server.MirDatabase.UserMagic(Spell.Healing)];
        foreach(var map in world.MapInfoList)map.Respawns.Clear();
        world.SaveDB();world.SaveAccounts();
        Console.WriteLine(retirement?"已设置隔离安静退场：低血道士、原治愈术、额外2000最大生命、无药品/怪物，不能反复施法刷新退出保护。":"已设置隔离低血道士：25HP/74MP、已学治愈术、无药品/装备、非安全区、无怪物。");
    }

    public static void Prepare(ProjectPaths paths,int characterId,bool variant=false)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改本测试创建的隔离世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(Server.MirDatabase.BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(c=>c.Index==characterId);
        if(variant)
        {
            foreach(var map in world.MapInfoList)
            {
                map.Index+=20;map.Title="Variant "+map.Title;
                foreach(var exit in map.Movements){exit.MapIndex+=20;exit.ShowOnBigMap=true;}
                if(map.Index==22)map.Respawns.Clear();
            }
            foreach(var npc in world.NPCInfoList)npc.MapIndex+=20;
            foreach(var role in world.CharacterList){role.CurrentMapIndex+=20;role.BindMapIndex+=20;}
            world.MapIndex+=20;world.SaveDB();world.SaveAccounts();
            Console.WriteLine("已设置隔离 B 版本的不同地图编号/名称与公开入口。");return;
        }
        var weapon=character.Equipment.First(i=>i?.Info?.Type==ItemType.Weapon);
        weapon.CurrentDura=100;weapon.MaxDura=Math.Max((ushort)10000,weapon.MaxDura);
        for(var i=0;i<character.Inventory.Length;i++)if(character.Inventory[i]?.Info?.Type==ItemType.Potion || CompanionSupplyRules.IsTownScroll(character.Inventory[i]))character.Inventory[i]=null;
        var torch=world.CreateFreshItem(world.ItemInfoList.First(item=>item.Type==ItemType.Torch&&!item.Bind.HasFlag(BindMode.DontRepair)));
        torch.CurrentDura=100;torch.MaxDura=10000;
        character.Inventory[Array.IndexOf(character.Inventory,null)]=torch;
        foreach(var (name,power,weight,bind) in new[]{("NativeBackup",90,(byte)1,BindMode.None),("NativeSurplus",3,(byte)200,BindMode.None),("NativeProtected",1,(byte)0,BindMode.DontSell)})
        {
            var info=new ItemInfo{Index=++world.ItemIndex,Name=name,Type=ItemType.Weapon,Price=100,Durability=5000,Weight=weight,
                RequiredClass=RequiredClass.Warrior,RequiredGender=RequiredGender.Male,Bind=bind,Stats=new Stats{[Stat.MaxDC]=power}};
            world.ItemInfoList.Add(info);
            character.Inventory[Array.IndexOf(character.Inventory,null)]=world.CreateFreshItem(info);
        }
        character.AccountInfo.Gold=10000;
        PrepareMaterials(world,character,paths);
        var remote=world.MapInfoList.Single(map=>map.Index==2);
        remote.Respawns.Clear();
        var terrain=new MapReader(Path.Combine(paths.Assets,"maps",remote.FileName+".map"));
        bool Clear(System.Drawing.Point point)=>point.X>=0&&point.Y>=0&&point.X<terrain.Width&&point.Y<terrain.Height&&
            (terrain.MapCells[point.X,point.Y].BackImage&0x20000000)==0&&(terrain.MapCells[point.X,point.Y].FrontImage&0x8000)==0;
        var escape=remote.Movements[0].Source;
        var approach=Enumerable.Range(0,8).Select(direction=>Enumerable.Range(1,12).Select(step=>Functions.PointMove(escape,(MirDirection)direction,step)).ToArray())
            .First(line=>line.All(Clear));
        var threat=new Server.MirDatabase.MonsterInfo{Index=++world.MonsterIndex,Name="NativeSupplyThreat",Image=Monster.Oma,AI=0,
            Level=1,ViewRange=12,MoveSpeed=100,AttackSpeed=2500,Stats=new Stats{[Stat.HP]=20,[Stat.MinDC]=6,[Stat.MaxDC]=6,[Stat.Accuracy]=100}};
        world.MonsterInfoList.Add(threat);
        remote.Respawns.Add(new Server.MirDatabase.RespawnInfo{RespawnIndex=++world.RespawnIndex,MonsterIndex=threat.Index,
            Location=approach[^2],Count=1,Delay=600});
        character.CurrentMapIndex=remote.Index;character.CurrentLocation=approach[^1];
        world.SaveDB();world.SaveAccounts();Console.WriteLine("已设置跨图超重减负/备装保护/缺药、回补遇袭、破损武器及不支持修理的蜡烛场景。");
    }

    private static void PrepareMaterials(Envir world,Server.MirDatabase.CharacterInfo character,ProjectPaths paths)
    {
        var town=new ItemInfo{Index=++world.ItemIndex,Name="NativeTownScroll",Type=ItemType.Scroll,Shape=1,Price=1000,Weight=1,StackSize=20,
            RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None,RequiredType=RequiredType.Level};
        world.ItemInfoList.Add(town);
        foreach(var (name,type,counts) in new[]{("NativeMaterial",ItemType.CraftingMaterial,new ushort[]{2,9}),
            ("NativeFood",ItemType.Food,new ushort[]{11}),("NativeQuestMaterial",ItemType.CraftingMaterial,new ushort[]{10}),
            ("NativeRecipeMaterial",ItemType.CraftingMaterial,new ushort[]{10})})
        {
            // 卖掉武器/衣服后仍需部分出售材料，不依赖监管刷新时序碰巧多卖。
            var info=new ItemInfo{Index=++world.ItemIndex,Name=name,Type=type,Price=100,Weight=(byte)(name is "NativeMaterial" or "NativeFood"?2:0),StackSize=50};
            world.ItemInfoList.Add(info);
            foreach(var count in counts)
            {
                var item=world.CreateFreshItem(info);item.Count=count;
                character.Inventory[Array.IndexOf(character.Inventory,null)]=item;
            }
        }
        world.QuestInfoList.Add(new Server.MirDatabase.QuestInfo{Index=++world.QuestIndex,Name="Native Material Protection",FileName="NativeMaterialProtection",RequiredMaxLevel=255});
        File.WriteAllText(Path.Combine("Envir","Quests","NativeMaterialProtection.txt"),"[@ITEMTASKS]\nNativeQuestMaterial 10\n");
        Directory.CreateDirectory(Settings.RecipePath);
        File.WriteAllText(Path.Combine(Settings.RecipePath,"NativeBackup.txt"),"NativeRecipeMaterial 1\n");
        var shop=Path.Combine("Envir","NPCs","NativeShop.txt");
        File.WriteAllText(shop,File.ReadAllText(shop).Replace("[Types]\n",$"[Types]\n{(int)ItemType.CraftingMaterial}\n{(int)ItemType.Food}\n{(int)ItemType.Scroll}\n")
            .Replace("[Trade]\n",$"[Trade]\n{town.Name} 1\n"));
        var red=world.ItemInfoList.First(item=>item.Type==ItemType.Potion&&item.Shape==0&&item.Stats[Stat.HP]>0);
        var armour=PrepareArmour(world,character);
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-supply-fixture.json"),JsonSerializer.Serialize(new{potionId=red.Index,townId=town.Index,townPrice=town.Price,
            armourItemId=armour.ItemId,armourKeep=armour.Keep,armourEnhanced=armour.Enhanced,armourSold=armour.Sold,armourQuest=armour.Quest,
            equippedArmour=character.Equipment[(int)EquipmentSlot.Armour]?.UniqueID}));
    }

    private static (int ItemId,ulong Keep,ulong Enhanced,ulong[] Sold,ulong[] Quest) PrepareArmour(Envir world,Server.MirDatabase.CharacterInfo character)
    {
        var info=new ItemInfo{Index=++world.ItemIndex,Name="NativeDuplicateArmour",Type=ItemType.Armour,Price=100,Durability=5000,Weight=1,
            RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None};
        world.ItemInfoList.Add(info);
        var items=Enumerable.Range(0,4).Select(_=>world.CreateFreshItem(info)).ToArray();
        for(var i=0;i<items.Length;i++)
        {
            items[i].CurrentDura=(ushort)(5000-i*500);
            character.Inventory[Array.IndexOf(character.Inventory,null)]=items[i];
        }
        items[3].AddedStats[Stat.MP]=1;
        var questInfo=new ItemInfo{Index=++world.ItemIndex,Name="NativeQuestArmour",Type=ItemType.Armour,Price=100,Durability=5000,Weight=1,
            RequiredClass=RequiredClass.None,RequiredGender=RequiredGender.None};
        world.ItemInfoList.Add(questInfo);
        var quest=Enumerable.Range(0,3).Select(_=>world.CreateFreshItem(questInfo)).ToArray();
        foreach(var item in quest)character.Inventory[Array.IndexOf(character.Inventory,null)]=item;
        File.AppendAllText(Path.Combine("Envir","Quests","NativeMaterialProtection.txt"),"NativeQuestArmour 3\n");
        return(info.Index,items[0].UniqueID,items[3].UniqueID,[items[1].UniqueID,items[2].UniqueID],quest.Select(item=>item.UniqueID).ToArray());
    }
}
