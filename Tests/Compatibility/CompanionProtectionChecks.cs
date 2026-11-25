using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Mir2.Companion;
using Mir2.Companions;
using S = ServerPackets;

internal static class CompanionProtectionChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        foreach(var mode in new[]{"waiting","crowd","rejected","unknown","random","public-supply-crowd","public-supply-low","public-supply-healthy"})await Scenario(check,mode);
    }

    private static async Task Scenario(Action<bool,string> check,string mode)
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var profile=new CompanionProfile{Id=Guid.NewGuid(),Name="ProtectionAck",AcceptGroup=false,Chat=false};
        var events=new ConcurrentQueue<WorkerEvent>();
        var session=new CompanionSession(new(profile,"ProtectAccount","ProtectPassword",true,"127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port,"test","protection-check",CompanionProtocol.Fingerprint,"","","",1),
            e=>{events.Enqueue(e);return Task.CompletedTask;}){Paused=true};
        var random=mode=="random";var supply=mode.StartsWith("public-supply",StringComparison.Ordinal);
        session.Control(new("knowledge",Knowledge:new("protect",1,false,supply,false,[],[],[],[],NoTownTeleport:random||supply,NoRandom:!random)));
        session.Begin();using var peer=await listener.AcceptTcpClientAsync(timeout.Token);var stream=peer.GetStream();
        try
        {
            await Send(new S.Connected());check(await Read()==(short)ClientPacketIds.ClientVersion,"保护检查握手错误");
            await Send(new S.ClientVersion{Result=1});check(await Read()==(short)ClientPacketIds.Login,"保护检查登录错误");
            await Send(new S.LoginSuccess{Characters=[new SelectInfo{Index=1,Name=profile.Name}]});
            check(await Read()==(short)ClientPacketIds.StartGame,"保护检查没有进入角色");
            await Send(new S.BaseStatsInfo{Stats=new BaseStats(MirClass.Warrior){Stats=[new(Stat.HP){Base=100}]}});
            var info=new ItemInfo{Index=9,Name=random?"测试随机卷":"测试回城卷",Type=ItemType.Scroll,Shape=(short)(random?2:1)};
            await Send(new S.NewItemInfo{Info=info});await Send(new S.MapInformation{MapIndex=1,FileName="Unused"});
            await Send(new S.UserInformation{ObjectID=1,Name=profile.Name,HP=100,MP=100,Location=supply?new(5,5):default,Inventory=[new UserItem(info){UniqueID=99,Count=2}],Equipment=new UserItem[14],QuestInventory=[]});
            check(await Read()==(short)ClientPacketIds.SwitchGroup,"保护检查组队设置错误");
            while(!events.Any(e=>e.State=="已暂停"))await Task.Delay(10,timeout.Token);
            // 模拟已有任务的15秒无进度等待；保命不能共用该延时。
            var next=typeof(CompanionSession).GetField("nextAction",BindingFlags.NonPublic|BindingFlags.Instance)!;
            next.SetValue(session,Environment.TickCount64+15000);
            if(supply)
            {
                var map=(Client.MirObjects.MapReader)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.MirObjects.MapReader));
                map.Width=map.Height=10;map.MapCells=new Client.MirObjects.CellInfo[10,10];
                for(var x=0;x<10;x++)for(var y=0;y<10;y++)map.MapCells[x,y]=new();
                typeof(CompanionSession).GetField("map",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(session,map);
                typeof(CompanionSession).GetField("mapName",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(session,"Unused");
                session.Knowledge=session.Knowledge! with{Goal=new("service",1,5,5,"原地补货",Key:"public-supply")};
                await Send(new S.ObjectMonster{ObjectID=2,Location=new(6,5)});await Send(new S.Struck{AttackerID=2});
                if(mode=="public-supply-crowd")
                {await Send(new S.ObjectMonster{ObjectID=3,Location=new(5,6)});await Send(new S.Struck{AttackerID=3});}
                await Send(new S.HealthChanged{HP=mode=="public-supply-low"?20:100,MP=100});
                await Send(new S.GroupInvite{Name="补货避险屏障"});check(await Read()==(short)ClientPacketIds.GroupInvite,"补货避险状态未完成接收");
                session.Paused=false;
                await (Task)typeof(CompanionSession).GetMethod("Think",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(session,[Environment.TickCount64])!;
                check(await Read()==(short)(mode=="public-supply-healthy"?ClientPacketIds.PublicShopRequest:ClientPacketIds.Walk),
                    "低血/围攻被商城补货抢占撤退，或健康单怪时错误阻止补货："+mode);
                check(typeof(CompanionSession).GetField("pendingAsset",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(session)==null,"读取目录或撤退预先锁定了购买资产");
                return;
            }
            if(mode=="crowd" || random)
            {
                await Send(new S.ObjectMonster{ObjectID=2,Location=new(1,0)});
                await Send(new S.ObjectMonster{ObjectID=3,Location=new(0,1)});
                await Send(new S.Struck{AttackerID=2});
            }
            var began=Environment.TickCount64;await Send(new S.HealthChanged{HP=mode=="crowd"?60:20,MP=100});
            check(await Read()==(short)ClientPacketIds.UseItem,"等待任务或多怪承伤时没有发送对应保命卷轴");
            check(Environment.TickCount64-began<700,"保护仍被发展动作延时阻挡");
            check((long)next.GetValue(session)!>Environment.TickCount64,"药卷保护篡改了发展动作冷却");
            if(mode!="unknown")await Send(new S.UseItem{Grid=MirGridType.Inventory,UniqueID=99,Success=mode!="rejected"});
            if(mode is "unknown" or "rejected")
            {
                while(!session.Finished)await Task.Delay(10,timeout.Token);
                check(events.Any(e=>e.Kind=="failed"&&e.Reason.Contains(mode=="unknown"?"回执超时":"拒绝自动保护")),"拒绝/超时没有停用并保留原错误");
                check(await Read()==null,"拒绝或未知请求被自动重发");
                if(mode=="rejected")check(events.Any(e=>e.Kind=="protection-rejected"&&e.Text.Contains("测试回城卷")&&e.Text.Contains("99")),"保护拒绝丢失物品现场");
            }
            else
            {
                await Send(new S.GroupInvite{Name="保护回执屏障"});check(await Read()==(short)ClientPacketIds.GroupInvite,"保护成功后未继续接收游戏回执");
                check(!session.Finished&&!events.Any(e=>e.Kind=="failed"),"正常保护被误停用");
                if (random)
                {
                    check(events.Any(e=>e.Kind=="escape"&&e.Text.Contains("随机传送卷")),"禁回城受击时未按原回执确认使用随机卷");
                    var accepted = (HashSet<int>)typeof(CompanionSession).GetField("acceptedQuests", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
                    var finished = (HashSet<int>)typeof(CompanionSession).GetField("finishedQuests", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
                    accepted.UnionWith([30, 31, 32, 33, 34]); finished.UnionWith([30, 33, 34]);
                    await Send(new S.NewQuestInfo { Info = new ClientQuestInfo { Index = 30, Name = "赏金", Group = "盟重日常·巡边赏金", Type = QuestType.Daily } });
                    await Send(new S.NewQuestInfo { Info = new ClientQuestInfo { Index = 31, Name = "补给", Group = "盟重日常·魔血补给", Type = QuestType.Daily } });
                    await Send(new S.NewQuestInfo { Info = new ClientQuestInfo { Index = 32, Name = "剧情", Group = "剧情任务", Type = QuestType.General } });
                    await Send(new S.NewQuestInfo { Info = new ClientQuestInfo { Index = 33, Name = "普通每日", Group = "其他每日", Type = QuestType.Daily } });
                    await Send(new S.NewQuestInfo { Info = new ClientQuestInfo { Index = 34, Name = "重复", Group = "", Type = QuestType.Repeatable } });
                    await Send(new S.CompleteQuest { CompletedQuests = [30, 32, 33] });
                    await Send(new S.CompleteQuest { CompletedQuests = [] });
                    await Send(new S.ChangeQuest { Quest = new ClientQuestProgress { Id = 34 }, QuestState = QuestState.Remove });
                    await Send(new S.GroupInvite { Name = "结算回执屏障" });
                    check(await Read()==(short)ClientPacketIds.GroupInvite && finished.Count == 0 && accepted.SetEquals([31, 32]),
                        "长在线跨天未恢复领奖日常，或清掉了在途任务与非日常保护");
                }
            }
        }
        finally{await session.Stop();}
        Task Send(Packet packet)=>stream.WriteAsync(packet.GetPacketBytes().ToArray(),timeout.Token).AsTask();
        async Task<short?> Read()
        {
            while(true)
            {
                var header=new byte[4];try{await stream.ReadExactlyAsync(header,timeout.Token);}catch(EndOfStreamException){return null;}
                var body=new byte[BitConverter.ToUInt16(header)-4];await stream.ReadExactlyAsync(body,timeout.Token);
                var id=BitConverter.ToInt16(header,2);if(id!=(short)ClientPacketIds.KeepAlive)return id;
            }
        }
    }
}
