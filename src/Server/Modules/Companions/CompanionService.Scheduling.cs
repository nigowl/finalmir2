using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private (bool Allowed,string Reason,DateTimeOffset End) Eligibility(CompanionRecord r,DateTimeOffset now)
    {
        if(store.LoadError!=null)return(false,store.LoadError,now);
        if(runtimeError!=null)return(false,runtimeError,now);
        if(!store.Settings.Enabled)return(false,"本版本陪玩总开关关闭或目录复制后尚未启用",now);
        if(!r.Profile.Enabled)return(false,"角色已停用",now);
        if(r.CharacterId==0&&!CompanionValidation.CanCreateClass(r.Profile.Class))return(false,"当前版本未开放此职业创建，请在资料中选择已开放职业",now);
        if(r.Error.Length>0)return(false,r.Error,now);
        if(r.SuppressedUntil>now)return(false,"人工下线抑制至本次窗口结束",now);
        if(r.RestUntil>now)return(false,"会话间休息中",now);
        if(store.TodaySeconds(r,now)>=r.Profile.Schedule.DailyMinutes*60||store.RollingSeconds(r,now)>=r.Profile.Schedule.DailyMinutes*60)
            return(false,"每日或过去 24 小时在线额度已用完",now);
        if(!r.Profile.Scheduled)return(r.Desired is "run"or"pause",r.Desired=="off"?"仅手动启动":"等待名额",now.AddMinutes(r.Profile.Schedule.SessionMinutes));
        var window=CompanionScheduling.Preview(r.Profile.Schedule,r.Seed,now).Where(w=>w.Attending&&w.Start<=now&&w.End>now).OrderByDescending(w=>w.End).FirstOrDefault();
        return(window!=null,window==null?"未到在线窗口或本次不出勤":"等待名额",window?.End??now);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        runtime.BeforeStop=Shutdown;
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while(await timer.WaitForNextTickAsync(stoppingToken))
            {
                if(!runtime.Ready||runtime.Stopping)continue;
                await transport.WaitAsync(stoppingToken);
                var transportFailure=false;
                try
                {
                    while(worker.Events.TryDequeue(out var e)){transportFailure=e.Kind=="worker-failed"&&e.Recoverable;await runtime.Invoke(()=>{Handle(e);return true;},stoppingToken);}
                    if(!worker.Running&&live.Values.Any(l=>!l.WorkerStopped)){transportFailure=true;throw new IOException(CompanionSupervision.WorkerExit);}
                    var commands=await runtime.Invoke(Plan,stoppingToken);
                    if(commands.Any(c=>c.Kind=="recover-worker")){transportFailure=true;throw new IOException("陪玩会话停止后仍无回执，智能托管重建执行组件。");}
                    while(llmResults.TryDequeue(out var answer))commands.Add(answer);
                    if(commands.Count>0)
                    {
                        try{await worker.Start();}
                        catch(IOException){transportFailure=true;throw;}
                        if(!handshaken){nextConnect=DateTimeOffset.UtcNow.AddSeconds(1);continue;}
                        try{foreach(var command in commands)await worker.Send(command);}
                        catch(Exception e)when(e is IOException or OperationCanceledException){transportFailure=true;throw;}
                    }
                }
                catch(Exception e) when(!stoppingToken.IsCancellationRequested && e is not OutOfMemoryException)
                {
                    await WorkerFault(e,transportFailure);
                }
                finally{transport.Release();}
            }
        }
        catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}
        finally{await Shutdown();runtime.BeforeStop=null;}
    }
    private List<WorkerCommand> Plan()
    {
        var commands=new List<WorkerCommand>();var now=DateTimeOffset.UtcNow;
        CheckHealth(now);
        PlanArrivals(now);
        var capacity=Limits().EffectiveMaxOnline;
        var excess=live.Where(p=>p.Value.StoppingAt==null).OrderByDescending(p=>store.Get(p.Key).SessionUntil)
            .Skip(capacity).Select(p=>p.Key).ToHashSet();
        if(!worker.Running&&live.Values.Any(l=>!l.WorkerStopped))return [new("recover-worker")];
        if(store.Settings.SmartRecovery&&worker.Running&&!handshaken&&now-worker.StartedAt>TimeSpan.FromSeconds(30))return [new("recover-worker")];
        foreach(var id in live.Keys.ToArray())
        {
            var r=store.Get(id);var l=live[id];var eligible=Eligibility(r,now);
            if(l.StoppingAt!=null)
            {
                if(l.WorkerStopped&&Envir.Main.GetAccount(r.Account)?.Connection==null){live.Remove(id);continue;}
                if(l.Recovering)
                {
                    if(!l.WorkerStopped&&now-l.StoppingAt>TimeSpan.FromSeconds(15))return [new("recover-worker")];
                    if(!l.WorkerStopped)commands.Add(new("stop",Id:id));
                    continue;
                }
                if(now-l.StoppingAt>TimeSpan.FromSeconds(30))
                {commands.Add(new("stop",Id:id));Recover(id,CompanionSupervision.RetireTimeout,now);}
                // 等退出回执时也续监管租约，重复控制不能重发执行组件的未决TCP请求。
                else if(!l.WorkerStopped&&r.CharacterId>0)commands.Add(new("retire",Id:id));
                continue;
            }
            if(now-l.Heartbeat>TimeSpan.FromSeconds(30))
            {commands.Add(new("stop",Id:id));Recover(id,CompanionSupervision.Heartbeat,now);continue;}
            var reason=excess.Contains(id)?"在线名额缩减，正常让出名额":!eligible.Allowed?eligible.Reason:
                now>=r.SessionUntil?"本次游玩结束，按人格休息后重新排队":"";
            var stop=reason.Length>0;
            if(stop)
            {
                l.StoppingAt=now;l.State="下线中";l.Reason=reason;
                commands.Add(new(r.CharacterId>0?"retire":"stop",Id:id));
                store.EndSession(r,now,reason);continue;
            }
            commands.Add(new(r.Desired=="pause"?"pause":"resume",Id:id));
            commands.Add(new("update",Id:id,Profile:store.Public(r)));
            if(now>=l.ReservedUntil)
            {
                var seconds=Math.Min(10,r.Profile.Schedule.DailyMinutes*60-Math.Max(store.TodaySeconds(r,now),store.RollingSeconds(r,now)));
                store.ReserveOnline(r,now,seconds);l.ReservedUntil=now.AddSeconds(seconds);
            }
            if(now>=l.NextKnowledge)
            {
                l.NextKnowledge=now.AddSeconds(3);
                foreach(var key in l.BlockedGoals.Keys.Where(key=>l.BlockedGoals[key]<=now).ToArray())l.BlockedGoals.Remove(key);
                var knowledge=CompanionKnowledgeReader.Read(store.Get(id),l.BlockedGoals.Keys.ToHashSet(),store.Records.Values.ToArray(),store.Settings.AllowPk);
                l.Goal=knowledge?.Goal;
                l.Guild=knowledge?.Guild;
                if(knowledge!=null)knowledge=knowledge with{AllowPk=store.Settings.AllowPk,Revision=knowledge.Revision+"/"+store.Settings.AllowPk+"/"+knowledge.InSafeZone};
                if(knowledge!=null&&knowledge.Revision!=l.Knowledge){l.Knowledge=knowledge.Revision;commands.Add(new("knowledge",Id:id,Knowledge:knowledge));}
            }
        }
        foreach(var r in store.Records.Values.Where(r=>!live.ContainsKey(r.Profile.Id)).ToArray())
        {
            if(r.SessionUntil<=now)
            {store.EndSession(r,now,"上次会话已到期，按原计划休息后排队");continue;}
            var allowed=Eligibility(r,now).Allowed;
            if(allowed&&r.QueueSince==null)store.Mutate(r,n=>n.QueueSince=now);
            else if(!allowed&&r.QueueSince!=null)store.Mutate(r,n=>n.QueueSince=null);
        }
        if(now<nextConnect||store.LoadError!=null||runtime.Stopping)return commands;
        var candidates=CompanionStore.Waiting(store.Records.Values.Where(r=>!live.ContainsKey(r.Profile.Id)&&Eligibility(r,now).Allowed)).ToArray();
        queueReason="等待名额";
        if(candidates.Length==0)return commands;
        if(live.Count>=store.Settings.MaxOnline){queueReason="达到本版本陪玩在线上限";return commands;}
        var world=Envir.Main;
        var host=LocalHost();
        if(world.Connections.Count>=Settings.MaxUser-store.Settings.ReservePlayers){queueReason=$"总连接 {world.Connections.Count}/{Settings.MaxUser}，预留真人 {store.Settings.ReservePlayers} 个名额；可在系统设置调整";return commands;}
        var sameIp=world.Connections.Count(c=>c.IPAddress==host);
        if(sameIp>=Math.Max(0,Settings.MaxIP-1)){queueReason=$"同 IP 连接 {sameIp}/{Settings.MaxIP}，另预留 1 个本机名额；请在系统设置提高同 IP 上限，保存即生效";return commands;}
        if(Envir.IPBlocks.TryGetValue(host,out var banned)&&banned>world.Now){queueReason="本机 IP 已被临时封禁";return commands;}
        if(!Settings.AllowLogin){queueReason="登录已关闭";return commands;}
        var log=Envir.ConnectionLogs.GetValueOrDefault(host);log?.PruneAccounts(world.Time);log?.PruneCharacters(world.Time);
        CompanionRecord? pending=null;
        foreach(var candidate in candidates)
        {
            if(!candidate.Registered&&(!Settings.AllowNewAccount||log?.AccountsMade.Count>2))
            {queueReason="等待开放注册或同 IP 注册限频解除";continue;}
            if(candidate.CharacterId==0&&log?.CharactersMade.Count>4)
            {queueReason="等待同 IP 建角限频解除（每小时最多 5 次尝试）";continue;}
            if(world.GetAccount(candidate.Account)?.Connection!=null)
            {Fail(candidate.Profile.Id,"账户已被其他连接占用，请先人工核对。");continue;}
            pending=candidate;break;
        }
        // 新角色受限时让已有角色继续补位，不能由队首注册限频堵住整个队列。
        if(pending==null)return commands;
        if(!worker.Running||!handshaken)return [new("prepare")];
        var resource="http://"+(host.Contains(':')?"["+host+"]":host)+":"+(Environment.GetEnvironmentVariable("MIR2_RESOURCE_PORT")??"5080");
        var cache=Path.Combine(paths.Runtime,"cache","companions");
        store.BeginSession(pending,now,Eligibility(pending,now).End);pending=store.Get(pending.Profile.Id);
        live[pending.Profile.Id]=new();store.ReserveOnline(pending,now,10);live[pending.Profile.Id].ReservedUntil=now.AddSeconds(10);
        commands.Add(new("start",new(store.Public(pending),pending.Account,pending.Password,pending.Registered,host,runtime.GamePort,
            paths.VersionId,live[pending.Profile.Id].Session,CompanionProtocol.Fingerprint,resource,runtime.AssetVersion,cache,pending.Seed,pending.SessionDeaths)));
        if(pending.Desired=="pause")commands.Add(new("pause",Id:pending.Profile.Id));
        nextConnect=now.AddSeconds(Math.Max(6,Settings.IPBlockSeconds+1));
        return commands;
    }
    public async Task Shutdown()
    {
        await transport.WaitAsync();
        try
        {
            await worker.Stop();handshaken=false;
            if(runtime.Ready)await runtime.Invoke(()=>{live.Clear();return true;});else live.Clear();
            runId=Guid.NewGuid().ToString("N");
            llmPending.Clear();while(llmResults.TryDequeue(out _)){}
        }
        finally{transport.Release();}
    }
}
