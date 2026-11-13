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
            var leader=state.GroupMembers[0];var actor=state.Actors.Values.FirstOrDefault(a=>a.Kind==ObjectType.Player&&a.Name==leader&&!a.Hidden&&!a.Dead);
            if(actor!=null&&leader!=user.Name)
            {
                var heal=user.Magics.FirstOrDefault(m=>m.Spell==Spell.Healing&&now>=m.CastTime+Math.Max(m.Delay,2000)&&m.BaseCost+m.LevelCost*m.Level<=user.MP);
                if(heal!=null&&Profile.Traits.Helpfulness>=60&&actor.Health<(Profile.Goal=="协助队友"||goalHint=="help"?85:70))
                {
                    await connection.Send(new C.Magic{ObjectID=user.ObjectID,TargetID=actor.Id,Location=actor.Location,Spell=heal.Spell,Direction=Direction(user.Location,actor.Location)});
                    heal.CastTime=now;nextAction=now+2000;await Status("辅助中","为当前可见队友治疗");return true;
                }
                if(Distance(user.Location,actor.Location)>3){await Status("跟队中","跟随当前可见队长");return await MoveTo(actor.Location,2);}
            }
        }
        if(Profile.AcceptGroup&&state.GroupMembers.Count==0&&Profile.Traits.Social>=70&&now>nextInvite)
        {
            nextInvite=now+120000;
            var player=state.Actors.Values.FirstOrDefault(a=>a.Kind==ObjectType.Player&&a.Id!=user.ObjectID&&!a.Hidden&&!a.Dead&&Distance(user.Location,a.Location)<=8);
            if(player!=null){await connection.Send(new C.AddMember{Name=player.Name});await Say("附近有一起发展的朋友吗？");}
        }
        return false;
    }
}
