using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private DateTimeOffset nextHealthCheck;

    private void Recover(Guid id,string reason,DateTimeOffset now,bool legacy=false)
    {
        var r=store.Get(id);
        if(!store.Settings.Enabled||!store.Settings.SmartRecovery||r.RecoveryDisabled||!r.Profile.Enabled&&!legacy)
        {Fail(id,reason);return;}
        if(r.RecoveryAt==null)store.Mutate(r,n=>
        {
            n.Error=reason[..Math.Min(reason.Length,500)];n.QueueSince=null;
            n.RecoveryAttempts=Math.Min(1000,n.RecoveryAttempts+1);n.RecoveryAt=now+CompanionSupervision.Delay(n);
            if(legacy){n.Profile=n.Profile with{Enabled=true};n.Desired="run";}
        });
        if(live.TryGetValue(id,out var current))
        {current.Recovering=true;current.StoppingAt??=now;current.State="托管恢复中";current.Reason="清理旧会话后退避重排："+reason;}
    }

    private void CheckHealth(DateTimeOffset now)
    {
        if(now<nextHealthCheck||!store.Settings.Enabled||!store.Settings.SmartRecovery||store.LoadError!=null||runtimeError!=null)return;
        nextHealthCheck=now.AddSeconds(30);
        var slots=Math.Max(0,Math.Min(Limits().EffectiveMaxOnline-live.Count,Math.Min(
            Settings.MaxUser-store.Settings.ReservePlayers-Envir.Main.Connections.Count,
            Settings.MaxIP-1-Envir.Main.Connections.Count(c=>c.IPAddress==LocalHost()))));
        foreach(var r in store.Records.Values.OrderBy(CompanionSupervision.Priority).ThenBy(r=>r.RecoveryAt??DateTimeOffset.MaxValue).ThenBy(r=>r.Profile.Id).ToArray())
        {
            var active=live.GetValueOrDefault(r.Profile.Id);
            if(active!=null)
            {
                if(active.Entered&&!active.Recovering&&active.StoppingAt==null&&now-active.Started>=TimeSpan.FromMinutes(5)&&
                    now-active.Heartbeat<TimeSpan.FromSeconds(30)&&r.RecoveryAttempts>0&&r.SessionDeaths==0)
                    store.Mutate(r,n=>n.RecoveryAttempts=0);
                continue;
            }
            if(r.RecoveryDisabled||r.RecoveryAt==null&&!CompanionSupervision.Legacy(r)&&!CompanionSupervision.Death(r))continue;
            var account=Envir.Main.GetAccount(r.Account);
            var character=Envir.Main.CharacterList.FirstOrDefault(c=>c.Index==r.CharacterId);
            if(account?.Connection!=null)continue;
            if(account?.AdminAccount==true||r.Registered&&account==null||r.CharacterId>0&&
                (character==null||character.AccountInfo!=account||character.Name!=r.Profile.Name||(int)character.Class!=r.Profile.Class||(int)character.Gender!=r.Profile.Gender))
            {Fail(r.Profile.Id,"智能托管核对账户/角色身份失败，保留数据并等待人工处理。");continue;}
            var death=CompanionSupervision.Death(r);
            if(!death&&r.SessionDeaths>=r.Profile.DeathLimit)
            {
                store.Mutate(r,n=>{n.Error=CompanionSupervision.DeathBudget;n.RecoveryAt=now.AddMinutes(2);});
                continue;
            }
            if(slots==0||!Eligibility(r with{Profile=r.Profile with{Enabled=true},Desired=r.Desired=="off"?"run":r.Desired,Error=""},now).Allowed)continue;
            if((death||character?.HP<=0)&&!SafeRevival(character))continue;
            if(r.RecoveryAt==null){Recover(r.Profile.Id,r.Error,now,legacy:true);continue;}
            if(now<r.RecoveryAt||!r.Profile.Enabled)continue;
            // 旧TCP已退出，新会话重新读取原服资产和任务，不保留或重发旧请求。
            store.Mutate(r,n=>
            {
                n.Error="";n.RecoveryAt=null;n.QueueSince=now;
                if(death)
                {
                    n.LastSessionReason=$"智能托管死亡恢复：上次死亡{n.SessionDeaths}/{n.Profile.DeathLimit}，核对安全复活点后重新排队";
                    n.SessionDeaths=0;n.SessionStarted=null;n.SessionUntil=null;
                }
            });
            slots--;
            MessageQueue.Instance.Enqueue($"陪玩智能托管：{r.Profile.Name}；{(death?$"死亡恢复，保留历史{r.SessionDeaths}/{r.Profile.DeathLimit}并重开预算":"技术故障恢复")}；重试{r.RecoveryAttempts}，旧连接已退出。");
        }
    }

    private static bool SafeRevival(Server.MirDatabase.CharacterInfo? character)
    {
        if(character==null||character.Deleted)return false;
        var map=Envir.Main.GetMap(character.BindMapIndex);
        var point=map?.GetBindLocation(character.BindLocation);
        return character.PKPoints<200&&point is {} location&&map!.Info.SafeZones.Any(zone=>Functions.InRange(zone.Location,location,zone.Size));
    }

    private async Task WorkerFault(Exception error,bool transportFailure)
    {
        var recovering=false;
        var reason=error is OperationCanceledException?"陪玩控制通道写入超时，等待清理执行组件后恢复。":error.Message;
        runtimeError=error is IOException or InvalidDataException or InvalidOperationException?error.Message:$"陪玩监管异常（{error.GetType().Name}），请核对存储和执行组件后重新启动。";
        try
        {
            if(runtime.Ready)await runtime.Invoke(()=>
            {
                recovering=transportFailure&&store.Settings.Enabled&&store.Settings.SmartRecovery&&store.LoadError==null;
                if(recovering)foreach(var id in live.Keys.ToArray())Recover(id,reason,DateTimeOffset.UtcNow);
                else if(transportFailure&&store.LoadError==null)foreach(var id in live.Keys.ToArray())Fail(id,reason);
                return true;
            });
        }
        catch(Exception failure)when(failure is not OutOfMemoryException){runtimeError="陪玩恢复状态保存失败，已阻断："+failure.Message;recovering=false;}
        try{await worker.Stop();}
        catch(Exception failure)when(failure is not OutOfMemoryException){runtimeError="陪玩进程清理失败，已阻断："+failure.Message;recovering=false;}
        handshaken=false;
        try
        {
            if(runtime.Ready)await runtime.Invoke(()=>
            {
                while(worker.Events.TryDequeue(out var e))
                {
                    if(e.Kind=="hello")continue;
                    if(e.Kind=="worker-failed")
                    {if(!e.Recoverable){runtimeError=e.Reason;recovering=false;}continue;}
                    Handle(e);
                }
                if(recovering){foreach(var current in live.Values)current.WorkerStopped=true;runtimeError=null;}
                else live.Clear();
                return true;
            });
            else live.Clear();
        }
        catch(Exception failure)when(failure is not OutOfMemoryException){runtimeError="陪玩故障清理未完成，已阻断："+failure.Message;live.Clear();}
        nextConnect=DateTimeOffset.UtcNow.AddSeconds(recovering?30:1);
    }
}
