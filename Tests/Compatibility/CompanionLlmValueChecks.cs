using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Crystal.Native;
using Mir2.Companion;
using Mir2.Companions;
using Server.ConsoleHost;
using S=ServerPackets;

internal static class CompanionLlmValueChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        check(CompanionLlm.GoalCandidates("{\"Candidates\":[\"hunt\",\"explore\"],\"Observation\":\"闲时发展\"}").Length==2,"合法闲时候选被拒绝");
        foreach(var text in new[]{"bad","null","{}","{\"Candidates\":[\"hunt\"],\"Observation\":\"单项\"}",
            "{\"Candidates\":[\"hunt\",\"hunt\"],\"Observation\":\"重复\"}","{\"Candidates\":[\"hunt\",\"command\"],\"Observation\":\"越权\"}"})
            check(CompanionLlm.GoalCandidates(text).Length==0,"无意义/非法候选未在收费前拒绝");
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var profile=new CompanionProfile{Id=Guid.NewGuid(),Name="价值检查",LlmMode=2,AcceptGroup=false,Chat=true};
        var events=new List<WorkerEvent>();
        var session=new CompanionSession(new(profile,"test","private",true,"127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,"test","value-run",CompanionProtocol.Fingerprint,"","","",1),
            value=>{events.Add(value);return Task.CompletedTask;});
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(CompanionSession);
        FieldInfo Field(string name)=>type.GetField(name,flags)!;
        Task Invoke(string method,params object[] args)=>(Task)type.GetMethod(method,flags)!.Invoke(session,args)!;
        var state=(GameState)Field("state").GetValue(session)!;
        var connection=(GameConnection)Field("connection").GetValue(session)!;
        await connection.Connect("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port);
        using var peer=await listener.AcceptTcpClientAsync();var stream=peer.GetStream();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            state.Apply(new S.MapInformation{MapIndex=1,FileName="Value"});
            state.Apply(new S.BaseStatsInfo{Stats=new BaseStats(MirClass.Warrior){Stats=[new(Stat.HP){Base=100},new(Stat.MP){Base=100}]}});
            state.Apply(new S.UserInformation{ObjectID=1,Name=profile.Name,HP=100,MP=100,Location=new(5,5),Inventory=new UserItem[80],Equipment=new UserItem[14],QuestInventory=[],Magics=[]});
            state.Apply(new S.ObjectMonster{ObjectID=2,Name="普通怪物",Location=new(8,7),AI=0});
            var map=(Client.MirObjects.MapReader)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.MirObjects.MapReader));
            map.Width=map.Height=12;map.MapCells=new Client.MirObjects.CellInfo[12,12];
            for(var x=0;x<12;x++)for(var y=0;y<12;y++)map.MapCells[x,y]=new();
            Field("map").SetValue(session,map);Field("mapName").SetValue(session,"Value");
            var knowledge=new CompanionKnowledge("test",1,false,false,false,[],[],[],[new(1,9,5,2)],NoHero:true,
                Enemies:[new(2,12,1,10,true)],Goal:new("hunt",1,8,7,"正常闲时练级",MonsterIds:[12],Key:"hunt/1/12"));
            session.Knowledge=knowledge;
            var now=Environment.TickCount64;
            Field("lastProgress").SetValue(session,now-30000);
            foreach(var goal in new[]{new KnownGoal("wait",1,5,5,"缺药",Key:"fund/wait"),new("service",1,5,5,"补药"),
                new("accept",1,5,5,"接取"),new("turn-in",1,5,5,"交付"),new("hunt",2,5,5,"跨图"),new("hunt",1,8,7,"任务",QuestId:1)})
            {
                session.Knowledge=knowledge with{Goal=goal};await Invoke("SuggestGoal",now);
                check(!events.Any(e=>e.Kind=="llm-goal"),"强制任务/筹资/服务/跨图仍付费询问闲时建议");
            }
            session.Knowledge=knowledge with{Exits=[]};await Invoke("SuggestGoal",now);check(events.Count==0,"只有打怪一个选项仍请求模型");
            session.Knowledge=knowledge;state.User!.HP=50;await Invoke("SuggestGoal",now);check(events.Count==0,"低血时仍请求闲时模型");state.User.HP=100;
            session.Paused=true;await Invoke("SuggestGoal",now);check(events.Count==0,"暂停时仍请求模型");session.Paused=false;
            await Invoke("SuggestGoal",now);check(events.Count(e=>e.Kind=="llm-goal")==1,"两个实际闲时选项未产生模型请求");
            var message=events.Single(e=>e.Kind=="llm-goal");check(CompanionLlm.GoalCandidates(message.Text).Length==2,"请求被截断或不是合法JSON");
            await Invoke("SuggestGoal",now+60001);await Invoke("SuggestGoal",now+300001);check(events.Count(e=>e.Kind=="llm-goal")==1,"相同上下文每分钟或5分钟重复请求");
            await session.GoalResult("value-run/"+message.Reason,"explore");check(events.Last().State=="待执行","合法返回没有待执行状态");
            await Invoke("Think",Environment.TickCount64);check(await Read()==(short)ClientPacketIds.Walk,"探索建议没有实际改变移动请求");
            check(events.Any(e=>e.Kind=="llm-outcome"&&e.State=="已采用"&&e.Text.Contains("探索")),"仅收到模型结果就标为采用，或真实执行未记录");
            check((string)Field("goalHint").GetValue(session)! == "","执行后未清除一次性建议");
            Field("moving").SetValue(session,false);Field("nextGoalRequest").SetValue(session,0L);Field("nextSameGoalRequest").SetValue(session,0L);
            events.Clear();await Invoke("SuggestGoal",Environment.TickCount64);message=events.Single(e=>e.Kind=="llm-goal");
            await session.GoalResult("value-run/"+message.Reason,"hunt");await Invoke("Think",Environment.TickCount64);
            check(await Read()==(short)ClientPacketIds.Walk&&events.Any(e=>e.Kind=="llm-outcome"&&e.State=="已采用"&&e.Text.Contains("战斗")),"打怪建议没有实际接近目标或未记录采用");
            Field("moving").SetValue(session,false);Field("combatTarget").SetValue(session,0u);Field("nextGoalRequest").SetValue(session,0L);Field("nextSameGoalRequest").SetValue(session,0L);
            events.Clear();await Invoke("SuggestGoal",Environment.TickCount64);message=events.Single(e=>e.Kind=="llm-goal");
            session.Knowledge=knowledge with{Goal=new("service",1,5,5,"临时补药")};await session.GoalResult("value-run/"+message.Reason,"explore");
            check(events.Last().State=="未采用"&&(string)Field("goalHint").GetValue(session)! == "","场景改变后仍采用旧建议");
            session.Knowledge=knowledge;events.Clear();
            await Invoke("Social",new S.Chat{Type=ChatType.WhisperIn,Message="真人玩家=>这个怪物怎么打？"});
            var chat=events.Single(e=>e.Kind=="llm");check(chat.State=="真人玩家","私聊模型请求没有携带原消息真实来源");
            session.LlmResult("value-run/"+chat.Reason,"先补充药品，再按当前任务选择合适的怪物。");
            await Invoke("PendingSpeech",Environment.TickCount64);check(await Read()==(short)ClientPacketIds.Chat&&events.Any(e=>e.Kind=="llm-outcome"&&e.State=="已采用"&&e.Reason=="聊天回复"),"真人私聊模型回复没有经原TCP发送并记录采用");
            var calls=events.Count(e=>e.Kind=="llm");Field("nextChat").SetValue(session,0L);
            await Invoke("Social",new S.Chat{Type=ChatType.WhisperIn,Message="真人玩家=>这个怪物怎么打？"});
            check(await Read()==(short)ClientPacketIds.Chat&&events.Count(e=>e.Kind=="llm")==calls,"10分钟内相同玩家重复消息仍消耗模型请求");
        }
        finally{await session.Stop();}
        async Task<short> Read()
        {
            var header=new byte[4];await stream.ReadExactlyAsync(header,timeout.Token);var body=new byte[BitConverter.ToUInt16(header)-4];
            await stream.ReadExactlyAsync(body,timeout.Token);return BitConverter.ToInt16(header,2);
        }
    }
}
