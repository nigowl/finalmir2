using System.Reflection;
using System.Runtime.CompilerServices;
using Mir2.Companions;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionRecoveryChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        check(new CompanionProfile().DeathLimit==10,"新陪玩默认死亡预算不是10次");
        var death=new CompanionRecord{Error=CompanionSupervision.DeathBudget,SessionDeaths=10};
        check(CompanionSupervision.Death(death),"确定的死亡预算没有进入托管候选");
        check(!CompanionSupervision.Death(death with{RecoveryDisabled=true}),"人工停用可被死亡恢复绕过");
        check(!CompanionSupervision.Death(death with{Error=death.Error+"未知回执"}),"死亡错误采用模糊匹配清除未知保护");
        check(CompanionSupervision.Priority(death)>CompanionSupervision.Priority(new(){Error=CompanionSupervision.Heartbeat}),"死亡恢复抢占技术恢复");
        check(Enumerable.Range(1,6).Select(i=>CompanionSupervision.Delay(death with{RecoveryAttempts=i}).TotalSeconds)
            .SequenceEqual(new double[]{120,240,480,900,900,900}),"死亡重试没有退避至15分钟");
        var world=Envir.Main;var accounts=world.AccountList;var characters=world.CharacterList;var maps=world.MapList;
        var cwd=Directory.GetCurrentDirectory();var previous=DatabaseStorage.Current;var maxIp=Settings.MaxIP;var maxUser=Settings.MaxUser;
        try
        {
            var paths=new ProjectPaths(Path.Combine(root,"death-recovery"));Directory.CreateDirectory(paths.World);Directory.SetCurrentDirectory(paths.World);
            using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
            var store=new CompanionStore(storage,new(paths,storage),paths);Settings.MaxIP=20;Settings.MaxUser=100;
            store.SaveSettings(new(store.Settings with{Enabled=true,SmartRecovery=true,MaxOnline=1}));
            var account=new AccountInfo{Index=81,AccountID="recovery-dead",Gold=17};
            var character=new CharacterInfo{Index=82,Name="死亡恢复",CreationIP="",AccountInfo=account,HP=0,BindMapIndex=701,BindLocation=new(1,1)};
            character.Heroes=new HeroInfo[character.MaximumHeroCount];
            var map=new Map(new MapInfo{Index=701,SafeZones=[new(){Location=new(1,1),Size=0}]}){Width=3,Height=3,Cells=new Cell[3,3]};
            for(var x=0;x<3;x++)for(var y=0;y<3;y++)map.Cells[x,y]=new(){Attribute=CellAttribute.Walk};
            world.AccountList=[account];world.CharacterList=[character];world.MapList=[map];account.Characters.Add(character);
            var tech=Guid.NewGuid();var dead=Guid.NewGuid();
            store.SaveProfile(new(){Id=tech,Name="技术恢复",Enabled=false},"unregistered","password");
            store.SaveProfile(new(){Id=dead,Name=character.Name,Enabled=false},account.AccountID,"password");
            store.Mutate(store.Get(tech),r=>{r.Error=CompanionSupervision.Heartbeat;r.RecoveryDisabled=false;});
            store.Mutate(store.Get(dead),r=>{r.Error=CompanionSupervision.DeathBudget;r.SessionDeaths=10;r.RecoveryDisabled=false;r.CharacterId=82;r.Registered=true;});
            using var service=new CompanionService((GameRuntime)RuntimeHelpers.GetUninitializedObject(typeof(GameRuntime)),store,paths);
            var method=typeof(CompanionService).GetMethod("CheckHealth",BindingFlags.NonPublic|BindingFlags.Instance)!;
            void Tick(DateTimeOffset now)=>method.Invoke(service,[now]);
            var now=DateTimeOffset.UtcNow;Tick(now);
            check(store.Get(dead).RecoveryAt==now.AddMinutes(2)&&store.Get(dead).SessionDeaths==10,"死亡退避前提前清空预算或未保存计时");
            Tick(now.AddMinutes(3));
            check(store.Get(tech).Error==""&&store.Get(dead).Error==CompanionSupervision.DeathBudget,"单个空位没有先处理技术故障");
            // 已就绪的正常候选会在真实调度循环补位；本测试先人工停用它，再检查死亡候补。
            store.SaveProfile(store.Public(store.Get(tech)) with{Enabled=false});
            map.Info.SafeZones.Clear();Tick(now.AddMinutes(4));
            check(store.Get(dead).SessionDeaths==10&&store.Get(dead).RecoveryAt!=null,"无原安全复活点仍清除死亡保护");
            map.Info.SafeZones.Add(new(){Location=new(1,1),Size=0});Tick(now.AddMinutes(5));
            var recovered=store.Get(dead);
            check(recovered.Profile.Enabled&&recovered.Desired=="run"&&recovered.Error==""&&recovered.SessionDeaths==0&&recovered.RecoveryAt==null,"安全死亡候补没有重开预算并补位");
            check(character.HP==0&&account.Gold==17&&recovered.LastSessionReason.Contains("10/10"),"托管直接复活/造资产或未保留上次死亡记录");
            var reopened=new CompanionStore(storage,new(paths,storage),paths);
            check(reopened.Get(dead).RecoveryAttempts==1&&reopened.Get(dead).LastSessionReason==recovered.LastSessionReason,"重启丢失恢复次数或死亡历史");
        }
        finally{world.AccountList=accounts;world.CharacterList=characters;world.MapList=maps;Settings.MaxIP=maxIp;Settings.MaxUser=maxUser;DatabaseStorage.Current=previous;Directory.SetCurrentDirectory(cwd);}
    }
}
