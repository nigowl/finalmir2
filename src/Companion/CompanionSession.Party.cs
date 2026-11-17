using Crystal.Native;
using C = ClientPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private uint aggressor,duel;
    private long aggressorUntil,duelUntil,nextInvite;
    private AttackMode attackMode=AttackMode.Peace;
    private async Task<bool> PartyAndPk(long now)
    {
        var user=state.User!;
        var pk=Knowledge?.AllowPk==true&&Knowledge.NoFight==false&&!Knowledge.InSafeZone&&Profile.PkMode!="avoid";
        Actor? opponent=null;var mode=AttackMode.Peace;
        if(pk&&Profile.PkMode=="duel"&&now<duelUntil){opponent=state.Actors.GetValueOrDefault(duel);mode=AttackMode.Group;}
        if(pk&&now<aggressorUntil){opponent=state.Actors.GetValueOrDefault(aggressor);mode=AttackMode.RedBrown;}
        if(opponent!=null&&!opponent.Dead&&!opponent.Hidden&&!state.GroupMembers.Contains(opponent.Name)&&Distance(user.Location,opponent.Location)<=7)
        {
            if(attackMode!=mode){await connection.Send(new C.ChangeAMode{Mode=mode});nextAction=now+400;return true;}
            await Status("对抗中",mode==AttackMode.RedBrown?"短时自卫，使用红褐模式":"已接受附近玩家的限时约战");
            if(await CastAttack(opponent,now))return true;
            if(Distance(user.Location,opponent.Location)<=1){await connection.Send(new C.Attack{Direction=Direction(user.Location,opponent.Location),Spell=Spell.None});nextAction=now+1200;return true;}
            return await MoveTo(opponent.Location,1);
        }
        if(attackMode!=AttackMode.Peace){await connection.Send(new C.ChangeAMode{Mode=AttackMode.Peace});nextAction=now+300;return true;}
        if(Profile.AcceptGroup&&state.GroupMembers.Count>1)
        {
            if(await HealParty(now))return true;
            var leader=state.GroupMembers[0];var actor=state.Actors.Values.FirstOrDefault(a=>a.Kind==ObjectType.Player&&a.Name==leader&&!a.Hidden&&!a.Dead);
            if(leader!=user.Name)
            {
                if(Knowledge?.Goal?.Kind=="party"&&(Knowledge.Goal.MapId!=Knowledge.MapId||actor==null||Distance(user.Location,actor.Location)>3))
                    return await FollowObjective(now);
            }
        }
        return false;
    }

    private async Task<bool> HealParty(long now)
    {
        var user=state.User!;
        var ally=state.Actors.Values.Where(a=>a.Kind==ObjectType.Player&&!a.Hidden&&!a.Dead&&a.Id!=user.ObjectID&&
            state.GroupMembers.Contains(a.Name)&&Distance(user.Location,a.Location)<=7&&
            a.Health<(Profile.Goal=="协助队友"||goalHint=="help"?85:Profile.Traits.Helpfulness>=60?70:45)).OrderBy(a=>a.Health).FirstOrDefault();
        var heal=user.Magics.FirstOrDefault(m=>m.Spell==Spell.Healing&&now>=m.CastTime+Math.Max(m.Delay,5000)&&ClassSkillRules.Ready(state,m,now));
        if(skillsPaused||ally==null||heal==null||!await SendSkill(heal.Spell,ally,true,now))return false;
        heal.CastTime=now;await Status("辅助中","治疗当前实际低血队友："+ally.Name);return true;
    }

    private async Task<bool> InviteParty(long now)
    {
        if(Profile.AcceptGroup&&Knowledge?.Party is {Invite.Length:>0} party&&now>=nextInvite&&
            (Knowledge.Goal==null||Knowledge.Goal.Kind is "hunt" or "party" or "wait"))
        {
            var target=state.Actors.Values.FirstOrDefault(a=>a.Kind==ObjectType.Player&&a.Name==party.Invite&&!a.Hidden&&!a.Dead&&Distance(state.User!.Location,a.Location)<=8);
            if(target==null)return false;
            nextInvite=now+30000+(100-Profile.Traits.Social)*900;
            await connection.Send(new C.AddMember{Name=target.Name});
            await report(new("party-invite",Profile.Id,Text:"向允许组队的陪玩发送原邀请："+target.Name));return true;
        }
        return false;
    }
}
