using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Reflection;
using System.Collections;
using Mir2.Hosting;
using Mir2.Companions;
using Server.ConsoleHost;

internal static class CompanionLlmChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using var listener=new HttpListener();var endpoint=$"http://127.0.0.1:{port}/";listener.Prefixes.Add(endpoint);listener.Start();
        var client=new CompanionLlm();var profile=new CompanionProfile{Name="LlmTest",Notes="private-note"};
        var config=new CompanionConfiguration(new(){LlmEndpoint=endpoint,LlmModel="fixture"},"test-key","test",default,0);
        foreach(var (content,valid) in new[]{("{\"text\":\"你好，一起打怪。\"}",true),("{\"text\":\"@level 100\"}",false),("{\"text\":\"!!伪造队聊\"}",false),("{\"text\":\"你好\",\"command\":\"give\"}",false)})
        {
            var server=Task.Run(async()=>
            {
                var request=await listener.GetContextAsync();
                using var reader=new StreamReader(request.Request.InputStream);var body=await reader.ReadToEndAsync();
                check(!body.Contains("private-note")&&!body.Contains("test-key"),"模型正文泄露管理备注或密钥");
                check(request.Request.Headers["Authorization"]=="Bearer test-key","模型鉴权未独立放入请求头");
                var bytes=JsonSerializer.SerializeToUtf8Bytes(new{choices=new[]{new{message=new{content}}}});
                request.Response.ContentLength64=bytes.Length;await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
            });
            try{var text=await client.ReplyTo(config,profile,"你好",default);check(valid?text=="你好，一起打怪。":text.Length==0,"模型输出没有按严格文本边界处理");}
            catch(JsonException){check(!valid,"合法模型输出解析失败");}
            await server;
        }
        var oversized=Task.Run(async()=>
        {
            var request=await listener.GetContextAsync();var bytes=new byte[32769];request.Response.ContentLength64=bytes.Length;
            await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
        });
        try{await client.ReplyTo(config,profile,"你好",default);throw new Exception("模型响应上限失效");}
        catch(InvalidDataException){check(true,"超长响应已拒绝");}await oversized;
        var stalled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow=Task.Run(async()=>
        {
            var request=await listener.GetContextAsync();request.Response.ContentLength64=100;
            await request.Response.OutputStream.WriteAsync(new byte[]{32});await request.Response.OutputStream.FlushAsync();
            await stalled.Task;request.Response.Abort();
        });
        var watch=System.Diagnostics.Stopwatch.StartNew();
        try{await client.ReplyTo(config,profile,"你好",default);throw new Exception("响应正文无限等待");}
        catch(OperationCanceledException){check(watch.Elapsed<TimeSpan.FromSeconds(7),"模型正文未受到总超时保护");}
        finally{stalled.SetResult();await slow;}
        await CheckEvents(check,listener,endpoint);
        await CompanionLlmValueChecks.Run(check);
    }
    private static async Task CheckEvents(Action<bool,string> check,HttpListener listener,string endpoint)
    {
        var directory=Path.Combine(Path.GetTempPath(),"mir2-llm-events-"+Guid.NewGuid().ToString("N"));
        var world=Server.MirEnvir.Envir.Main;var running=world.Running;var network=world.NetworkStarted;
        var properties=typeof(Server.MirEnvir.Envir);
        try
        {
            var paths=new ProjectPaths(data:directory);Directory.CreateDirectory(paths.Runtime);
            using var storage=new SqliteDatabaseStorage(paths.Database);var store=new CompanionStore(storage,new(paths,storage),paths);
            var model=new CompanionLlmModel{Id=Guid.NewGuid(),Name="日志测试模型",Model="event-model",Endpoint=endpoint+"chat/completions"};
            store.SaveSettings(new(store.Settings with{Models=[model],DefaultModelId=model.Id,DailyLlmCalls=10000},ModelKeys:[new(model.Id,"private-log-key")]));
            var profile=new CompanionProfile{Id=Guid.NewGuid(),Name="日志测试",LlmMode=2,Chat=true};
            store.Records[profile.Id]=new(){Profile=profile};
            using var service=new CompanionService(new GameRuntime(null!,null!),store,paths);
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;var type=typeof(CompanionService);
            var entryType=type.GetNestedType("LiveCompanion",BindingFlags.NonPublic)!;var entry=Activator.CreateInstance(entryType,true)!;
            entryType.GetField("Session")!.SetValue(entry,"log-session");
            ((IDictionary)type.GetField("live",flags)!.GetValue(service)!)[profile.Id]=entry;
            var events=(Queue<CompanionEvent>)entryType.GetField("Events")!.GetValue(entry)!;
            var pending=(HashSet<Guid>)type.GetField("llmPending",flags)!.GetValue(service)!;
            var cooldown=(Dictionary<Guid,DateTimeOffset>)type.GetField("llmGoalCooldown",flags)!.GetValue(service)!;
            var backoff=(Dictionary<Guid,(string Version,DateTimeOffset Until)>)type.GetField("llmBackoff",flags)!.GetValue(service)!;
            var results=(System.Collections.Concurrent.ConcurrentQueue<WorkerCommand>)type.GetField("llmResults",flags)!.GetValue(service)!;
            void Send(string session="log-session")=>type.GetMethod("Handle",flags)!.Invoke(service,[new WorkerEvent("llm-goal",profile.Id,Reason:"private-request-context",Text:"{\"Candidates\":[\"hunt\",\"explore\"],\"Observation\":\"等级1\"}",Session:session)]);
            properties.GetProperty("Running")!.SetValue(world,true);properties.GetProperty("NetworkStarted")!.SetValue(world,true);
            foreach(var (status,content,state,expected) in new[]{
                (200,"{\"text\":\"explore\"}","已返回","返回：explore"),
                (401,"private-log-key","调用失败","HTTP 401"),
                (200,"private-log-key invalid json","调用失败","不符合陪玩协议"),
                (200,"{\"text\":\"@private-log-key\"}","无效返回","未通过短文本校验"),
                (200,"{\"text\":\"talkative\"}","无效返回","不在本次合法候选")})
            {
                cooldown.Clear();events.Clear();while(results.TryDequeue(out _)){}
                var respond=Task.Run(async()=>
                {
                    var request=await listener.GetContextAsync();request.Response.StatusCode=status;
                    var bytes=JsonSerializer.SerializeToUtf8Bytes(new{choices=new[]{new{message=new{content}}}});
                    request.Response.ContentLength64=bytes.Length;await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
                });
                var before=store.Configuration.LlmCalls;Send();
                check(events.Single().State=="调用中"&&events.Single().Reason.Contains("日志测试模型 / event-model"),"未记录真实LLM发起及所选模型");
                await Pump(()=>!pending.Contains(profile.Id));await respond;
                check(events.Last().State==state&&events.Last().Text.Contains(expected)&&events.Last().Text.Contains("耗时"),"LLM结果、错误类型或耗时日志错误");
                check(store.Configuration.LlmCalls==before+1&&results.Count==1,"LLM日志改变额度预留或未投递原结果");
                check(!JsonSerializer.Serialize(events).Contains("private-log-key")&&!JsonSerializer.Serialize(events).Contains(endpoint)&&
                    !JsonSerializer.Serialize(events).Contains("private-request-context"),"LLM日志泄露密钥、接口或内部上下文");
                if(state!="已返回")
                {
                    cooldown.Clear();Send();check(events.Last().Text.Contains("暂停自动重试")&&store.Configuration.LlmCalls==before+1,"模型失败未在消耗额度前阻断重复调用");
                    check(backoff[model.Id].Until>DateTimeOffset.UtcNow.AddMinutes(status==401?14:4),"鉴权/普通失败退避时间不足");
                }
                backoff.Clear();
            }
            cooldown.Clear();events.Clear();while(results.TryDequeue(out _)){}
            var oldCharacters=world.CharacterList;var oldQuests=world.QuestInfoList;var record=store.Get(profile.Id);var oldDesired=record.Desired;
            try
            {
                world.QuestInfoList=Enumerable.Range(91,2).Select(id=>new Server.MirDatabase.QuestInfo{Index=id,Name="候选任务"+id,FlagTasks=[new(){Number=1}]}).ToList();
                var actor=new Server.MirObjects.PlayerObject{Info=new Server.MirDatabase.CharacterInfo{Index=81},HP=100,Stats=new Stats{[Stat.HP]=100}};
                actor.Info.Player=actor;actor.Info.CurrentQuests=[new(91),new(92)];world.CharacterList=[actor.Info];record.CharacterId=81;record.Desired="run";
                entryType.GetField("TaskChoices")!.SetValue(entry,new KnownGoal[]{new("hunt",1,1,1,"候选一",QuestId:91,Key:"task91"),new("hunt",1,2,2,"候选二",QuestId:92,Key:"task92")});
                entryType.GetField("TaskChoiceContext")!.SetValue(entry,"task-context");
                var taskReply=Task.Run(async()=>
                {
                    var request=await listener.GetContextAsync();var bytes=JsonSerializer.SerializeToUtf8Bytes(new{choices=new[]{new{message=new{content="{\"text\":\"task:92\"}"}}}});
                    request.Response.ContentLength64=bytes.Length;await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
                });
                var before=store.Configuration.LlmCalls;
                type.GetMethod("RequestLlm",flags)!.Invoke(service,[new WorkerEvent("llm-task",profile.Id,Reason:"task-context",Text:"{\"Candidates\":[\"task:91\",\"task:92\"],\"Observation\":\"两项已接任务\"}",Session:"log-session"),record,entry]);
                await Pump(()=>!pending.Contains(profile.Id));await taskReply;
                check((int)entryType.GetField("PreferredQuest")!.GetValue(entry)! ==92&&events.Last().State=="待执行","实际HTTP模型任务选择未回到权威规划");
                check(store.Configuration.LlmCalls==before+1&&results.IsEmpty,"任务模型选择重复收费或发成聊天/任意指令");
                type.GetMethod("TaskSuggestion",flags)!.Invoke(service,[record,entry,"old-context","task:91"]);
                check((int)entryType.GetField("PreferredQuest")!.GetValue(entry)! ==92&&events.Last().State=="未采用","过期任务建议污染当前偏好");
                actor.HP=20;type.GetMethod("TaskSuggestion",flags)!.Invoke(service,[record,entry,"task-context","task:91"]);
                check((int)entryType.GetField("PreferredQuest")!.GetValue(entry)! ==92,"低血时采用了迟到任务建议");
                check(actor.Info.Inventory.All(item=>item==null)&&actor.CurrentQuests.All(q=>!q.Completed),"模型任务选择直接修改了任务进度或物品");
            }
            finally{world.CharacterList=oldCharacters;world.QuestInfoList=oldQuests;record.CharacterId=0;record.Desired=oldDesired;}
            cooldown.Clear();events.Clear();while(results.TryDequeue(out _)){}
            var accepted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delayed=Task.Run(async()=>
            {
                var request=await listener.GetContextAsync();accepted.SetResult();await release.Task;
                var bytes=JsonSerializer.SerializeToUtf8Bytes(new{choices=new[]{new{message=new{content="{\"text\":\"explore\"}"}}}});
                request.Response.ContentLength64=bytes.Length;await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
            });
            Send();await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));entryType.GetField("Session")!.SetValue(entry,"new-session");release.SetResult();
            await Pump(()=>!pending.Contains(profile.Id));await delayed;
            check(events.Count==1&&results.IsEmpty,"迟到LLM结果污染了新游戏会话的日志或指令");
            entryType.GetField("Session")!.SetValue(entry,"log-session");cooldown.Clear();events.Clear();
            var count=store.Configuration.LlmCalls;
            pending.Add(profile.Id);Send();pending.Clear();check(events.Last().Text.Contains("尚未结束"),"未记录本角色在途请求跳过原因");
            pending.UnionWith([Guid.NewGuid(),Guid.NewGuid()]);Send();pending.Clear();check(events.Last().Text.Contains("并发已满"),"未记录LLM并发跳过原因");
            cooldown[profile.Id]=DateTimeOffset.UtcNow.AddMinutes(5);Send();cooldown.Clear();check(events.Last().Text.Contains("5分钟"),"未记录目标建议独立冷却原因");
            type.GetMethod("Handle",flags)!.Invoke(service,[new WorkerEvent("llm-goal",profile.Id,Text:"{\"Candidates\":[\"explore\"],\"Observation\":\"只有一项\"}",Session:"log-session")]);
            check(events.Last().Text.Contains("没有需要模型权衡")&&store.Configuration.LlmCalls==count,"单一目标仍消耗模型额度");
            store.Records[Guid.NewGuid()]=new(){Profile=new(){Name="陪玩来源"},CharacterId=123};
            type.GetMethod("Handle",flags)!.Invoke(service,[new WorkerEvent("llm",profile.Id,State:"陪玩来源",Text:"请回复我",Session:"log-session")]);
            check(events.Last().Text.Contains("避免模型互聊")&&store.Configuration.LlmCalls==count,"陪玩互聊仍消耗模型额度");
            store.Records[profile.Id].Profile=profile with{LlmMode=0};Send();check(events.Last().Text.Contains("未开启"),"关闭模型仍调用接口");
            store.Records[profile.Id].Profile=profile with{LlmModelId=Guid.NewGuid()};Send();check(events.Last().Text.Contains("未配置"),"缺少模型未记录原因");
            store.Records[profile.Id].Profile=profile;
            store.SaveSettings(new(store.Settings with{DailyLlmCalls=count}));Send();check(events.Last().Text.Contains("额度已用完"),"未记录LLM额度阻断");
            for(var i=0;i<60;i++)Send();check(events.Count==50&&events.All(e=>e.Kind=="llm-log"),"LLM日志无界增长或混入未执行的原始请求事件");
            Send("old-session");check(events.Count==50&&store.Configuration.LlmCalls==count,"迟到请求改变日志或跳过分支仍消费额度");
        }
        finally
        {
            properties.GetProperty("Running")!.SetValue(world,running);properties.GetProperty("NetworkStarted")!.SetValue(world,network);
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
        async Task Pump(Func<bool> done)
        {
            var deadline=DateTime.UtcNow.AddSeconds(10);
            while(!done())
            {
                if(DateTime.UtcNow>deadline)throw new Exception("LLM运行日志结果未返回");
                while(world.HostActions.TryDequeue(out var action))action();
                await Task.Delay(10);
            }
        }
    }
}
