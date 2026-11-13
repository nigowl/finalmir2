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
                try
                {
                    while(worker.Events.TryDequeue(out var e))await runtime.Invoke(()=>{Handle(e);return true;},stoppingToken);
                    var commands=await runtime.Invoke(Plan,stoppingToken);
                    while(llmResults.TryDequeue(out var answer))commands.Add(answer);
                    if(commands.Count>0)
                    {
                        await worker.Start();
                        if(!handshaken){nextConnect=DateTimeOffset.UtcNow.AddSeconds(1);continue;}
                        foreach(var command in commands)await worker.Send(command);
                    }
                }
                catch(Exception e) when(!stoppingToken.IsCancellationRequested && e is not OutOfMemoryException)
                {
                    runtimeError=e is IOException or InvalidDataException or InvalidOperationException?e.Message:$"陪玩监管异常（{e.GetType().Name}），已停止陪玩；请检查存储和执行组件后重新启动。";
                    try{await worker.Stop();}
                    catch(Exception stopError)when(stopError is IOException or InvalidOperationException){runtimeError+="；陪玩进程清理失败，请检查进程状态。";}
                    handshaken=false;
                    if(runtime.Ready)await runtime.Invoke(()=>{live.Clear();return true;});else live.Clear();
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
        var excess=live.OrderBy(p=>p.Value.Started).Skip(store.Settings.MaxOnline).Select(p=>p.Key).ToHashSet();
        if(!worker.Running&&live.Count>0){foreach(var id in live.Keys.ToArray())Fail(id,"陪玩工作进程已退出，请核对后重新启动。");handshaken=false;}
        foreach(var id in live.Keys.ToArray())
        {
            var r=store.Get(id);var l=live[id];var eligible=Eligibility(r,now);
            if(now-l.Heartbeat>TimeSpan.FromSeconds(30))
            {commands.Add(new("stop",Id:id));Fail(id,"陪玩心跳超过30秒未更新，已停止；核对进程后人工重新启动。");continue;}
            var stop=excess.Contains(id)||!eligible.Allowed||now-l.Started>TimeSpan.FromMinutes(r.Profile.Schedule.SessionMinutes);
            if(stop)
            {
                commands.Add(new("stop",Id:id));live.Remove(id);
                store.Mutate(r,n=>n.RestUntil=now.AddMinutes(n.Profile.Schedule.RestMinutes));continue;
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
                var knowledge=CompanionKnowledgeReader.Read(store.Get(id),l.BlockedGoals.Keys.ToHashSet());
                l.Goal=knowledge?.Goal;
                if(knowledge!=null)knowledge=knowledge with{AllowPk=store.Settings.AllowPk,Revision=knowledge.Revision+"/"+store.Settings.AllowPk+"/"+knowledge.InSafeZone};
                if(knowledge!=null&&knowledge.Revision!=l.Knowledge){l.Knowledge=knowledge.Revision;commands.Add(new("knowledge",Id:id,Knowledge:knowledge));}
            }
        }
        if(now<nextConnect||store.LoadError!=null||runtime.Stopping)return commands;
        var pending=store.Records.Values.Where(r=>!live.ContainsKey(r.Profile.Id)&&Eligibility(r,now).Allowed).OrderBy(r=>store.TodaySeconds(r,now)).FirstOrDefault();
        queueReason="等待名额";
        if(pending==null)return commands;
        if(live.Count>=store.Settings.MaxOnline){queueReason="达到本版本陪玩在线上限";return commands;}
        var world=Envir.Main;
        var host=LocalHost();
        if(world.Connections.Count>=Settings.MaxUser-store.Settings.ReservePlayers){queueReason=$"总连接 {world.Connections.Count}/{Settings.MaxUser}，预留真人 {store.Settings.ReservePlayers} 个名额；可在系统设置调整";return commands;}
        var sameIp=world.Connections.Count(c=>c.IPAddress==host);
        if(sameIp>=Math.Max(0,Settings.MaxIP-1)){queueReason=$"同 IP 连接 {sameIp}/{Settings.MaxIP}，另预留 1 个本机名额；请在系统设置提高同 IP 上限，保存即生效";return commands;}
        if(Envir.IPBlocks.TryGetValue(host,out var banned)&&banned>world.Now){queueReason="本机 IP 正受原服封禁保护";return commands;}
        if(!Settings.AllowLogin||!pending.Registered&&!Settings.AllowNewAccount){queueReason="原服关闭登录或新账户注册";return commands;}
        if(world.GetAccount(pending.Account)?.Connection!=null){Fail(pending.Profile.Id,"账户已被其他连接占用，请先人工核对。");return commands;}
        // 注册同样受原 IP 频率保护，达到上限等待，不能触发整 IP 封禁。
        if(!pending.Registered&&Envir.ConnectionLogs.TryGetValue(host,out var log))
        {log.PruneAccounts(world.Time);if(log.AccountsMade.Count>2){queueReason="等待原服同 IP 注册频率窗口";return commands;}}
        if(pending.CharacterId==0&&Envir.ConnectionLogs.TryGetValue(host,out var creationLog))
        {creationLog.PruneCharacters(world.Time);if(creationLog.CharactersMade.Count>4){queueReason="等待原服同 IP 建角频率窗口（每小时最多 5 次尝试）";return commands;}}
        if(!worker.Running||!handshaken)return [new("prepare")];
        var resource="http://"+(host.Contains(':')?"["+host+"]":host)+":"+(Environment.GetEnvironmentVariable("MIR2_RESOURCE_PORT")??"5080");
        var cache=Path.Combine(paths.Runtime,"cache","companions");
        live[pending.Profile.Id]=new();store.ReserveOnline(pending,now,10);live[pending.Profile.Id].ReservedUntil=now.AddSeconds(10);
        commands.Add(new("start",new(store.Public(pending),pending.Account,pending.Password,pending.Registered,host,runtime.GamePort,
            paths.VersionId,live[pending.Profile.Id].Session,CompanionProtocol.Fingerprint,resource,runtime.AssetVersion,cache,pending.Seed)));
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
