using System.Drawing;
using System.Text.Json;
using Mir2.Companions;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionDispatchScenario
{
    public static void Prepare(ProjectPaths paths)
    {
        if(!paths.Data.Contains("mir2-companions-dispatch-",StringComparison.Ordinal))throw new InvalidOperationException("只允许本调度测试的临时版本。");
        using var ownership=new FileStream(Path.Combine(paths.Data,".server.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var world=Envir.Main;if(!world.LoadDB())throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());world.LoadAccounts();
        foreach(var map in world.MapInfoList){map.Respawns.Clear();map.Movements.Clear();}
        world.NPCInfoList.Clear();world.QuestInfoList.Clear();
        var first=world.MapInfoList[0];var start=first.SafeZones[0].Location;first.SafeZones[0].Size=10;
        var store=new CompanionStore(storage,new(paths,storage),paths);
        if(store.Records.Count>0)throw new InvalidOperationException("调度测试须为空档案，不能覆盖已有角色。");
        var profiles=Enumerable.Range(0,20).Select(i=>
        {
            var preset=PersonalityPresets.All.Single(p=>p.Key==(i<10?"laid_back":"diligent"));
            return new CompanionProfile{Id=Guid.NewGuid(),Name=$"Queue{i:00}",Enabled=true,Scheduled=true,AcceptGroup=false,Chat=false,
                PresetKey=preset.Key,PresetRevision=preset.Revision,Traits=preset.Traits,
                Schedule=new(){TimeZone="UTC",Windows=[new(AllDay:true)],JitterMinutes=0,SessionMinutes=i<10?1:3,RestMinutes=1,DailyMinutes=60}};
        }).ToArray();
        store.SaveBatch(profiles,CompanionNames.All);
        for(var i=0;i<profiles.Length;i++)
        {
            var record=store.Get(profiles[i].Id);
            var account=new AccountInfo{Index=++world.NextAccountID,AccountID=record.Account,Password=record.Password,CreationIP="127.0.0.1",CreationDate=DateTime.UtcNow};
            var point=new Point(start.X+i%5-2,start.Y+i/5-2);
            var character=new CharacterInfo{Index=++world.NextCharacterID,Name=record.Profile.Name,AccountInfo=account,Class=MirClass.Warrior,Gender=MirGender.Male,
                Level=1,HP=100,MP=100,CurrentMapIndex=first.Index,CurrentLocation=point,BindMapIndex=first.Index,BindLocation=point,
                CreationIP="127.0.0.1",CreationDate=DateTime.UtcNow,Heroes=new HeroInfo[1]};
            account.Characters.Add(character);world.AccountList.Add(account);world.CharacterList.Add(character);
            var index=i;
            store.Mutate(record,r=>{r.Seed=index;r.Registered=true;r.AccountId=account.Index;r.CharacterId=character.Index;});
        }
        world.SaveDB();world.SaveAccounts();
        store.SaveSettings(new(store.Settings with{Enabled=true,MaxOnline=10,ReservePlayers=1,SmartScheduling=true}));
        var ini=new InIReader(Path.Combine(paths.World,"Configs","Setup.ini"));ini.Write("Network","MaxUser",100);ini.Write("Network","MaxIP",100);ini.Save();
        File.WriteAllText(Path.Combine(paths.Runtime,"companion-dispatch-fixture.json"),JsonSerializer.Serialize(new{profiles=profiles.Select(p=>p.Id).ToArray()}));
        Console.WriteLine("已准备20个普通测试账户/角色及10个在线名额，仅本临时版本；安全地图、无GM权限或发放资产。");
    }
}
