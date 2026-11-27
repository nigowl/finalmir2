using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Crystal.Native;
using Mir2.Companion;
using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class CompanionGuildDecisionChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        Cohorts(check);
        check(CompanionLlm.GoalCandidates("{\"Candidates\":[\"accept-duel\",\"decline-duel\"],\"Observation\":\"明确切磋\"}",true).Length==2,"切磋合法候选被拒绝");
        check(CompanionLlm.GoalCandidates("{\"Candidates\":[\"accept-duel\",\"command\"],\"Observation\":\"越权\"}",true).Length==0,"切磋候选接受了任意指令");
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
        var profile=new CompanionProfile{Id=Guid.NewGuid(),Name="切磋抉择",PkMode="duel",LlmMode=2,Chat=true,Traits=new(){Aggression=95}};
        var events=new List<WorkerEvent>();var session=new CompanionSession(new(profile,"test","private",true,"127.0.0.1",port,"test","duel-run",CompanionProtocol.Fingerprint,"","","",1),e=>{events.Add(e);return Task.CompletedTask;});
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(CompanionSession);
        FieldInfo Field(string name)=>type.GetField(name,flags)!;
        Task Invoke(string method,params object[] args)=>(Task)type.GetMethod(method,flags)!.Invoke(session,args)!;
        var state=(GameState)Field("state").GetValue(session)!;var connection=(GameConnection)Field("connection").GetValue(session)!;
        await connection.Connect("127.0.0.1",port);using var peer=await listener.AcceptTcpClientAsync();var stream=peer.GetStream();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            state.Apply(new S.MapInformation{MapIndex=1});state.Apply(new S.BaseStatsInfo{Stats=new BaseStats(MirClass.Warrior){Stats=[new(Stat.HP){Base=100}]}});
            state.Apply(new S.UserInformation{ObjectID=1,Name=profile.Name,Level=30,HP=100,Inventory=new UserItem[80],Equipment=new UserItem[14],QuestInventory=[],Location=new(5,5)});
            state.Actors[2]=new(){Id=2,Name="明确挑战者",Kind=ObjectType.Player,Location=new(6,5)};
            var knowledge=new CompanionKnowledge("duel",1,false,true,true,[],[],[],[],AllowPk:true,NoHero:true);session.Knowledge=knowledge;
            var request=new S.ObjectChat{ObjectID=2,Type=ChatType.Normal,Text=profile.Name+"，切磋"};
            await Invoke("Social",request);var e=events.Single(x=>x.Kind=="llm-duel");
            check((uint)Field("duel").GetValue(session)! ==0&&!stream.DataAvailable,"模型决策返回前已经接受切磋");
            await Invoke("Social",request);check(events.Count(x=>x.Kind=="llm-duel")==1&&(uint)Field("duel").GetValue(session)! ==0,"重复挑战绕过在途抉择或重复收费");
            session.DuelResult("duel-run/"+e.Reason,"decline-duel");await Invoke("PendingDuel",Environment.TickCount64);
            check(await Read()==ClientPacketIds.Chat&&(uint)Field("duel").GetValue(session)! ==0,"模型拒绝仍接受切磋");
            await Invoke("Social",request);check((uint)Field("duel").GetValue(session)! ==0&&!stream.DataAvailable,"重复挑战绕过模型拒绝缓存");
            Field("nextDuelSuggestion").SetValue(session,0L);Field("nextChat").SetValue(session,0L);await Invoke("Social",request);e=events.Last(x=>x.Kind=="llm-duel");
            session.DuelResult("old-run/"+e.Reason,"accept-duel");check(!(bool)Field("duelAnswered").GetValue(session)!,"旧会话模型结果污染当前切磋");
            session.DuelResult("duel-run/"+e.Reason,"accept-duel");session.Knowledge=knowledge with{AllowPk=false};await Invoke("PendingDuel",Environment.TickCount64);
            check((uint)Field("duel").GetValue(session)! ==0&&!stream.DataAvailable,"关闭PK后仍采用迟到的接受建议");
            session.Knowledge=knowledge;Field("nextDuelSuggestion").SetValue(session,0L);await Invoke("Social",request);e=events.Last(x=>x.Kind=="llm-duel");
            session.DuelResult("duel-run/"+e.Reason,"accept-duel");await Invoke("PendingDuel",Environment.TickCount64);
            check(await Read()==ClientPacketIds.Chat&&(uint)Field("duel").GetValue(session)! ==2,"合法接受建议没有纳入原限时切磋");
            check((long)Field("duelUntil").GetValue(session)!<=Environment.TickCount64+30000,"模型扩大了切磋期限");
            Field("duel").SetValue(session,0u);Field("nextChat").SetValue(session,0L);profile.LlmMode=0;
            await Invoke("Social",request);check(await Read()==ClientPacketIds.Chat&&(uint)Field("duel").GetValue(session)! ==2,"关闭模型未恢复原人格规则");
            profile.LlmMode=2;Field("duel").SetValue(session,0u);Field("nextDuelSuggestion").SetValue(session,0L);await Invoke("Social",request);e=events.Last(x=>x.Kind=="llm-duel");
            type.GetMethod("InvalidateSuggestions",flags)!.Invoke(session,[]);session.DuelResult("duel-run/"+e.Reason,"accept-duel");await Invoke("PendingDuel",Environment.TickCount64);
            check((uint)Field("duel").GetValue(session)! ==0&&!stream.DataAvailable,"切图/队伍场景失效后仍采用旧切磋建议");
        }
        finally{await session.Stop();}
        async Task<ClientPacketIds> Read()
        {var header=new byte[4];await stream.ReadExactlyAsync(header,timeout.Token);var body=new byte[BitConverter.ToUInt16(header)-4];await stream.ReadExactlyAsync(body,timeout.Token);return (ClientPacketIds)BitConverter.ToInt16(header,2);}
    }
    private static void Cohorts(Action<bool,string> check)
    {
        var world=Envir.Main;var maps=world.MapInfoList;var loaded=world.MapList;var characters=world.CharacterList;var players=world.Players;var guilds=world.Guilds;var npcs=world.NPCs;var scripts=world.Scripts;var scriptIndex=world.ScriptIndex;var wars=world.Conquests;
        var costs=Server.Settings.Guild_CreationCostList;var level=Server.Settings.Guild_RequiredLevel;
        try
        {
            var map=new Map(new MapInfo{Index=910});world.MapInfoList=[map.Info];world.MapList=[map];world.CharacterList=[];world.Players=[];world.Guilds=[];world.NPCs=[];world.Scripts=[];
            Server.Settings.Guild_RequiredLevel=22;Server.Settings.Guild_CreationCostList=[new(){Amount=1000}];
            CompanionRecord Role(int id)
            {
                var account=new AccountInfo{Gold=5000};var info=new CharacterInfo{Index=id,Name="行会抉择"+id,Level=30,AccountInfo=account};
                var player=new PlayerObject{Info=info,Account=account,CurrentMap=map,HP=100,Stats=new Stats{[Stat.HP]=100}};
                player.Node=new LinkedListNode<MapObject>(player);info.Player=player;world.CharacterList.Add(info);world.Players.Add(player);
                return new(){CharacterId=id,Desired="run",Profile=new(){Id=Guid.Parse($"{id:x8}-0000-0000-0000-000000000000"),Name=info.Name,Enabled=true,Guilds=new(){Mode="auto"}}};
            }
            var a=Role(1);var b=Role(2);var c=Role(4);var records=new[]{a,b,c};
            var owner=world.CharacterList[0].Player;var guild=new GuildObject(new GuildInfo(owner,"现有第一行会"){GuildIndex=1});world.Guilds.Add(guild);owner.MyGuild=guild;owner.MyGuildRank=guild.Ranks[0];
            var npc=new NPCObject(new NPCInfo{Name="建会使者",FileName="GuildDecisionAbsent"}){CurrentMap=map};NPCScript.Get(npc.ScriptID).NPCPages=[new(NPCScript.GuildCreateKey)];
            KnownGuildPlan? Plan(CompanionRecord r,string preference="")=>CompanionGuildDevelopment.Read(world.CharacterList.Single(p=>p.Index==r.CharacterId).Player,r,records,null,true,preference);
            check(Plan(b)?.Action=="create","首个行会存在时，其他阵营仍全部入会而不再建会");
            check(Plan(c)?.Action=="join"&&Plan(c)?.Name==guild.Name,"同阵营没有正常申请入会");
            world.CharacterList[2].Player.EnableGuildInvite=true;
            check(CompanionGuildDevelopment.Read(owner,a,records,null,true)?.Action=="invite","会长没有招收可正常入会的同阵营陪玩");
            check(CompanionGuildDevelopment.Read(owner,a,records,null,true,preferences:new Dictionary<Guid,string>{{c.Profile.Id,"develop"}})?.Action!="invite","模型暂缓入会仍被会长反复邀请");
            check(Plan(b,"develop")?.Action=="develop","模型暂缓建会选择没有生效");
            check(Plan(b,"join:1")?.Action=="create","模型通过候选外行会破坏多阵营分配");
            owner.Dead=true;guild.Ranks[0].Members[0].Player=null;
            check(Plan(c)?.Action=="wait","本会会长离线/死亡导致同阵营不停新建重复行会");
            check(Plan(b)?.Action=="create","其他阵营会长离线阻断了独立建会");owner.Dead=false;guild.Ranks[0].Members[0].Player=owner;
            b.Profile.Guilds.Name=guild.Name;check(Plan(b)?.Action=="join","显式目标名称被自动分阵营覆盖");b.Profile.Guilds.Name="";
            b.Profile.Guilds.Mode="join";check(Plan(b)?.Action=="join","仅入会模式被强制创建其他行会");b.Profile.Guilds.Mode="auto";
            b.Profile.Guilds.Mode="off";check(Plan(b)==null,"关闭策略后仍规划建会");b.Profile.Guilds.Mode="auto";
            var candidate=world.CharacterList[1].Player;candidate.Account.Gold=999;
            check(Plan(b) is { Action:"prepare",Goal.Kind:"guild-fund" },"缺钱时没有开始真实筹资，或绕过建会预算直接建会");
            check(owner.MyGuild==guild&&candidate.MyGuild==null&&guild.Info.Membercount==1&&owner.Account.Gold==5000,"只读多行会计划修改了会籍或资产");
            var offline=Role(5);records=[a,b,c,offline];
            var other=world.CharacterList.Single(p=>p.Index==offline.CharacterId);other.AccountInfo.Gold=1000000;world.Players.Remove(other.Player);
            candidate.Account.Gold=5000;
            check(Plan(b)?.Action=="create","长期离线候选阻塞了当前在线同组会长筹建");
            var scheduled=Role(8);records=[a,b,c,offline,scheduled];
            var richer=world.CharacterList.Single(p=>p.Index==scheduled.CharacterId).Player;richer.Account.Gold=500000;
            scheduled.Profile.Scheduled=true;scheduled.Desired="off";
            check(Plan(b)?.Reason.Contains(scheduled.Profile.Name)==true&&Plan(scheduled)?.Action=="create","日程自动上线的富裕候选被当成手动关闭而跳过");
            scheduled.Desired="pause";check(Plan(b)?.Action=="create","人工暂停角色仍占用筹建资格");scheduled.Desired="off";
            scheduled.SuppressedUntil=DateTimeOffset.UtcNow.AddMinutes(1);check(Plan(b)?.Action=="create","人工下线抑制被日程上线绕过");scheduled.SuppressedUntil=null;
            scheduled.RestUntil=DateTimeOffset.UtcNow.AddMinutes(1);check(Plan(b)?.Action=="create","休息退场角色仍占用筹建资格");scheduled.RestUntil=null;
            scheduled.Error="资产回执未知";check(Plan(b)?.Action=="create","存在错误保护的日程候选仍占用筹建资格");scheduled.Error="";
            scheduled.SessionUntil=DateTimeOffset.UtcNow.AddSeconds(-1);check(Plan(b)?.Action=="create","到期退场角色仍占用筹建资格");scheduled.SessionUntil=null;
            scheduled.Profile.Scheduled=false;check(Plan(b)?.Action=="create","未开日程且未手动启动的角色仍占用筹建资格");scheduled.Profile.Scheduled=true;
            var own=new GuildObject(new GuildInfo(candidate,"日程攻城行会"){GuildIndex=2});world.Guilds.Add(own);
            candidate.MyGuild=own;candidate.MyGuildRank=own.Ranks[0];richer.MyGuild=own;richer.MyGuildRank=new GuildRank{Index=1};
            b.Profile.Scheduled=true;b.Desired="off";b.Profile.Guilds.Siege=true;scheduled.Profile.Guilds.Siege=true;
            var war=new ConquestObject(new ConquestGuildInfo{Info=new ConquestInfo{Index=2,Type=ConquestType.Request,Monday=true},Owner=0}){ConquestMap=map};world.Conquests=[war];
            NPCScript.Get(npc.ScriptID).NPCPages.Add(new(ConquestRegistration.Page));
            check(CompanionGuildSieges.Organize(candidate,records,null)?.Action=="register","日程上线的会长和成员无法组织攻城报名");
            scheduled.Desired="pause";check(CompanionGuildSieges.Organize(candidate,records,null)==null,"暂停成员被计入攻城报名人数");scheduled.Desired="off";
            b.SuppressedUntil=DateTimeOffset.UtcNow.AddMinutes(1);check(CompanionGuildSieges.Organize(candidate,records,null)==null,"人工下线的会长仍组织攻城");
        }
        finally{world.MapInfoList=maps;world.MapList=loaded;world.CharacterList=characters;world.Players=players;world.Guilds=guilds;world.NPCs=npcs;world.Scripts=scripts;world.ScriptIndex=scriptIndex;world.Conquests=wars;Server.Settings.Guild_CreationCostList=costs;Server.Settings.Guild_RequiredLevel=level;}
    }
}
