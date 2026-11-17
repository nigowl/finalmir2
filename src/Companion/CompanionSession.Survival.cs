using Crystal.Native;
using C = ClientPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private async Task<bool> ProtectItems(long now)
    {
        if (state.User==null || pendingAsset!=null || logoutSent!=0) return false;
        preferences.AutoHp=preferences.AutoMp=Knowledge?.NoDrug==false;
        preferences.AutoTown=CompanionSafety.TownAllowed(Knowledge);
        preferences.TownPercent=Crowded(now)?65:35;
        if (protection.NextItem(state, preferences, now) is { } potion)
        { await connection.Send(new C.UseItem { Grid = MirGridType.Inventory, UniqueID = potion.UniqueID }); return true; }
        if (protection.Paused) throw new InvalidOperationException(protection.Notice);
        return protection.PendingItem != 0;
    }

    private async Task<bool> Protect(long now)
    {
        if (await ProtectItems(now)) return true;
        // 原服每次施法都会刷新退出保护；安静退场不能反复自疗阻止自己下线。
        var withdrawing=now-monsterAttackedAt<5000 && (Crowded(now) || Knowledge is { } knowledge &&
            state.Actors.GetValueOrDefault(monsterAttacker) is { } attacker && CompanionSafety.TravelBeforeCombat(knowledge,MonsterId(attacker),
                CompanionSafety.LowHealth(state.User!.HP,state.CalculateStats()[Stat.HP],Profile.Traits.Risk)));
        var healingAllowed=(!retiring||now-monsterAttackedAt<Globals.LogDelay||now<aggressorUntil)&&!withdrawing;
        if (healingAllowed && !moving && !skillsPaused && pendingSkill==Spell.None && now>=nextSkill && CompanionSafety.SelfHealing(state, now) is { } healing)
        {
            await SendSkill(healing.Spell,state.Me!,true,now);
            // 原治愈术持续回血，留出回血及全局施法间隔，不能每步重施。
            healing.CastTime=now; nextAction=now+1800;
            await Status("自疗中", "使用已学治愈术恢复生命"); return true;
        }
        return false;
    }

    private async Task<bool> Defend(long now,Stats stats)
    {
        var knowledge=Knowledge;if(knowledge==null)return false;
        var user=state.User!;
        var low=CompanionSafety.LowHealth(user.HP,stats[Stat.HP],Profile.Traits.Risk);
        var attacker = now - monsterAttackedAt < 10000 ? state.Actors.GetValueOrDefault(monsterAttacker) : null;
        var focused=state.Actors.GetValueOrDefault(combatTarget);
        if(focused is {Dead:false,Hidden:false} && Distance(user.Location,focused.Location)<=1 &&
            now-monsterAttackers.GetValueOrDefault(focused.Id,long.MinValue/2)<10000 &&
            knowledge.Enemies?.FirstOrDefault(enemy=>enemy.ObjectId==focused.Id)?.Suitable==true)attacker=focused;
        var crowded=Crowded(now);
        if (attacker is { Dead:false, Hidden:false } && Distance(user.Location,attacker.Location)<=3)
        {
            // 任务赶路也先沿目标路线离开，不能在入口反复清理无关追击怪。
            if (!retiring && CompanionSafety.TravelBeforeCombat(knowledge,MonsterId(attacker),low))
            {
                if(await FollowObjective(now,true))return true;
                var goal=knowledge.Goal!;
                if(!low && knowledge.Enemies?.FirstOrDefault(enemy=>enemy.ObjectId==attacker.Id)?.Suitable==true &&
                    goal.MapId==knowledge.MapId && Distance(user.Location,new(goal.X,goal.Y))<=(goal.Kind=="hunt"?1:3))return false;
            }
            if (retiring || low || crowded || knowledge.Enemies?.FirstOrDefault(enemy=>enemy.ObjectId==attacker.Id)?.Suitable==false)
            {
                var threats=ClientAutomation.Targets(state).Where(actor=>Distance(user.Location,actor.Location)<=3).Select(actor=>actor.Location).ToArray();
                var occupied=state.Actors.Values.Where(actor=>actor.Id!=user.ObjectID&&!actor.Dead&&!actor.Hidden&&actor.Kind is not (ObjectType.Item or ObjectType.Spell)).Select(actor=>actor.Location).ToHashSet();
                foreach(var exit in knowledge.Exits)occupied.Add(new(exit.X,exit.Y));
                var retreat=CompanionSafety.Retreat(user.Location,threats,Walkable,occupied);
                if(retreat!=null)
                {await Status("撤退中","正在承伤，优先脱离近身威胁");if(await MoveTo(retreat.Value))return true;}
            }
            if (Distance(user.Location,attacker.Location)<=1 && knowledge.NoFight!=true)
            {await Status("自卫中","优先反击正在攻击自己的怪物："+attacker.Name);if(await Attack(attacker,now,stats))return true;}
        }
        return false;
    }

    private bool Crowded(long now) => state.User is { } user &&
        (monsterAttackers.Count(pair=>now-pair.Value<5000 && state.Actors.GetValueOrDefault(pair.Key) is {Dead:false,Hidden:false} threat &&
            Distance(user.Location,threat.Location)<=3)>=2 || now-monsterAttackedAt<5000 &&
            state.Actors.GetValueOrDefault(monsterAttacker) is {Dead:false,Hidden:false} attacker && Distance(user.Location,attacker.Location)<=1 &&
            ClientAutomation.Targets(state).Count(threat=>Distance(user.Location,threat.Location)<=2)>=2);
}
