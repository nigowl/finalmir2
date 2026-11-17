using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionParty
{
    public static KnownPartyPlan? Read(PlayerObject player,CompanionProfile profile,CompanionRecord[] records)
    {
        if(!profile.AcceptGroup || player.Dead || !player.AllowGroup || player.CurrentMap.Info.NoGroup)return null;
        var peers=records.Where(r=>r.Profile.Enabled&&r.Profile.AcceptGroup&&r.Desired=="run")
            .Select(r=>Envir.Main.CharacterList.FirstOrDefault(c=>c.Index==r.CharacterId)?.Player)
            .Where(p=>p!=null&&p!=player&&!p.Dead&&p.AllowGroup&&p.CurrentMap==player.CurrentMap&&
                !p.Hidden&&Functions.InRange(player.CurrentLocation,p.CurrentLocation,8)).Select(p=>p!).ToArray();
        var group=player.GroupMembers;var leader=group?.FirstOrDefault();var invite="";
        if((leader==player||leader==null&&!peers.Any(p=>p.GroupMembers==null&&p.Info.Index<player.Info.Index)) && (group?.Count??0)<Globals.MaxGroup)
            invite=peers.Where(p=>p.GroupMembers==null&&p.GroupInvitation==null&&(leader==player||player.Info.Index<p.Info.Index))
                .OrderByDescending(p=>player.MyGuild!=null&&p.MyGuild==player.MyGuild)
                .ThenByDescending(p=>!(group??[player]).Any(member=>member.Class==p.Class))
                .ThenBy(p=>Functions.MaxDistance(player.CurrentLocation,p.CurrentLocation)).ThenBy(p=>p.Info.Index)
                .FirstOrDefault()?.Name??"";
        KnownGoal? follow=null;uint[] targets=[];
        if(leader!=null&&leader!=player&&!leader.Dead&&leader.CurrentMap!=null)
        {
            var routes=CompanionPlanning.Routes(player.CurrentMap.Info.Index,Envir.Main.MapInfoList,group!.Count);
            if(routes.TryGetValue(leader.CurrentMap.Info.Index,out var exit))
                follow=new("party",leader.CurrentMap.Info.Index,leader.CurrentLocation.X,leader.CurrentLocation.Y,
                    "跟随队长协同打怪："+leader.Name,NextExit:exit,Key:$"party/{leader.Info.Index}");
            targets=Envir.Main.Objects.OfType<MonsterObject>().Where(m=>m.CurrentMap==player.CurrentMap&&!m.Dead&&!m.Hidden&&m.Master==null&&
                    Functions.InRange(m.CurrentLocation,player.CurrentLocation,Globals.DataRange)&&m.IsAttackTarget(player)&&
                    group.Any(member=>m.Target==member||m.EXPOwner==member)&&CompanionPlanning.CanHunt(player.Stats,player.Level,m.Info,profile.Traits.Risk))
                .OrderBy(m=>Functions.MaxDistance(m.CurrentLocation,leader.CurrentLocation)).ThenBy(m=>m.ObjectID).Take(32).Select(m=>m.ObjectID).ToArray();
        }
        return new(invite,peers.Select(p=>p.Name).Take(128).ToArray(),leader?.Name??"",follow,targets);
    }
}
