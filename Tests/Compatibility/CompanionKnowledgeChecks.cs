using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Mir2.Companion;
using Mir2.Companions;
using S = ServerPackets;

internal static class CompanionKnowledgeChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var profile=new CompanionProfile{Id=Guid.NewGuid(),Name="MapKnowledge",AcceptGroup=false,Chat=false};
        var events=new ConcurrentQueue<WorkerEvent>();
        var start=new WorkerStart(profile,"MapAccount","MapPassword",true,"127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,
            "test","knowledge-check",CompanionProtocol.Fingerprint,"","","",1);
        var session=new CompanionSession(start,e=>{events.Enqueue(e);return Task.CompletedTask;}){Paused=true};
        session.Begin();
        using var peer=await listener.AcceptTcpClientAsync(timeout.Token);var stream=peer.GetStream();
        try
        {
            await Send(new S.Connected());check(await Read()==(short)ClientPacketIds.ClientVersion,"地图快照检查握手错误");
            await Send(new S.ClientVersion{Result=1});check(await Read()==(short)ClientPacketIds.Login,"地图快照检查登录错误");
            await Send(new S.LoginSuccess{Characters=[new SelectInfo{Index=1,Name=profile.Name}]});
            check(await Read()==(short)ClientPacketIds.StartGame,"地图快照检查没有进入原角色");
            await Send(new S.UserInformation{ObjectID=1,Name=profile.Name,HP=100,MP=100,
                Inventory=new UserItem[46],Equipment=new UserItem[14],QuestInventory=[],Magics=[]});
            check(await Read()==(short)ClientPacketIds.ItemPricesRequest,"陪玩未请求当前版本价格");
            check(await Read()==(short)ClientPacketIds.SwitchGroup,"地图快照检查缺少组队设置");
            var first=Knowledge(1);await Control(first,"first");var exposedBeforeMap=session.Knowledge!=null;
            await Barrier(new S.MapInformation{MapIndex=1,FileName="First"});
            check(ReferenceEquals(session.Knowledge,first),"首次地图包抹掉了已经送达的同图目标信息，相同快照被去重后无法自行恢复");
            check(!exposedBeforeMap,"尚未确认地图就激活了目标及地图权限");
            var second=Knowledge(2);await Control(second,"second");exposedBeforeMap=session.Knowledge!=null;
            await Barrier(new S.MapChanged{MapIndex=2,FileName="Second"});
            check(ReferenceEquals(session.Knowledge,second),"切图包抹掉了提前送达的新地图目标信息");
            check(!exposedBeforeMap,"新地图包到达前就启用了未来地图目标及药卷权限");
            await Control(first,"stale");check(session.Knowledge==null,"切图后迟到的旧地图目标仍可用于动作及药卷判断");
            await Barrier(new S.MapChanged{MapIndex=3,FileName="Third"});
            check(session.Knowledge==null,"没有匹配快照时沿用了其他地图信息");
            var third=Knowledge(3);await Control(third,"third");
            check(ReferenceEquals(session.Knowledge,third),"先收到切图包、后收到匹配快照时不能恢复");
            await Barrier(new S.MapChanged{MapIndex=3,FileName="Third"});
            check(ReferenceEquals(session.Knowledge,third),"同地图移动清掉了有效目标快照");
            await Barrier(new S.MapInformation{MapIndex=3,FileName="Third"});
            check(ReferenceEquals(session.Knowledge,third),"同地图完整信息重发清掉了有效快照");
            check(!events.Any(e=>e.Kind=="failed"),"地图快照次序检查触发执行组件故障");
        }
        finally{await session.Stop();}

        CompanionKnowledge Knowledge(int map)=>new("stable-"+map,map,false,false,false,[],[],[],[],
            Goal:new KnownGoal("wait",map,0,0,"等待本图目标",Key:"map/"+map),NoTownTeleport:false);
        Task Send(Packet packet)=>stream.WriteAsync(packet.GetPacketBytes().ToArray(),timeout.Token).AsTask();
        async Task Control(CompanionKnowledge knowledge,string version)
        {
            session.Control(new("knowledge",Knowledge:knowledge));
            session.Control(new("update",Profile:profile with{Version=version}));
            while(!events.Any(e=>e.Kind=="configured"&&e.Text==version))await Task.Delay(10,timeout.Token);
        }
        async Task Barrier(Packet map)
        {
            // 同一TCP流的拒绝组队回执证明前面的地图包已处理，不靠任意等待猜包序。
            await Send(map);await Send(new S.GroupInvite{Name="MapBarrier"});
            check(await Read()==(short)ClientPacketIds.GroupInvite,"地图包处理屏障没有收到原协议回执");
        }
        async Task<short> Read()
        {
            while(true)
            {
                var header=new byte[4];await stream.ReadExactlyAsync(header,timeout.Token);
                var body=new byte[BitConverter.ToUInt16(header)-4];await stream.ReadExactlyAsync(body,timeout.Token);
                var id=BitConverter.ToInt16(header,2);if(id!=(short)ClientPacketIds.KeepAlive)return id;
            }
        }
    }
}
