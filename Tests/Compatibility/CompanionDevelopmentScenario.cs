using System.Drawing;
using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionDevelopmentScenario
{
    public static void Prepare(ProjectPaths paths,int characterId)
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
        world.NPCInfoList.Add(new NPCInfo{Index=++world.NPCIndex,Name="发展书店",FileName="NativeDevelopmentShop",MapIndex=remote.Index,
            Location=Functions.PointMove(character.BindLocation,MirDirection.Left,2)});
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativeDevelopmentShop.txt"),"[@MAIN]\n#SAY\n购买技能和装备。\n<购买/@BUY>\n[@BUY]\n#SAY\n原服商店。\n[Types]\n"+
            (int)ItemType.Book+"\n"+(int)ItemType.Ring+"\n"+(int)ItemType.Amulet+"\n[Trade]\n"+book.Name+" 1\n"+ring.Name+" 1\n"+amulet.Name+" 100\n");
        var monster=new MonsterInfo{Index=++world.MonsterIndex,Name="NativeDevelopmentTarget",Level=23,Experience=25,AI=0,
            ViewRange=0,MoveSpeed=2000,AttackSpeed=3000,Stats=new Stats{[Stat.HP]=20}};world.MonsterInfoList=[monster];
        world.GetMapInfo(character.BindMapIndex).Respawns.Add(new RespawnInfo{MonsterIndex=monster.Index,Count=1,Delay=600,
            Location=Functions.PointMove(character.BindLocation,MirDirection.Right,5)});
        Settings.AutoLearnSkills=false;
        var setup=new InIReader(Path.Combine(paths.World,"Configs/Setup.ini"));setup.Write("AutoLearnSkills","Enabled",false);setup.Save();
        world.SaveDB();world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-development-fixture.json"),JsonSerializer.Serialize(new{
            book=book.Index,ring=ring.Index,amulet=amulet.Index,left=left.UniqueID,right=right.UniqueID,weapon=character.Equipment[0].UniqueID,origin=character.BindMapIndex,remote=remote.Index}));
        Console.WriteLine("已准备满药但缺技能/护符、右戒指较差的道士，验证主动跨图购书学习、购符装备和原法术击杀。");
    }
}
