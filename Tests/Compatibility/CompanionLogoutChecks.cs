using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Mir2.Companion;
using Mir2.Companions;
using S = ServerPackets;

internal static class CompanionLogoutChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        await Scenario(check,false);
        await Scenario(check,true);
        await Scenario(check,false,true);
        await Scenario(check,false,true,true);
    }

    private static async Task Scenario(Action<bool,string> check,bool unknown,bool healing=false,bool attacked=false)
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var profile=new CompanionProfile{Id=Guid.NewGuid(),Name="LogoutAck",Class=0,Gender=0,Chat=false};
        var events=new ConcurrentQueue<WorkerEvent>();
        var start=new WorkerStart(profile,"AckAccount","AckPassword",true,"127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,
            "test","logout-check",CompanionProtocol.Fingerprint,"","","",1);
        var session=new CompanionSession(start,e=>{events.Enqueue(e);return Task.CompletedTask;});
        session.Control(new("retire"));session.Begin();
        using var peer=await listener.AcceptTcpClientAsync(timeout.Token);var stream=peer.GetStream();
        Task<short?>? pending=null;
        try
        {
            await Send(new S.Connected());check(await Read()==(short)ClientPacketIds.ClientVersion,"陪玩退出检查握手顺序错误");
            await Send(new S.ClientVersion{Result=1});check(await Read()==(short)ClientPacketIds.Login,"陪玩退出检查登录顺序错误");
            await Send(new S.LoginSuccess{Characters=[new SelectInfo{Index=1,Name=profile.Name,Class=MirClass.Warrior,Gender=MirGender.Male}]});
            check(await Read()==(short)ClientPacketIds.StartGame,"陪玩退出检查未启动原角色");
            await Send(new S.BaseStatsInfo{Stats=new BaseStats(MirClass.Warrior){Stats=[new BaseStat(Stat.HP){Base=1000}]}});
            await Send(new S.UserInformation{ObjectID=1,Name=profile.Name,HP=100,MP=100,Inventory=new UserItem[46],Equipment=new UserItem[14],QuestInventory=[],
                Magics=healing?[new ClientMagic{Name="治愈术",Spell=Spell.Healing,BaseCost=1,CastTime=-10000}]:[]});
            if(attacked)
            {
                await Send(new S.ObjectMonster{ObjectID=2,Name="退场近身威胁",Location=new(1,0)});
                await Send(new S.Struck{AttackerID=2});
            }
            check(await Read()==(short)ClientPacketIds.SwitchGroup,"陪玩退出检查缺少原组队设置");
            if(attacked)
            {
                check(await ReadLogout()==(short)ClientPacketIds.Magic,"近期受击退场被禁止自疗保命");
                await Send(new S.HealthChanged{HP=1000,MP=99});
            }
            check(await ReadLogout()==(short)ClientPacketIds.LogOut,healing?"安静退场仍主动施放治愈术，持续刷新原LogTime而无法退出":"退场没有发送原LogOut");
            pending=ReadLogout();session.Control(new("retire"));await Task.Delay(250,timeout.Token);
            check(!session.Finished&&!pending.IsCompleted,"退出回执到达前就关闭TCP或重复发送LogOut");
            if(!unknown)
            {
                var rejectedAt=Environment.TickCount64;await Send(new S.LogOutFailed());session.Control(new("retire"));
                check(await pending==(short)ClientPacketIds.LogOut,"原服明确拒绝退出后没有等待重试");
                check(Environment.TickCount64-rejectedAt>=750&&!session.Finished,"原服拒绝退出后立即断线或快速重复请求");
                pending=ReadLogout();await Send(new S.LogOutSuccess{Characters=[]});
            }
            while(!session.Finished)await Task.Delay(10,timeout.Token);
            check(await pending==null,"退场完成后仍发出新的退出请求");
            check(unknown?events.Any(e=>e.Kind=="failed"&&e.Reason.Contains("退出角色回执未知")):
                !events.Any(e=>e.Kind=="failed")&&events.Any(e=>e.Kind=="status"&&e.Reason.Contains("原服确认退出角色")),
                unknown?"退出超时被当作成功或没有明确报错":"正常退出没有原服确认，或被误报为故障");
        }
        finally
        {
            await session.Stop();timeout.Cancel();
            if(pending!=null)try{await pending;}catch(OperationCanceledException)when(timeout.IsCancellationRequested){}
        }
        Task Send(Packet packet)=>stream.WriteAsync(packet.GetPacketBytes().ToArray(),timeout.Token).AsTask();
        async Task<short?> Read()
        {
            var header=new byte[4];
            try{await stream.ReadExactlyAsync(header,timeout.Token);}
            catch(EndOfStreamException){return null;}
            var body=new byte[BitConverter.ToUInt16(header)-4];await stream.ReadExactlyAsync(body,timeout.Token);
            return BitConverter.ToInt16(header,2);
        }
        async Task<short?> ReadLogout()
        {
            short? id;
            do{id=await Read();}while(id==(short)ClientPacketIds.KeepAlive);
            return id;
        }
    }
}
