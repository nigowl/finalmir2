using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CompanionPartyChecks
{
    public static void Run(Action<bool,string> check)
    {
        var world=Envir.Main;var characters=world.CharacterList;var maps=world.MapInfoList;var guilds=world.Guilds;
        try
        {
            var map=new Map(new MapInfo{Index=701});var remote=new Map(new MapInfo{Index=702});world.MapInfoList=[map.Info,remote.Info];world.Guilds=[];
            PlayerObject Player(int id)=>new(){Info=new CharacterInfo{Index=id,Name="队友"+id,Level=20},CurrentMap=map,CurrentLocation=new(5,5),AllowGroup=true};
            var first=Player(1);var second=Player(2);var third=Player(3);PlayerObject[] all=[first,second,third];world.CharacterList=all.Select(p=>{p.Info.Player=p;return p.Info;}).ToList();
            var records=all.Select(p=>new CompanionRecord{CharacterId=p.Info.Index,Desired="run",Profile=new(){Name=p.Name,Enabled=true,AcceptGroup=true}}).ToArray();
            KnownPartyPlan? Plan(PlayerObject p)=>CompanionParty.Read(p,records.Single(r=>r.CharacterId==p.Info.Index).Profile,records);
            var guild=new GuildObject(new GuildInfo(first,"组队测试"));first.MyGuild=third.MyGuild=guild;
            check(Plan(first)!.Invite==third.Name,"没有优先邀请同行会陪玩");
            check(Plan(third)!.Invite=="","单人队长没有按角色编号选举，可能互发交叉邀请");
            records[2].Profile.AcceptGroup=false;check(Plan(first)!.Invite==second.Name,"邀请了关闭组队的陪玩");records[2].Profile.AcceptGroup=true;
            records[2].Desired="pause";check(!Plan(first)!.Peers.Contains(third.Name),"暂停角色仍被纳入自动组队");records[2].Desired="run";
            records[2].Profile.Enabled=false;check(!Plan(first)!.Peers.Contains(third.Name),"停用角色仍被自动邀请");records[2].Profile.Enabled=true;
            map.Info.NoGroup=true;check(Plan(first)==null,"禁组队地图仍发送邀请");map.Info.NoGroup=false;
            first.GroupMembers=second.GroupMembers=[first,second];
            check(Plan(second)!.Goal?.Kind=="party"&&Plan(second)!.Leader==first.Name,"实际队伍没有生成跟随队长计划");
            first.CurrentMap=remote;check(Plan(second)!.Goal==null,"无公开路线仍规划跨图跟队");
            map.Info.Movements.Add(new MovementInfo{MapIndex=702,Source=new(10,10)});
            check(Plan(second)!.Goal?.NextExit?.DestinationMapId==702,"已有公开路线时未规划跨图跟队");
            remote.Info.RequiredGroup=true;remote.Info.RequiredGroupSize=3;check(Plan(second)!.Goal==null,"跨图跟队绕过原组队人数门槛");
            remote.Info.RequiredGroup=false;first.CurrentMap=map;
            first.GroupMembers=Enumerable.Repeat(first,Globals.MaxGroup).ToList();check(Plan(first)!.Invite=="","队满仍主动邀请");
            first.GroupMembers=null;first.Dead=true;check(Plan(first)==null,"死亡角色仍主动组队");
        }
        finally{world.CharacterList=characters;world.MapInfoList=maps;world.Guilds=guilds;}
    }
}
