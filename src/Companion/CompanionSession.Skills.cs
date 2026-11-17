using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private Spell pendingSkill;
    private uint skillTarget;
    private short skillPoison;
    private int skillPreviousHp, skillPreviousElements;
    private long skillSent, nextSkill;
    private bool skillSupport, skillsPaused;
    private (Spell Spell,uint Target,bool Support,long Started)? preparedSkill;

    private async Task<bool> PrepareSkill(long now)
    {
        if(preparedSkill is not {} request)return false;
        var target=state.Actors.GetValueOrDefault(request.Target);
        if(retiring||state.Trading||state.StorageActive||target==null||target.Dead)
        {preparedSkill=null;return false;}
        if(now-request.Started>12000)
        {preparedSkill=null;await PauseSkills("耗材准备超时，停止本次会话自动施法");return false;}
        var magic=state.User!.Magics.FirstOrDefault(m=>m.Spell==request.Spell);
        if(magic==null||!ClassSkillRules.Ready(state,magic,now))
        {preparedSkill=null;return false;}
        return await SendSkill(request.Spell,target,request.Support,now);
    }

    private async Task<bool> AwaitSkill(long now)
    {
        if(pendingSkill==Spell.None)return now<nextSkill;
        if(skillSupport && ClassSkillRules.Confirmed(state,pendingSkill,skillTarget,skillPoison,skillPreviousHp,skillPreviousElements) ||
            pendingSkill==Spell.Poisoning && (!state.Actors.TryGetValue(skillTarget,out var target)||target.Dead))pendingSkill=Spell.None;
        else if(now-skillSent>=6000)await PauseSkills("未确认生效，停止本次会话自动施法，保留药品保护和撤退");
        return pendingSkill!=Spell.None||now<nextSkill;
    }

    private async Task PauseSkills(string reason)
    {
        var spell=pendingSkill;pendingSkill=Spell.None;preparedSkill=null;skillsPaused=true;
        await report(new("diagnostic",Profile.Id,Reason:$"职业技能{spell.ToLocalizedString()}：{reason}"));
    }

    private async Task SkillPacket(Packet packet)
    {
        if(packet is S.Magic result && pendingSkill!=Spell.None && result.Spell==pendingSkill)
        {
            if(!result.Cast)await PauseSkills("原服拒绝，停止本次会话自动施法，不重发");
            else if(!skillSupport)pendingSkill=Spell.None;
        }
        if(packet is S.MapChanged or S.Death){pendingSkill=Spell.None;preparedSkill=null;}
    }

    private async Task<bool> ClassSkills(Actor target,long now)
    {
        if(await AwaitSkill(now))return true;
        if(await PrepareSkill(now))return true;
        if(skillsPaused||state.Trading||state.StorageActive||retiring)return false;
        var spell=ClassSkillRules.Next(state,preferences,target,true,now);
        if(spell!=Spell.None)
        {
            var receiver=spell==Spell.Poisoning?target:spell==Spell.PetEnhancer?ClassSkillRules.PetToEnhance(state):state.Me;
            if(receiver!=null)return await SendSkill(spell,receiver,true,now);
        }
        var attack=ClassSkillRules.AttackMagic(state,preferences,target,now);
        if(attack!=null)return await SendSkill(attack.Spell,target,false,now);
        return ClassSkillRules.WaitForRanged(state,preferences,target,now);
    }

    private async Task<bool> CastAttack(Actor target,long now)
    {
        if(await AwaitSkill(now))return true;
        if(await PrepareSkill(now))return true;
        if(skillsPaused||Distance(state.User!.Location,target.Location)>7)return false;
        // 对抗仍沿用原单体范围，不能把刷怪群攻/施毒策略扩展到玩家。
        var magic=state.User.Magics.Where(m=>ClientAutomation.IsSingleSkill(m.Spell) && m.Spell!=Spell.None && !ClientAutomation.IsAttackSkill(m.Spell) &&
            !state.DisabledSkills.Contains(m.Spell) && ClassSkillRules.ManaCost(state,m)<=state.User.MP && SkillMaterials.Missing(state,SkillMaterials.Required(state,m.Spell,target)).Length==0)
            .OrderBy(m=>Math.Max(now,m.CastTime+ClientSkillRules.Cooldown(m))).FirstOrDefault();
        if(magic==null)return false;
        if(!ClassSkillRules.Ready(state,magic,now))return Distance(state.User.Location,target.Location)>1;
        return await SendSkill(magic.Spell,target,false,now);
    }

    private async Task<bool> SendSkill(Spell spell,Actor target,bool support,long now)
    {
        var materials=SkillMaterials.Required(state,spell,target);
        if(!materials.All(material=>SkillMaterials.Equipped(state,material)))
        {
            var change=SkillMaterials.NextChange(state,materials);
            if(change is C.EquipItem equip && !refused.Contains(equip.UniqueID))
            {preparedSkill??=(spell,target.Id,support,now);await Asset(equip,"equip",equip.UniqueID);return true;}
            if(change is C.RemoveItem remove && !refused.Contains(remove.UniqueID))
            {preparedSkill??=(spell,target.Id,support,now);await Asset(remove,"remove",remove.UniqueID);return true;}
            skillsPaused=true;preparedSkill=null;
            await report(new("diagnostic",Profile.Id,Reason:"职业技能无法安全准备耗材，停止本次会话自动施法"));return false;
        }
        var user=state.User!;
        preparedSkill=null;
        pendingSkill=spell;skillTarget=target.Id;skillSupport=support;skillPoison=materials.FirstOrDefault().Shape;
        skillPreviousHp=spell==Spell.Healing&&target.Id!=user.ObjectID?target.Health:user.HP;skillPreviousElements=state.Me?.Elements.Count??0;skillSent=now;
        if(ClientSkillRules.IsTrigger(spell))await connection.Send(new C.SpellToggle{Spell=spell,CanUse=true});
        else await connection.Send(new C.Magic{ObjectID=user.ObjectID,TargetID=target.Id,Location=target.Location,
            Direction=Direction(user.Location,target.Location),Spell=spell});
        nextSkill=now+ClientSkillRules.GlobalDelay(spell);nextAction=nextSkill;
        return true;
    }
}
