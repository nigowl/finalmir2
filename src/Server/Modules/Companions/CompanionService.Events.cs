using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private readonly Dictionary<Guid,DateTimeOffset> llmGoalCooldown=[];
    private readonly HashSet<Guid> llmGoalPending=[];
    private readonly Dictionary<Guid,(string Version,DateTimeOffset Until)> llmBackoff=[];
    private void Handle(WorkerEvent e)
    {
        if(e.Kind=="hello")
        {
            if(e.Protocol!=CompanionProtocol.Fingerprint)throw new InvalidDataException("陪玩程序与服务端 Shared 协议指纹不匹配。");
            handshaken=true;return;
        }
        if(e.Kind=="worker-failed")throw new IOException(e.Reason);
        if(!live.TryGetValue(e.Id,out var current)||e.Session!=current.Session)return;
        var r=store.Get(e.Id);current.Heartbeat=DateTimeOffset.UtcNow;
        if(e.Kind is "llm"or"llm-goal"or"llm-duel"){RequestLlm(e,r,current);return;}
        if(e.Kind=="llm-outcome")
        {
            if(e.State is "已采用" or "未采用" or "待执行")AddEvent(current,new(DateTimeOffset.UtcNow,"llm-log",e.State,e.Reason,e.Text));
            return;
        }
        if(e.Kind is "status"or"heartbeat" && current.StoppingAt==null) {current.State=e.State;current.Reason=e.Reason;}
        if(e.Kind is "goal-blocked" or "combat-blocked"&&current.Goal?.Key==e.Text&&e.Text.Length>0)
        {
            var keys=new List<string>{e.Text};
            if(e.Kind=="combat-blocked"&&current.Goal.Kind=="hunt"&&e.MonsterId>0&&current.Goal.MonsterIds?.Contains(e.MonsterId)==true)
                keys.Add($"combat/{e.MonsterId}");
            foreach(var key in keys)
            {
                if(current.BlockedGoals.Count>=128)current.BlockedGoals.Remove(current.BlockedGoals.MinBy(pair=>pair.Value).Key);
                current.BlockedGoals[key]=DateTimeOffset.UtcNow.AddSeconds(90);
            }
            current.NextKnowledge=DateTimeOffset.MinValue;
        }
        if(e.Kind=="registered")
        {
            var account=Envir.Main.GetAccount(r.Account)??throw new InvalidDataException("原账户尚未存在。");
            if(account.AdminAccount)throw new InvalidDataException("陪玩账户不能具有 GM 权限。");
            store.Mutate(r,n=>{n.Registered=true;n.AccountId=account.Index;},saveAccounts:true);
        }
        if(e.Kind=="entered")
        {
            var account=Envir.Main.GetAccount(r.Account)??throw new InvalidDataException("原账户不存在。");
            var character=account.Characters.SingleOrDefault(c=>c.Name==r.Profile.Name)??throw new InvalidDataException("原角色身份不匹配。");
            if((int)character.Class!=r.Profile.Class||(int)character.Gender!=r.Profile.Gender)throw new InvalidDataException("陪玩角色身份不匹配。");
            store.Mutate(r,n=>{n.CharacterId=character.Index;n.AccountId=account.Index;n.Registered=true;},saveAccounts:true);
            // 子进程可能在原服进入后、私有回执处理前退出；不能把迟到回执误判成全局协议故障。
            if(character.Player==null){Recover(e.Id,"原连接在确认进入前已结束，等待智能托管重新核对。",DateTimeOffset.UtcNow);return;}
            current.Knowledge="";
            current.Entered=true;
        }
        if(e.Kind=="death")
        {
            store.Mutate(r,n=>n.SessionDeaths=Math.Min(1000,n.SessionDeaths+1));
            MessageQueue.Instance.Enqueue($"陪玩死亡：{r.Profile.Name}；{e.Reason}；{e.Text}");
        }
        if(e.Kind=="protection-rejected")MessageQueue.Instance.Enqueue($"陪玩保护拒绝：{r.Profile.Name}；{e.Reason}；{e.Text}");
        if(e.Kind=="failed") { if(e.Recoverable||e.Reason==CompanionSupervision.DeathBudget&&r.SessionDeaths>=r.Profile.DeathLimit)Recover(e.Id,e.Reason,DateTimeOffset.UtcNow);else Fail(e.Id,e.Reason); }
        if(e.Kind=="stopped"&&live.ContainsKey(e.Id))
        {if(current.StoppingAt==null)Recover(e.Id,CompanionSupervision.SessionExit,DateTimeOffset.UtcNow);if(live.ContainsKey(e.Id))current.WorkerStopped=true;}
        if(e.Kind!="heartbeat")AddEvent(current,new(DateTimeOffset.UtcNow,e.Kind,e.State,e.Reason,e.Text));
    }
    private static void AddEvent(LiveCompanion current,CompanionEvent value)
    {
        current.Events.Enqueue(value);
        while(current.Events.Count>50)current.Events.Dequeue();
    }
    private bool RequestLlm(WorkerEvent e,CompanionRecord r,LiveCompanion current)
    {
        var guild = e.Kind == "llm-guild"; var task = e.Kind == "llm-task"; var duel = e.Kind == "llm-duel";
        var goal=e.Kind is "llm-goal" or "llm-guild" or "llm-task" or "llm-duel";
        var purpose=guild?"行会与攻城决策":task?"任务先后决策":duel?"切磋决策":goal?"目标建议":"聊天回复";
        void Log(string state,string message)
        {
            AddEvent(current,new(DateTimeOffset.UtcNow,"llm-log",state,purpose,message));
            if(state=="未调用"&&!guild&&!task)llmResults.Enqueue(new(duel?"duel":goal?"goal":"llm",Id:e.Id,Context:e.Session+"/"+e.Reason));
        }
        if(r.Profile.LlmMode==0||goal&&r.Profile.LlmMode!=2||!goal&&!r.Profile.Chat){Log("未调用","当前角色未开启此项模型增强，继续规则行为。");return false;}
        if(current.StoppingAt!=null){Log("未调用","角色正在下线，继续正常退场。");return false;}
        if(!goal&&(e.State.Length==0||store.Records.Values.Any(record=>record.CharacterId>0&&record.Profile.Name==e.State)))
        {Log("未调用","消息来自陪玩或来源尚未确认，使用规则回复，避免模型互聊。");return false;}
        var candidates=guild?current.GuildChoices.Select(CompanionGuildDevelopment.ChoiceKey).ToArray():task?current.TaskChoices.Select(p=>"task:"+p.QuestId).ToArray():goal?CompanionLlm.GoalCandidates(e.Text,duel):[];
        if(duel && !DuelAllowed(r,e.State)) { Log("未调用","切磋双方状态或PK权限已改变，继续规则行为。");return false; }
        if(goal&&(candidates.Length<2||!guild&&!task&&!duel&&current.Goal is { } objective&&(objective.Kind!="hunt"||objective.QuestId>0||objective.Key.StartsWith("fund/",StringComparison.Ordinal))))
        {Log("未调用","当前没有需要模型权衡的多个闲时选项，继续规则行为。");return false;}
        if(llmPending.Contains(e.Id)){Log("未调用","此角色的上次模型请求尚未结束，继续规则行为。");return false;}
        if(llmPending.Count>=2){Log("未调用","模型并发已满（最多2个请求），继续规则行为。");return false;}
        if(goal&&llmGoalPending.Count>=1){Log("未调用","已有目标建议在调用，保留模型名额供实际玩家对话。");return false;}
        var cooldown=goal?llmGoalCooldown:llmCooldown;
        if(cooldown.GetValueOrDefault(e.Id)>DateTimeOffset.UtcNow){Log("未调用",goal?"尚在5分钟目标建议间隔内，继续规则行为。":"尚在30秒调用间隔内，继续规则行为。");return false;}
        var selected=CompanionModels.Select(store.Configuration,r.Profile.LlmModelId);
        if(selected==null){Log("未调用","未配置可用模型或所选模型已停用，继续规则行为。");return false;}
        var modelVersion=AdminChanges.Version(selected);
        if(llmBackoff.TryGetValue(selected.Model.Id,out var backoff)&&backoff.Version==modelVersion&&backoff.Until>DateTimeOffset.UtcNow)
        {Log("未调用","此模型近期调用失败，暂停自动重试以节省额度；修改该模型配置后可立即重新检查。");return false;}
        if(!store.ReserveLlm()){Log("未调用",$"本版本今日模型请求额度已用完（{store.Settings.DailyLlmCalls}次），继续规则行为。");return false;}
        llmPending.Add(e.Id);if(goal)llmGoalPending.Add(e.Id);cooldown[e.Id]=DateTimeOffset.UtcNow.AddSeconds(goal?300:30);
        var profile=store.Public(r);var generation=runId;
        var description=$"{purpose} · {selected.Model.Name} / {selected.Model.Model}";
        AddEvent(current,new(DateTimeOffset.UtcNow,"llm-log","调用中",description,"已保留1次请求额度，等待模型返回。"));
        _=RunLlm();
        return true;
        async Task RunLlm()
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();var text="";var state="已返回";var detail="";var retryMinutes=5;
            try
            {
                text=await llm.ReplyTo(selected,profile,e.Text,CancellationToken.None,goal);
                if(text.Length==0){state="无效返回";detail="返回内容未通过短文本校验，继续规则行为。";}
                else if(goal&&!candidates.Contains(text)){text="";state="无效返回";detail="返回不在本次合法候选中，继续规则行为。";}
                else detail="返回："+text;
            }
            catch(Exception error)when(error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentException)
            {
                state="调用失败";
                if(error is HttpRequestException {StatusCode:System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden})retryMinutes=15;
                // 不记录原始异常/响应，避免接口地址、密钥或供应商回显进入角色日志。
                detail=error switch
                {
                    HttpRequestException {StatusCode:{ } status}=>$"接口返回 HTTP {(int)status}，请检查接口、密钥和模型权限。",
                    HttpRequestException=>"无法连接模型接口，请检查网络、地址和证书。",
                    OperationCanceledException=>"5秒内未收到完整响应，调用超时。",
                    _=>"模型响应格式或内容不符合陪玩协议。"
                };
            }
            if(runtime.Ready&&!runtime.Stopping)
            {
                try
                {
                    await runtime.Invoke(()=>
                    {
                        if(generation!=runId)return false;
                        llmPending.Remove(e.Id);llmGoalPending.Remove(e.Id);
                        if(CompanionModels.Select(store.Configuration,selected.Model.Id) is { } currentModel&&AdminChanges.Version(currentModel)==modelVersion)
                        {
                            if(text.Length==0)llmBackoff[selected.Model.Id]=(modelVersion,DateTimeOffset.UtcNow.AddMinutes(retryMinutes));
                            else llmBackoff.Remove(selected.Model.Id);
                        }
                        if(!live.TryGetValue(e.Id,out var active)||active.Session!=e.Session)return false;
                        AddEvent(active,new(DateTimeOffset.UtcNow,"llm-log",state,description,$"耗时 {watch.ElapsedMilliseconds} 毫秒 · {detail}"));
                        if(guild)GuildSuggestion(store.Get(e.Id),active,e.Reason,text);
                        else if(task)TaskSuggestion(store.Get(e.Id),active,e.Reason,text);
                        else llmResults.Enqueue(new(duel?"duel":goal?"goal":"llm",Id:e.Id,Text:text,Context:e.Session+"/"+e.Reason));
                        return true;
                    });
                }
                catch(Exception error)when(error is OperationCanceledException or InvalidOperationException){}
            }
        }
    }
    private void Fail(Guid id,string error)
    {
        var r=store.Get(id);live.Remove(id);
        store.Mutate(r,n=>{n.Error=error[..Math.Min(error.Length,500)];n.Desired="off";n.Profile=n.Profile with{Enabled=false};n.QueueSince=null;n.SessionUntil=null;n.SessionStarted=null;n.RecoveryAt=null;});
    }
}
