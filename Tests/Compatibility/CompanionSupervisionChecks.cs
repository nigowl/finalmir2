using Crystal.Native;
using Mir2.Companion;
using Mir2.Companions;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using System.Reflection;
using System.Runtime.CompilerServices;
using S = ServerPackets;

internal static class CompanionSupervisionChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        check(!new CompanionSettings().SmartRecovery,"智能托管默认开启导致已有角色被自动恢复");
        foreach(var reason in new[]{CompanionSupervision.Heartbeat,CompanionSupervision.WorkerExit,CompanionSupervision.RetireTimeout,CompanionSupervision.SessionExit})
        {
            var record=new CompanionRecord{Error=reason};
            check(CompanionSupervision.Legacy(record),"没有识别确定的旧技术故障");
            check(!CompanionSupervision.Legacy(record with{RecoveryDisabled=true}),"人工停用被旧故障恢复绕过");
            check(!CompanionSupervision.Legacy(record with{SuppressedUntil=DateTimeOffset.UtcNow.AddDays(-1)}),"历史人工下线被恢复");
            check(!CompanionSupervision.Legacy(record with{Desired="pause"}),"旧故障覆盖人工暂停意愿");
        }
        foreach(var reason in new[]{"任务操作回执未知（物品/任务编号 64），已停止；重新登录核对后才能继续，不会自动重发。","达到本次会话死亡预算，请调整目标或配置后重新启动。","账户已被其他连接占用，请先人工核对。","陪玩程序与服务端 Shared 协议指纹不匹配。","",CompanionSupervision.Heartbeat+"其他信息"})
            check(!CompanionSupervision.Legacy(new(){Error=reason}),"非白名单错误被旧故障恢复清除");
        check(Enumerable.Range(1,8).Select(i=>CompanionSupervision.Delay(i).TotalSeconds).SequenceEqual(new double[]{30,60,120,240,480,900,900,900}),"恢复退避没有递增并限制到15分钟");
        var state=new GameState();
        check(!CompanionSafety.CanReconnect(state,new IOException(),0,3,false,false),"未确认登录的创建/注册故障会自动重发");
        state.Apply(new S.UserInformation{ObjectID=1,HP=50,Inventory=[],Equipment=new UserItem[14],QuestInventory=[],Magics=[]});
        check(CompanionSafety.CanReconnect(state,new IOException(),2,3,false,false),"无未决请求的普通断线不能恢复");
        check(CompanionSafety.CanReconnect(state,new TimeoutException(),2,3,false,false),"无未决请求的控制租约/移动超时不能恢复");
        check(!CompanionSafety.CanReconnect(state,new IOException(),2,3,true,false),"资产/药卷/退出未知请求可自动恢复");
        check(!CompanionSafety.CanReconnect(state,new IOException(),2,3,false,true),"原服踢下线或账户占用可自动重连");
        check(!CompanionSafety.CanReconnect(state,new IOException(),3,3,false,false),"重连可以绕过死亡预算");
        check(!CompanionSafety.CanReconnect(state,new InvalidDataException(),0,3,false,false),"协议/数据错误被当作普通断线");
        state.Me!.Dead=true;check(!CompanionSafety.CanReconnect(state,new IOException(),0,3,false,false),"死亡状态会自动重连重置预算");state.Me.Dead=false;
        state.StorageActive=true;check(!CompanionSafety.CanReconnect(state,new IOException(),0,3,false,false),"仓库未知状态会重连");state.StorageActive=false;
        state.Apply(new S.TradeAccept{Name="队友"});check(!CompanionSafety.CanReconnect(state,new IOException(),0,3,false,false),"交易未知状态会重连");
        var paths=new ProjectPaths(Path.Combine(root,"supervision"));Directory.CreateDirectory(paths.World);
        var previous=DatabaseStorage.Current;var cwd=Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(paths.World);using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
            var store=new CompanionStore(storage,new(paths,storage),paths);
            store.SaveSettings(new(store.Settings with{Enabled=true,SmartRecovery=true}));
            var id=Guid.NewGuid();store.SaveProfile(new(){Id=id,Name="健康托管",Enabled=true,DeathLimit=3},"account","password");
            var now=DateTimeOffset.UtcNow;
            store.Mutate(store.Get(id),r=>{r.SessionUntil=now.AddMinutes(20);r.SessionStarted=now.AddMinutes(-2);r.SessionDeaths=2;r.RecoveryAt=now.AddMinutes(1);r.RecoveryAttempts=3;r.Error=CompanionSupervision.Heartbeat;r.Desired="pause";});
            var reopened=new CompanionStore(storage,new(paths,storage),paths);var saved=reopened.Get(id);
            check(reopened.Settings.SmartRecovery&&saved.RecoveryAt==store.Get(id).RecoveryAt&&saved.RecoveryAttempts==3&&saved.Desired=="pause"&&saved.SessionDeaths==2,"重启丢失托管设置/退避/暂停意愿/死亡次数");
            reopened.BeginSession(saved,now,now.AddHours(1));
            check(reopened.Get(id).SessionDeaths==2&&reopened.Get(id).SessionUntil==saved.SessionUntil,"恢复重新抽取会话或清空本次死亡次数");
            reopened.EndSession(reopened.Get(id),now,"正常到期");reopened.BeginSession(reopened.Get(id),now.AddHours(1),now.AddHours(2));
            check(reopened.Get(id).SessionDeaths==0,"正常新会话未开始新的死亡预算");
            var profile=reopened.Public(reopened.Get(id));reopened.SaveProfile(profile with{Enabled=false});
            check(reopened.Get(id).RecoveryAt==null&&reopened.Get(id).RecoveryDisabled,"人工停用没有撤销自动恢复");
            var world=Envir.Main;var accounts=world.AccountList;var characters=world.CharacterList;
            try
            {
                var account=new AccountInfo{Index=71,AccountID="account"};
                var character=new CharacterInfo{Index=72,Name="健康托管",CreationIP="",HP=50,AccountInfo=account};character.Heroes=new HeroInfo[character.MaximumHeroCount];account.Characters.Add(character);
                world.AccountList=[account];world.CharacterList=[character];
                reopened.SaveProfile(reopened.Public(reopened.Get(id)) with{Enabled=true});
                reopened.Mutate(reopened.Get(id),n=>{n.Error="";n.SessionDeaths=2;});
                using var service=new CompanionService((GameRuntime)RuntimeHelpers.GetUninitializedObject(typeof(GameRuntime)),reopened,paths);
                var type=typeof(CompanionService).GetNestedType("LiveCompanion",BindingFlags.NonPublic)!;
                var live=Activator.CreateInstance(type,true)!;type.GetField("Session")!.SetValue(live,"late-enter");
                var entries=(System.Collections.IDictionary)typeof(CompanionService).GetField("live",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(service)!;entries[id]=live;
                typeof(CompanionService).GetMethod("Handle",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[new WorkerEvent("entered",id,Session:"late-enter")]);
                check(reopened.Get(id).CharacterId==72&&reopened.Get(id).RecoveryAt!=null&&reopened.Get(id).Profile.Enabled,"迟到进入回执被当作全局协议错误，或未保存已经存在的角色绑定");
                check(service.Overview().Online==0,"只有调度会话、没有原服角色在线时仍计为在线");
                character.Player=(Server.MirObjects.PlayerObject)RuntimeHelpers.GetUninitializedObject(typeof(Server.MirObjects.PlayerObject));
                check(service.Overview().Online==1,"原服实际在线的绑定陪玩没有计入总览");
                entries.Clear();check(service.Overview().Online==1,"实际在线人数依赖工作进程会话而漏计原服角色");
                character.Player=null;entries[id]=live;
                reopened.Mutate(reopened.Get(id),n=>{n.Error="";n.RecoveryAt=null;});
                var ended=Activator.CreateInstance(type,true)!;type.GetField("Session")!.SetValue(ended,"unexpected-stop");entries[id]=ended;
                typeof(CompanionService).GetMethod("Handle",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[new WorkerEvent("stopped",id,Session:"unexpected-stop")]);
                check(reopened.Get(id).RecoveryAt!=null&&reopened.Get(id).Profile.Enabled&&(bool)type.GetField("WorkerStopped")!.GetValue(ended)!,"非人工的结束事件未保留启用意愿或未确认旧执行会话已经结束");
            }
            finally{world.AccountList=accounts;world.CharacterList=characters;}
        }
        finally{DatabaseStorage.Current=previous;Directory.SetCurrentDirectory(cwd);}
    }

    public static void Prepare(ProjectPaths paths)
    {
        using var storage=new SqliteDatabaseStorage(paths.Database);DatabaseStorage.Current=storage;
        var store=new CompanionStore(storage,new(paths,storage),paths);var now=DateTimeOffset.UtcNow;
        foreach(var r in store.Records.Values.ToArray())store.Mutate(r,n=>
        {
            n.RecoveryDisabled=n.Profile.Name=="保留人工";
            n.Error=n.Profile.Name=="保留资产"?"任务操作回执未知（物品/任务编号 64），已停止；重新登录核对后才能继续，不会自动重发。":CompanionSupervision.Heartbeat;
            n.Desired="off";
            if(n.Profile.Name=="托管主角")
            {n.Error="";n.Profile=n.Profile with{Enabled=true};n.Desired="pause";n.SessionStarted=now;n.SessionUntil=now.AddMinutes(30);n.SessionDeaths=2;}
        });
    }
}
