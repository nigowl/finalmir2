using System.Drawing;
using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionDevelopmentScenario
{
    public static void Prepare(ProjectPaths paths,int characterId,bool blockedSupply=false,bool switchingShops=false,bool publicCompanions=false)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改隔离陪玩测试世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var character=world.CharacterList.Single(c=>c.Index==characterId);
        character.Level=23;character.HP=159;character.MP=158;character.Experience=0;character.Magics.Clear();
        character.CurrentQuests.Clear();character.CompletedQuests.Clear();character.Inventory=new UserItem[46];character.Equipment=new UserItem[14];
        character.CurrentMapIndex=character.BindMapIndex;character.CurrentLocation=character.BindLocation;character.AccountInfo.Gold=10000;
        foreach(var map in world.MapInfoList)map.Respawns.Clear();world.NPCInfoList.Clear();world.QuestInfoList.Clear();
        ItemInfo Add(string name,ItemType type,uint price,Stats? stats=null,ushort durability=0,short shape=0,ushort stack=1)
        {
            var info=new ItemInfo{Index=++world.ItemIndex,Name=name,Type=type,Price=price,RequiredClass=RequiredClass.Taoist,
                Durability=durability,Shape=shape,StackSize=stack,Stats=stats??new Stats()};world.ItemInfoList.Add(info);return info;
        }
        UserItem Fresh(ItemInfo info,ushort count=1){var item=world.CreateFreshItem(info);item.Count=count;return item;}
        var weapon=Add("NativeDevelopmentWeapon",ItemType.Weapon,100,new Stats{[Stat.MinDC]=1,[Stat.MaxDC]=2},5000);
        character.Equipment[0]=Fresh(weapon);
        var left=Fresh(Add("NativeDevelopmentLeftRing",ItemType.Ring,100,new Stats{[Stat.MaxAC]=5},5000));
        var right=Fresh(Add("NativeDevelopmentOldRightRing",ItemType.Ring,100,new Stats{[Stat.MaxAC]=1},5000));
        character.Equipment[7]=left;character.Equipment[8]=right;
        var red=Add("NativeDevelopmentRed",ItemType.Potion,40,new Stats{[Stat.HP]=100},stack:100);
        var blue=Add("NativeDevelopmentBlue",ItemType.Potion,40,new Stats{[Stat.MP]=100},stack:100);
        var town=Add("NativeDevelopmentTown",ItemType.Scroll,100,shape:1,stack:20);
        character.Inventory[0]=Fresh(red,20);character.Inventory[1]=Fresh(blue,20);character.Inventory[2]=Fresh(town);
        var book=Add("NativeDevelopmentBook",ItemType.Book,100,shape:(short)Spell.SoulFireBall);book.RequiredAmount=18;
        var ring=Add("NativeDevelopmentUpgradeRing",ItemType.Ring,100,new Stats{[Stat.MaxAC]=3},5000);
        var amulet=Add("NativeDevelopmentAmulet",ItemType.Amulet,1,shape:0,stack:100);
        var magic=world.MagicInfoList.Single(m=>m.Spell==Spell.SoulFireBall);
        magic.Level1=18;magic.BaseCost=5;magic.LevelCost=0;magic.DelayBase=1000;magic.PowerBase=80;magic.PowerBonus=0;
        var remote=world.GetMapInfo(2);
        if(blockedSupply)
        {
            var origin=world.GetMapInfo(character.BindMapIndex);origin.FileName="NativeBlockedSupply";origin.Movements.Clear();origin.SafeZones.Clear();
            character.CurrentLocation=character.BindLocation=new(10,16);character.Inventory[0]=null;
            origin.SafeZones.Add(new(){Info=origin,Location=new(10,16),Size=0,StartPoint=true});
            var header=new byte[52];BitConverter.GetBytes((short)32).CopyTo(header,0);BitConverter.GetBytes((short)32).CopyTo(header,2);
            using(var writer=new BinaryWriter(File.Create(Path.Combine(paths.Assets,"maps","NativeBlockedSupply.map"))))
            {writer.Write(header);for(var x=0;x<32;x++)for(var y=0;y<32;y++){writer.Write((ushort)(x>=2&&x<=18&&y>=2&&y<=29||x==23&&y==16?0:0x8000));writer.Write(new byte[10]);}}
            remote=origin;
            world.NPCInfoList.Add(new NPCInfo{Index=++world.NPCIndex,Name="隔离药店",FileName="NativeBlockedSupply",MapIndex=origin.Index,Location=new(23,16)});
            File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeBlockedSupply.txt"),"[@MAIN]\n#SAY\n补给。\n<购买/@BUY>\n[@BUY]\n#SAY\n原服药店。\n[Types]\n"+(int)ItemType.Potion+"\n[Trade]\n"+red.Name+" 100\n");
        }
        if(switchingShops)
        {
            var origin=world.GetMapInfo(character.BindMapIndex);origin.FileName="NativeSwitchingShops";origin.Movements.Clear();origin.SafeZones.Clear();
            character.CurrentLocation=character.BindLocation=new(10,16);
            origin.SafeZones.Add(new(){Info=origin,Location=character.BindLocation,Size=0,StartPoint=true});
            var header=new byte[52];BitConverter.GetBytes((short)400).CopyTo(header,0);BitConverter.GetBytes((short)40).CopyTo(header,2);
            using(var writer=new BinaryWriter(File.Create(Path.Combine(paths.Assets,"maps","NativeSwitchingShops.map"))))
            {writer.Write(header);for(var x=0;x<400;x++)for(var y=0;y<40;y++){writer.Write((ushort)(x>=2&&x<=390&&y>=10&&y<=20||x==10&&y==35?0:0x8000));writer.Write(new byte[10]);}}
            remote.FileName="NativePreferredShop";remote.Movements.Clear();remote.SafeZones.Clear();
            origin.Movements.Add(new(){Source=new(10,35),MapIndex=remote.Index,Destination=new(10,10)});
            world.NPCInfoList.Add(new NPCInfo{Index=++world.NPCIndex,Name="隔墙近店",FileName="NativeDevelopmentShop",MapIndex=remote.Index,Location=new(10,11)});
            var fallback=new MapInfo{Index=++world.MapIndex,Title="替代商店",FileName="NativeFallbackShop"};world.MapInfoList.Add(fallback);
            origin.Movements.Add(new(){Source=new(375,16),MapIndex=fallback.Index,Destination=new(10,10)});
            foreach(var shop in new[]{remote,fallback})
            {
                var shopHeader=new byte[52];BitConverter.GetBytes((short)32).CopyTo(shopHeader,0);BitConverter.GetBytes((short)32).CopyTo(shopHeader,2);
                using var writer=new BinaryWriter(File.Create(Path.Combine(paths.Assets,"maps",shop.FileName+".map")));
                writer.Write(shopHeader);for(var x=0;x<32;x++)for(var y=0;y<32;y++){writer.Write((ushort)(x>=2&&x<30&&y>=2&&y<30?0:0x8000));writer.Write(new byte[10]);}
            }
            remote=fallback;
        }
        world.NPCInfoList.Add(new NPCInfo{Index=++world.NPCIndex,Name="发展书店",FileName="NativeDevelopmentShop",MapIndex=remote.Index,
            Location=switchingShops?new(14,10):Functions.PointMove(character.BindLocation,MirDirection.Left,2)});
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeDevelopmentShop.txt"),"[@MAIN]\n#SAY\n购买技能和装备。\n<购买/@BUY>\n[@BUY]\n#SAY\n原服商店。\n[Types]\n"+
            (int)ItemType.Book+"\n"+(int)ItemType.Ring+"\n"+(int)ItemType.Amulet+"\n[Trade]\n"+book.Name+" 1\n"+ring.Name+" 1\n"+amulet.Name+" 100\n");
        var monster=new MonsterInfo{Index=++world.MonsterIndex,Name="NativeDevelopmentTarget",Level=23,Experience=25,AI=0,
            ViewRange=0,MoveSpeed=2000,AttackSpeed=3000,Stats=new Stats{[Stat.HP]=20}};world.MonsterInfoList=[monster];
        if(!blockedSupply&&!switchingShops)world.GetMapInfo(character.BindMapIndex).Respawns.Add(new RespawnInfo{MonsterIndex=monster.Index,Count=1,Delay=600,
            Location=Functions.PointMove(character.BindLocation,MirDirection.Right,5)});
        if(publicCompanions)
        {
            var origin=world.GetMapInfo(character.BindMapIndex);origin.Movements.Clear();origin.Respawns.Clear();
            character.Inventory[0]=null;character.Inventory[2]=null;
            amulet.Name="护身符（小）";amulet.StackSize=500;
            var meat=Add("隔离普通余肉",ItemType.Meat,100,stack:100);meat.Weight=1;
            for(var i=2;i<character.Inventory.Length;i++)character.Inventory[i]=Fresh(meat);
            var protectedItem=Add("隔离任务材料",ItemType.CraftingMaterial,100,stack:100);
            character.Inventory[2]=Fresh(protectedItem,7);
            var quest=new QuestInfo{Index=++world.QuestIndex,Name="隔离未来任务",FileName="NativePublicFuture"};
            File.WriteAllText(Path.Combine(Settings.QuestPath,quest.FileName+".txt"),$"[@ITEMTASKS]\n{protectedItem.Name} 1\n");
            quest.LoadInfo();world.QuestInfoList.Add(quest);
            File.AppendAllText(Path.Combine(Settings.NPCPath,"NativeDevelopmentShop.txt"),red.Name+" 100\n"+blue.Name+" 100\n"+town.Name+" 20\n");
            var lockedBook=Add("隔离未达技能等级书",ItemType.Book,1,shape:(short)Spell.SummonSkeleton);
            world.MagicInfoList.Single(m=>m.Spell==Spell.SummonSkeleton).Level1=99;
            File.AppendAllText(Path.Combine(Settings.NPCPath,"NativeDevelopmentShop.txt"),lockedBook.Name+" 1\n");
            // 原地回收后恢复空间，证明没有为商城/回收移动到不可达的远程NPC。
        }
        Settings.AutoLearnSkills=false;
        var setup=new InIReader(Path.Combine(paths.World,"Configs/Setup.ini"));setup.Write("AutoLearnSkills","Enabled",false);setup.Save();
        world.SaveDB();world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-development-fixture.json"),JsonSerializer.Serialize(new{
            book=book.Index,ring=ring.Index,amulet=amulet.Index,red=red.Index,town=town.Index,left=left.UniqueID,right=right.UniqueID,weapon=character.Equipment[0].UniqueID,origin=character.BindMapIndex,remote=remote.Index}));
        Console.WriteLine(blockedSupply?"已准备无红药/10000金币道士、隔墙药店及可达书店，验证补给排除后的保命优先。":"已准备满药但缺技能/护符、右戒指较差的道士，验证主动跨图购书学习、购符装备和原法术击杀。");
    }

    public static void OpenSupply(ProjectPaths paths)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改隔离陪玩测试世界。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.NPCInfoList.Single(n=>n.FileName=="NativeBlockedSupply").Location=new(12,16);world.SaveDB();
    }
}
