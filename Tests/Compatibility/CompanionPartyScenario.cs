using System.Drawing;
using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CompanionPartyScenario
{
    public static void Prepare(ProjectPaths paths,int[] ids)
    {
        if(!paths.Data.Contains("mir2-companions-party-",StringComparison.Ordinal)||ids.Length!=3)throw new InvalidOperationException("只允许三名隔离陪玩组队回归。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("隔离世界读取失败");world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        var characters=ids.Select(id=>world.CharacterList.Single(c=>c.Index==id)).ToArray();var local=world.MapInfoList[0];var remote=world.MapInfoList[1];
        foreach(var map in world.MapInfoList){map.FileName="NativeParty";map.Respawns.Clear();map.Movements.Clear();map.SafeZones.Clear();map.NoDropPlayer=true;map.NoTownTeleport=true;map.NoFight=false;map.NoGroup=false;}
        local.SafeZones.Add(new SafeZoneInfo{Info=local,Location=new(5,5),Size=0,StartPoint=true});
        local.Movements.Add(new MovementInfo{Source=new(20,10),MapIndex=remote.Index,Destination=new(5,10),ShowOnBigMap=false});
        remote.Movements.Add(new MovementInfo{Source=new(5,10),MapIndex=local.Index,Destination=new(20,10),ShowOnBigMap=false});
        foreach(var c in characters)
        {
            c.Level=30;c.HP=c.MP=10000;c.Experience=0;c.AccountInfo.Gold=10000;c.CurrentMapIndex=c.BindMapIndex=local.Index;c.CurrentLocation=c.BindLocation=new(10,10);
            c.Inventory=new UserItem[46];c.Equipment=new UserItem[14];c.QuestInventory=new UserItem[40];c.CurrentQuests.Clear();c.CompletedQuests.Clear();c.Magics.Clear();
            var blade=new ItemInfo{Index=++world.ItemIndex,Name="隔离组队武器"+c.Index,Type=ItemType.Weapon,Durability=60000,
                Shape=(short)(c.Class==MirClass.Archer?Globals.ClassWeaponCount*2:1),Stats=new Stats{[Stat.HP]=2000,[Stat.MP]=2000,[Stat.MinDC]=50,[Stat.MaxDC]=50,[Stat.MinMC]=50,[Stat.MaxMC]=50,[Stat.Accuracy]=100}};
            world.ItemInfoList.Add(blade);c.Equipment[0]=world.CreateFreshItem(blade);
            if(c.Class==MirClass.Archer)c.Magics.Add(new UserMagic(Spell.StraightShot));
            if(c.Class==MirClass.Taoist)c.Magics.Add(new UserMagic(Spell.Healing));
        }
        var mob=new MonsterInfo{Index=++world.MonsterIndex,Name="NativePartyTarget",Image=Monster.Hen,AI=0,Level=30,Experience=900,ViewRange=16,MoveSpeed=1000,AttackSpeed=1200,
            Stats=new Stats{[Stat.HP]=1500,[Stat.MinDC]=1,[Stat.MaxDC]=1}};world.MonsterInfoList.Add(mob);
        local.Respawns.Add(new RespawnInfo{MonsterIndex=mob.Index,Location=new(15,10),Count=1,Delay=600});
        world.QuestInfoList=[new QuestInfo{Index=1,Name="组队真实猎杀与跨图交付",FileName="NativePartyQuest",RequiredMinLevel=1,RequiredMaxLevel=255,RequiredClass=RequiredClass.Warrior}];world.QuestIndex=1;
        File.WriteAllText(Path.Combine(Settings.QuestPath,"NativePartyQuest.txt"),"[@KILLTASKS]\nNativePartyTarget 1\n[@GOLDREWARD]\n17\n");
        world.QuestInfoList[0].LoadInfo();characters[0].CurrentQuests.Add(new QuestProgressInfo(1){StartDateTime=DateTime.UtcNow});
        world.NPCInfoList=[new NPCInfo{Index=1,Name="队伍接引员",FileName="NativePartyStart",MapIndex=local.Index,Location=new(8,10)},
            new NPCInfo{Index=2,Name="队伍交付员",FileName="NativePartyFinish",MapIndex=remote.Index,Location=new(15,10)}];world.NPCIndex=2;
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativePartyStart.txt"),"[@MAIN]\n#SAY\n先组队协作猎杀。\n[Quests]\n1\n");
        File.WriteAllText(Path.Combine(Settings.NPCPath,"NativePartyFinish.txt"),"[@MAIN]\n#SAY\n从公开入口前来交付。\n[Quests]\n-1\n");
        // 仅准备既有会籍的隔离数据，建会/入会的原NPC资产流程由guilds.py单独覆盖。
        var guild=new GuildInfo(new PlayerObject{Info=characters[0]},"隔离组队行会"){GuildIndex=++world.NextGuildID,MemberCap=20};
        guild.Ranks[0].Members[0].Player=null;guild.Ranks[0].Members[0].Online=false;
        guild.Ranks.Add(new GuildRank{Index=1,Name="会员",Members=[new GuildMember{Name=characters[2].Name,Id=characters[2].Index}]});guild.Membercount=2;
        characters[0].GuildIndex=characters[2].GuildIndex=guild.GuildIndex;world.GuildList=[guild];
        using(var bytes=new MemoryStream()){using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))guild.Save(writer);storage.Write(Path.Combine(Settings.GuildPath,"0.mgd"),bytes.ToArray());}
        var header=new byte[52];BitConverter.GetBytes((short)32).CopyTo(header,0);BitConverter.GetBytes((short)32).CopyTo(header,2);
        using(var writer=new BinaryWriter(File.Create(Path.Combine(paths.Assets,"maps","NativeParty.map"))))
        {writer.Write(header);for(var x=0;x<32;x++)for(var y=0;y<32;y++){writer.Write((ushort)(x==0||y==0||x==31||y==31?0x8000:0));writer.Write(new byte[10]);}}
        world.SaveDB();world.SaveAccounts();File.WriteAllText(Path.Combine(paths.Runtime,"party-fixture.json"),JsonSerializer.Serialize(new{ids,origin=local.Index,remote=remote.Index,guild=guild.GuildIndex}));
    }
}
