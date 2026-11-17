using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
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
        if(e.Kind is "llm"or"llm-goal")RequestLlm(e,r);
        if(e.Kind is "status"or"heartbeat" && current.StoppingAt==null) {current.State=e.State;current.Reason=e.Reason;}
        if(e.Kind=="goal-blocked"&&current.Goal?.Key==e.Text&&e.Text.Length>0)
        {
            if(current.BlockedGoals.Count>=128)current.BlockedGoals.Remove(current.BlockedGoals.MinBy(pair=>pair.Value).Key);
            current.BlockedGoals[e.Text]=DateTimeOffset.UtcNow.AddSeconds(90);current.NextKnowledge=DateTimeOffset.MinValue;
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
        if(e.Kind!="heartbeat")
        {
            current.Events.Enqueue(new(DateTimeOffset.UtcNow,e.Kind,e.State,e.Reason,e.Text));
            while(current.Events.Count>50)current.Events.Dequeue();
        }
    }
    private void RequestLlm(WorkerEvent e,CompanionRecord r)
    {
        var goal=e.Kind=="llm-goal";
        if(r.Profile.LlmMode==0||goal&&r.Profile.LlmMode!=2||!goal&&!r.Profile.Chat||llmPending.Count>=2||llmPending.Contains(e.Id)||llmCooldown.GetValueOrDefault(e.Id)>DateTimeOffset.UtcNow||CompanionModels.Select(store.Configuration,r.Profile.LlmModelId)==null||!store.ReserveLlm())return;
        llmPending.Add(e.Id);llmCooldown[e.Id]=DateTimeOffset.UtcNow.AddSeconds(30);
        var configuration=store.Configuration;var profile=store.Public(r);var generation=runId;
        _=RunLlm();
        async Task RunLlm()
        {
            var text="";
            try{text=await llm.ReplyTo(configuration,profile,e.Text,CancellationToken.None,goal);}
            catch(Exception error)when(error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentException)
            {text="";}
            llmResults.Enqueue(new(goal?"goal":"llm",Id:e.Id,Text:text,Context:e.Session+"/"+e.Reason));
            if(runtime.Ready&&!runtime.Stopping)
            {try{await runtime.Invoke(()=>{if(generation==runId)llmPending.Remove(e.Id);return true;});}catch(Exception error)when(error is OperationCanceledException or InvalidOperationException){}}
        }
    }
    private void Fail(Guid id,string error)
    {
        var r=store.Get(id);live.Remove(id);
        store.Mutate(r,n=>{n.Error=error[..Math.Min(error.Length,500)];n.Desired="off";n.Profile=n.Profile with{Enabled=false};n.QueueSince=null;n.SessionUntil=null;n.SessionStarted=null;n.RecoveryAt=null;});
    }
}
