using System.Diagnostics;
using Crystal.Native;
using C=ClientPackets;
using S=ServerPackets;

internal static class LlmValueSession
{
    public static async Task Run(string[] args)
    {
        var at=Array.IndexOf(args,"--llm-value-session");var port=int.Parse(args[at+1]);var name=args[at+2];
        using var connection=new GameConnection();var state=new GameState();var packets=new List<Packet>();
        await connection.Connect("127.0.0.1",port);await Until(()=>packets.OfType<S.Connected>().Any());
        await connection.Send(new C.ClientVersion{VersionHash=[]});await Until(()=>packets.OfType<S.ClientVersion>().Any());
        await connection.Send(new C.NewAccount{AccountID="LlmValuePlayer",Password="Value123"});
        await Until(()=>packets.OfType<S.NewAccount>().Any());
        if(packets.OfType<S.NewAccount>().Single().Result!=8)throw new Exception("隔离玩家注册失败");
        await connection.Send(new C.Login{AccountID="LlmValuePlayer",Password="Value123"});await Until(()=>packets.OfType<S.LoginSuccess>().Any());
        await connection.Send(new C.NewCharacter{Name="模型问答验证",Class=MirClass.Warrior,Gender=MirGender.Male});await Until(()=>packets.OfType<S.NewCharacterSuccess>().Any());
        await connection.Send(new C.StartGame{CharacterIndex=packets.OfType<S.NewCharacterSuccess>().Single().CharInfo.Index});await Until(()=>state.User!=null);
        await connection.Send(new C.Chat{Message="/"+name+" 当前应该怎样补给并完成任务？"});
        await Until(()=>packets.OfType<S.Chat>().Any(p=>p.Type==ChatType.WhisperIn&&p.Message.Contains("原TCP模型问答已收到",StringComparison.Ordinal)));
        await connection.Send(new C.LogOut());await Until(()=>packets.OfType<S.LogOutSuccess>().Any());
        Console.WriteLine("PASS 原TCP真人私聊：实际模型回复送达原聊天频道并正常退出。");
        async Task Until(Func<bool> done)
        {
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed<TimeSpan.FromSeconds(20))
            {
                while(connection.Received.TryDequeue(out var packet)){state.Apply(packet);packets.Add(packet);}
                if(done())return;
                if(connection.Errors.TryDequeue(out var error))throw new IOException(error);
                if(!connection.Connected)throw new IOException("隔离玩家连接结束");
                await Task.Delay(20);
            }
            throw new TimeoutException("原TCP模型问答等待超时");
        }
    }
}
