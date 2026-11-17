using System.Text.Json;
using Mir2.Companions;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionClassScenario
{
    public static void Prepare(ProjectPaths paths,int[] characters)
    {
        if(!paths.Data.Contains("mir2-companions-",StringComparison.Ordinal))throw new InvalidOperationException("只允许修改隔离陪玩版本。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        if(characters.Length==0)
        {
            var store=new CompanionStore(storage,new(paths,storage),paths);
            if(store.Records.Count>0)throw new InvalidOperationException("五职业测试要求空陪玩档案。");
            var profiles=Enum.GetValues<MirClass>().Select(job=>new CompanionProfile{Id=Guid.NewGuid(),Name=$"Class{(int)job}",Class=(int)job,Enabled=true,AcceptGroup=false,Chat=false,
                Schedule=new(){TimeZone="UTC",Windows=[new(AllDay:true)],JitterMinutes=0,SessionMinutes=30,RestMinutes=0,DailyMinutes=60}}).ToArray();
            store.SaveBatch(profiles,CompanionNames.All);
            foreach(var profile in profiles)
            {
                var record=store.Get(profile.Id);var start=world.MapInfoList[0].SafeZones[0].Location;
                var account=new AccountInfo{Index=++world.NextAccountID,AccountID=record.Account,Password=record.Password,CreationIP="127.0.0.1",CreationDate=DateTime.UtcNow};
                var actor=new CharacterInfo{Index=++world.NextCharacterID,Name=profile.Name,Class=(MirClass)profile.Class,Gender=MirGender.Male,AccountInfo=account,
                    BindLocation=start,BindMapIndex=1,CreationIP="127.0.0.1",CreationDate=DateTime.UtcNow,Heroes=new HeroInfo[1]};
                account.Characters.Add(actor);world.AccountList.Add(account);world.CharacterList.Add(actor);
                store.Mutate(record,r=>{r.Registered=true;r.AccountId=account.Index;r.CharacterId=actor.Index;});
            }
            characters=store.Records.Values.Select(r=>r.CharacterId).ToArray();
            store.SaveSettings(new(store.Settings with{Enabled=true,MaxOnline=5}));
            var network=new InIReader(Path.Combine(paths.World,"Configs/Setup.ini"));network.Write("Network","MaxIP",32);network.Save();
        }
        foreach(var map in world.MapInfoList)map.Respawns.Clear();
        world.NPCInfoList.Clear();world.QuestInfoList.Clear();
        var expected=new List<object>();
        foreach(var id in characters)
        {
            var actor=world.CharacterList.Single(c=>c.Index==id);var job=actor.Class;
            Spell[] skills=job switch
            {
                MirClass.Warrior=>[Spell.FlamingSword,Spell.TwinDrakeBlade,Spell.ProtectionField,Spell.Rage],
                MirClass.Wizard=>[Spell.MagicShield,Spell.MagicBooster,Spell.FireBall],
                MirClass.Taoist=>[Spell.SummonSkeleton,Spell.Poisoning,Spell.SoulShield,Spell.BlessedArmour,Spell.SoulFireBall,Spell.PoisonCloud],
                MirClass.Assassin=>[Spell.Haste,Spell.LightBody,Spell.DoubleSlash],
                _=>[Spell.ElementalBarrier,Spell.Concentration,Spell.Meditation,Spell.DoubleShot]
            };
            actor.Level=50;actor.HP=actor.MP=10000;actor.Experience=0;actor.AccountInfo.Gold=10000;
            actor.CurrentQuests.Clear();actor.CompletedQuests.Clear();actor.Inventory=new UserItem[46];actor.Equipment=new UserItem[14];
            actor.Magics=skills.Select(spell=>new UserMagic(spell){CastTime=-100000}).ToList();
            foreach(var spell in skills)
            {
                var info=world.MagicInfoList.Single(m=>m.Spell==spell);info.Level1=1;info.BaseCost=10;info.LevelCost=0;info.Need1=1000;
            }
            var weapon=new ItemInfo{Index=++world.ItemIndex,Name=$"隔离{job.ToLocalizedString()}武器",Type=ItemType.Weapon,
                Shape=(short)(job==MirClass.Archer?2*Globals.ClassWeaponCount:1),Durability=60000,
                Stats=new Stats{[Stat.HP]=10000,[Stat.MP]=10000,[Stat.MinDC]=100,[Stat.MaxDC]=100,[Stat.MinMC]=100,[Stat.MaxMC]=100,[Stat.MinSC]=100,[Stat.MaxSC]=100,[Stat.Accuracy]=100}};
            world.ItemInfoList.Add(weapon);actor.Equipment[0]=world.CreateFreshItem(weapon);
            if(job==MirClass.Taoist)
            {
                for(short shape=0;shape<=2;shape++)
                {
                    var material=new ItemInfo{Index=++world.ItemIndex,Name=$"隔离施法耗材{shape}",Type=ItemType.Amulet,Shape=shape,StackSize=1000};
                    world.ItemInfoList.Add(material);actor.Inventory[shape]=world.CreateFreshItem(material);actor.Inventory[shape].Count=100;
                }
                var bracelet=new ItemInfo{Index=++world.ItemIndex,Name="隔离保留右腕",Type=ItemType.Bracelet,Durability=60000,Stats=new Stats{[Stat.MaxAC]=5}};
                world.ItemInfoList.Add(bracelet);actor.Equipment[(int)EquipmentSlot.BraceletR]=world.CreateFreshItem(bracelet);
            }
            var field=new MapInfo{Index=++world.MapIndex,FileName="0",Title=$"隔离{job.ToLocalizedString()}战场",NoDropPlayer=true,NoTownTeleport=true,Light=LightSetting.Day};
            world.MapInfoList.Add(field);actor.CurrentMapIndex=actor.BindMapIndex=field.Index;actor.CurrentLocation=actor.BindLocation;
            var target=new MonsterInfo{Index=++world.MonsterIndex,Name=$"隔离{job.ToLocalizedString()}靶怪",Level=50,AI=0,Experience=100,
                ViewRange=0,MoveSpeed=ushort.MaxValue,AttackSpeed=ushort.MaxValue,Stats=new Stats{[Stat.HP]=2000}};
            world.MonsterInfoList.Add(target);
            field.Respawns.Add(new RespawnInfo{MonsterIndex=target.Index,Location=Functions.PointMove(actor.BindLocation,MirDirection.Down,2),Count=1,Delay=600});
            if(job==MirClass.Taoist)
                // 三怪须从任一首选目标都在毒云范围内，避免先选边怪后永远只剩两只。
                foreach(var offset in new[]{-1,1})field.Respawns.Add(new RespawnInfo{MonsterIndex=target.Index,Location=new(actor.BindLocation.X+offset,actor.BindLocation.Y+2),Count=1,Delay=600});
            expected.Add(new{id,job=(int)job,skills=skills.Where(s=>s is not Spell.Meditation).Select(s=>(int)s).ToArray(),weapon=actor.Equipment[0].UniqueID});
        }
        Settings.AutoLearnSkills=false;var setup=new InIReader(Path.Combine(paths.World,"Configs/Setup.ini"));setup.Write("AutoLearnSkills","Enabled",false);setup.Save();
        world.SaveDB();world.SaveAccounts();File.WriteAllText(Path.Combine(paths.Runtime,"companion-class-fixture.json"),JsonSerializer.Serialize(expected));
    }
}
