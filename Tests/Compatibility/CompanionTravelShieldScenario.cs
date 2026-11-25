using System.Drawing;
using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionTravelShieldScenario
{
    public static void Prepare(ProjectPaths paths,int characterId,bool quiet)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改隔离陪玩版本。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var actor=world.CharacterList.Single(c=>c.Index==characterId);
        if(actor.Class!=MirClass.Wizard)throw new InvalidDataException("赶路护盾夹具要求原法师角色。");
        foreach(var existing in world.MapInfoList)existing.Respawns.Clear();world.NPCInfoList.Clear();world.QuestInfoList.Clear();
        var map=new MapInfo{Index=++world.MapIndex,FileName="NativeTravelShield",Title="隔离护盾交付通道",NoTownTeleport=true,NoDropPlayer=true,Light=LightSetting.Day};
        map.SafeZones.Add(new(){Info=map,Location=new(5,5),Size=0,StartPoint=true});world.MapInfoList.Add(map);
        actor.Level=30;actor.HP=actor.MP=10000;actor.Experience=0;actor.AccountInfo.Gold=10000;
        actor.CurrentMapIndex=actor.BindMapIndex=map.Index;actor.CurrentLocation=new(10,12);actor.BindLocation=new(5,5);
        actor.Inventory=new UserItem[46];actor.Equipment=new UserItem[14];actor.QuestInventory=new UserItem[40];actor.CompletedQuests.Clear();
        var shield=world.MagicInfoList.Single(m=>m.Spell==Spell.MagicShield);shield.Level1=1;shield.BaseCost=20;shield.LevelCost=0;shield.Need1=1000;
        // 低魔力短盾须先消失，再验证赶路不会连续重施耗蓝。
        shield.PowerBase=shield.PowerBonus=0;
        actor.Magics=[new UserMagic(Spell.MagicShield){CastTime=-100000}];
        var weapon=new ItemInfo{Index=++world.ItemIndex,Name="隔离赶路法杖",Type=ItemType.Weapon,Durability=50000,
            Stats=new Stats{[Stat.HP]=500,[Stat.MinDC]=5,[Stat.MaxDC]=5,[Stat.Accuracy]=100}};
        world.ItemInfoList.Add(weapon);actor.Equipment[0]=world.CreateFreshItem(weapon);
        foreach(var (stat,count) in new[]{(Stat.HP,20),(Stat.MP,10)})
        {
            var medicine=new ItemInfo{Index=++world.ItemIndex,Name="隔离护盾储备"+stat,Type=ItemType.Potion,StackSize=100,Stats=new Stats{[stat]=30}};
            world.ItemInfoList.Add(medicine);var item=world.CreateFreshItem(medicine);item.Count=(ushort)count;actor.Inventory[stat==Stat.HP?0:1]=item;
        }
        var monster=new MonsterInfo{Index=++world.MonsterIndex,Name="隔离赶路危险守怪",AI=0,Image=Monster.ClZombie,Level=50,
            ViewRange=0,MoveSpeed=ushort.MaxValue,AttackSpeed=1000,Stats=new Stats{[Stat.HP]=10000,[Stat.MinDC]=50,[Stat.MaxDC]=50,[Stat.Accuracy]=100}};
        world.MonsterInfoList.Add(monster);
        if(!quiet)map.Respawns.Add(new(){RespawnIndex=++world.RespawnIndex,MonsterIndex=monster.Index,Location=new(24,14),Count=1,Delay=600});
        var quest=new QuestInfo{Index=1,Name="护盾通道拜访",FileName="NativeTravelShield",RequiredMinLevel=1,RequiredMaxLevel=255,RequiredClass=RequiredClass.None};
        world.QuestInfoList.Add(quest);world.QuestIndex=1;
        File.WriteAllText(Path.Combine(Settings.QuestPath,quest.FileName+".txt"),"[@DESCRIPTION]\n穿过通道，正常拜访交付。\n[@GOLDREWARD]\n333\n");
        quest.LoadInfo();actor.CurrentQuests=[new(1){StartDateTime=DateTime.UtcNow}];
        var npc=new NPCInfo{Index=++world.NPCIndex,Name="隔离护盾交付人",FileName="NativeTravelShield",MapIndex=map.Index,Location=new(52,12)};
        world.NPCInfoList.Add(npc);File.WriteAllText(Path.Combine(Settings.NPCPath,npc.FileName+".txt"),"[@MAIN]\n#SAY\n赶路护盾原任务交付。\n[Quests]\n-1\n");
        var header=new byte[52];BitConverter.GetBytes((short)64).CopyTo(header,0);BitConverter.GetBytes((short)32).CopyTo(header,2);
        using(var writer=new BinaryWriter(File.Create(Path.Combine(paths.Assets,"maps",map.FileName+".map"))))
        {
            writer.Write(header);
            // 守怪单独落格，避免原随机漫游堵住用来验证护盾的通道。
            for(var x=0;x<64;x++)for(var y=0;y<32;y++){writer.Write((ushort)(x==5&&y==5||x==24&&y==14||x>=2&&x<=61&&y==12?0:0x8000));writer.Write(new byte[10]);}
        }
        Settings.AutoLearnSkills=false;var setup=new InIReader(Path.Combine(paths.World,"Configs/Setup.ini"));setup.Write("AutoLearnSkills","Enabled",false);setup.Save();
        world.SaveDB();world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-travel-shield-fixture.json"),JsonSerializer.Serialize(new{quiet,map=map.Index,character=actor.Index,weapon=actor.Equipment[0].UniqueID}));
    }
}
