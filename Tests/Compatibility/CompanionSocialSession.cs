using System.Diagnostics;
using Crystal.Native;
using C=ClientPackets;
using S=ServerPackets;

internal static class CompanionSocialSession
{
    public static async Task Run(string[] args)
    {
        var index=Array.IndexOf(args,"--companion-social");var port=int.Parse(args[index+1]);var botName=args[index+2];var signal=args[index+3];
        var sessionWatch=Stopwatch.StartNew();
        using var connection=new GameConnection();var state=new GameState();var packets=new List<Packet>();
        await connection.Connect("127.0.0.1",port);await Until(()=>packets.OfType<S.Connected>().Any());
        await connection.Send(new C.ClientVersion{VersionHash=[]});await Until(()=>packets.OfType<S.ClientVersion>().Any());
        await connection.Send(new C.NewAccount{AccountID="SocialTester",Password="Social123"});await Until(()=>packets.OfType<S.NewAccount>().Any());
        await connection.Send(new C.Login{AccountID="SocialTester",Password="Social123"});await Until(()=>packets.OfType<S.LoginSuccess>().Any());
        await connection.Send(new C.NewCharacter{Name="SocialTester",Class=MirClass.Warrior,Gender=MirGender.Male});await Until(()=>packets.OfType<S.NewCharacterSuccess>().Any());
        await connection.Send(new C.StartGame{CharacterIndex=packets.OfType<S.NewCharacterSuccess>().Single().CharInfo.Index});await Until(()=>state.User!=null);
        await Until(()=>state.Actors.Values.Any(a=>a.Name==botName&&!a.Hidden));
        var bot=state.Actors.Values.Single(a=>a.Name==botName);var botId=bot.Id;
        await connection.Send(new C.SwitchGroup{AllowGroup=true});await connection.Send(new C.AddMember{Name=botName});
        await Until(()=>state.GroupMembers.Contains(botName));
        for(var i=0;i<4;i++)
        {
            var location=state.User!.Location;
            for(var attempt=0;attempt<8&&state.User.Location==location;attempt++)
            {
                var direction=(MirDirection)(((int)MirDirection.Down+attempt)%8);
                var target=Functions.PointMove(location,direction,1);
                if(state.Actors.Values.Any(actor=>actor.Id!=state.User.ObjectID&&!actor.Dead&&actor.Kind!=ObjectType.Item&&actor.Location==target))continue;
                packets.RemoveAll(packet=>packet is S.UserLocation);
                await connection.Send(new C.Walk{Direction=direction});
                await Until(()=>packets.OfType<S.UserLocation>().Any());await Pump(700);
            }
            if(state.User.Location==location)throw new Exception("社交测试周边没有可通行的空格");
        }
        await Until(()=>state.Actors.TryGetValue(botId,out var actor)&&Functions.InRange(actor.Location,state.User!.Location,3));
        await connection.Send(new C.DelMember{Name=state.User!.Name});await Until(()=>state.GroupMembers.Count==0);
        packets.Clear();await connection.Send(new C.Chat{Message=botName+"，切磋"});await Pump(3500);
        if(packets.OfType<S.Struck>().Any(p=>p.AttackerID==botId))throw new Exception("PK 总开关关闭仍发生攻击");
        File.WriteAllText(signal,"group/chat/follow/pk-off");await Until(()=>File.Exists(signal+".continue"));
        await Pump(12000);packets.Clear();await connection.Send(new C.Chat{Message=botName+"，切磋"});
        await Until(()=>packets.OfType<S.Struck>().Any(p=>p.AttackerID==botId));
        if(state.Me?.Dead==true){await connection.Send(new C.TownRevive());await Until(()=>state.Me?.Dead==false);}
        await Pump((int)Math.Max(0,80000-sessionWatch.ElapsedMilliseconds));packets.Clear();
        await connection.Send(new C.Chat{Message="/"+botName+" 你好"});
        await Until(()=>packets.OfType<S.Chat>().Any(c=>c.Type==ChatType.WhisperIn&&c.Message.Contains("模型测试回复")));
        Console.WriteLine("PASS 真实 TCP 组队接受、规则聊天、跟随队长、PK 默认关闭/显式约战、LLM 私聊增强。");
        await connection.Send(new C.LogOut());
        async Task Pump(int milliseconds)
        {
            var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<milliseconds){Drain();await Task.Delay(30);}
        }
        void Drain()
        {
            while(connection.Received.TryDequeue(out var packet)){state.Apply(packet);packets.Add(packet);}
            if(connection.Errors.TryDequeue(out var error))throw new IOException(error);
            if(!connection.Connected)throw new IOException("社交测试连接已断开");
        }
        async Task Until(Func<bool> condition)
        {
            var watch=Stopwatch.StartNew();while(watch.Elapsed<TimeSpan.FromSeconds(25)){Drain();if(condition())return;await Task.Delay(30);}
            throw new TimeoutException("社交协议等待超时："+string.Join(',',packets.TakeLast(10).Select(p=>p.GetType().Name)));
        }
    }
}
