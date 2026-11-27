using System.Drawing;
using Crystal.Native;
using C = ClientPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private readonly ClientAutomation protection = new();
    private readonly ClientPreferences preferences = new() { HpPercent = 65, MpPercent = 35, TownPercent = 35 };
    private long restUntil, lastProgress, nextWander;
    private uint lastGold;
    private long lastExperience;
    private uint monsterAttacker;
    private uint combatTarget;
    private readonly CompanionCombatProgress combatProgress = new();
    private long monsterAttackedAt;
    private readonly Dictionary<uint,long> monsterAttackers=[];
    private bool Funding => Knowledge?.Goal?.Key.StartsWith("fund/", StringComparison.Ordinal) == true;
    private async Task Think(long now)
    {
        var user = state.User!; nextAction = now + 250;
        if (pendingAsset != null)
        {
            if (now - assetSent > 5000) throw new TimeoutException($"{AssetName(pendingAsset)}回执未知（物品/任务编号 {assetId}），已停止；重新登录核对后才能继续，不会自动重发。");
            return;
        }
        if (protection.Paused) throw new InvalidOperationException(protection.Notice);
        // 死亡预算先于正常轮换，不能用会话到期绕过停用保护。
        if(CompanionSafety.DeathBudgetReached(state,deaths,Profile.DeathLimit))
            throw new InvalidOperationException("达到本次会话死亡预算，请调整目标或配置后重新启动。");
        if(retiring)
        {
            await Retire(now);return;
        }
        if (state.Me?.Dead == true)
        {
            CloseStorage();
            await connection.Send(new C.TownRevive()); nextAction = now + 5000; await Status("恢复中", "等待原回城复活"); return;
        }
        if(state.StorageActive&&(!storagePage||Paused||user.HP*100L<=Math.Max(1,state.CalculateStats()[Stat.HP])*65||now-monsterAttackedAt<3000))CloseStorage();
        if(await Protect(now))return;
        if (Paused) { await Status("已暂停", "保持连接和保命，暂停发展决策"); return; }
        if (moving)
        {
            if (now - moveSent > 3000) throw new TimeoutException("移动回执超时，已停止旧路线。");
            return;
        }
        if(map==null||mapName!=state.Map?.FileName)
        {
            mapLoad??=LoadMap();
            if(!mapLoad.IsCompleted){await Status("加载中","按需校验当前地图，继续接收游戏状态");return;}
            await mapLoad;mapLoad=null;if(map==null)return;
        }
        if(Knowledge==null){await Status("准备中","等待当前版本服务器的目标信息");return;}
        if (lastProgress == 0 || user.Gold != lastGold || user.Experience != lastExperience)
        { lastProgress = now; lastGold = user.Gold; lastExperience = user.Experience; }
        if (now - lastProgress > (5+Profile.Traits.Persistence/10) * 60_000)
        {
            await Status("待规划", "长时间没有有效进度，暂缓行动并重新查询任务、掉落和刷新点；角色保持在线");
            lastProgress = now; nextAction = now + 15000; return;
        }
        if(storagePage){if(await UseStorage(now))return;}
        if (preparedSkill==null && pendingSkill==Spell.None && await ImproveEquipment()) return;
        var stats = state.CalculateStats();
        var low = CompanionSafety.LowHealth(user.HP,stats[Stat.HP],Profile.Traits.Risk);
        // 低血或围攻先尝试原撤退，普通补货不能驻足压过保命。
        if (!low && !Crowded(now) && Knowledge.Goal?.Key == "public-supply" && preparedSkill == null && pendingSkill == Spell.None && await PublicServices(now)) return;
        // 在威胁接近前开盾，已经承伤或低血时不驻足抢占原撤离。
        if(!low&&now-monsterAttackedAt>=5000&&now>=aggressorUntil&&!skillsPaused&&preparedSkill==null&&pendingSkill==Spell.None&&now>=nextSkill&&
            CompanionSafety.NeedsTravelShield(state,Knowledge,preferences,now))
        {await SendSkill(Spell.MagicShield,state.Me!,true,now);await Status("护盾中","赶路前使用已学魔法盾防护附近危险怪物");return;}
        if(Knowledge.Goal is {Kind:"hunt",Key.Length:>0} pressured && !combatProgress.WaitingForPlan(pressured.Key,now) &&
            now-monsterAttackedAt<5000 && state.Actors.GetValueOrDefault(monsterAttacker) is {Dead:false} attacker &&
            pressured.MonsterIds?.Contains(MonsterId(attacker))==true &&
            (user.HP*100L<=stats[Stat.HP]*65L || Crowded(now)))
            await DeclineCombat(pressured,MonsterId(attacker),"当前目标实际承伤或围攻超出持续作战能力，降级后再积累装备与补给");
        if(await Defend(now,stats))return;
        if(await PrepareSkill(now))return;
        if(await AwaitSkill(now))return;
        if (await PublicServices(now)) return;
        if (await UseTownOnRoute(now)) return;
        if (!low && await MaintainHero(now)) return;
        if (combatProgress.WaitingForPlan(Knowledge.Goal?.Key,now))
        { await Status("等待目标", "等待服务端排除当前目标并重新规划"); return; }
        if (await TasksAndSupplies(now)) return;
        // 赶路只顺手捡脚下物，等待筹资时可取近物，不能让服务/等待永久饿死拾取。
        if (!low && await CollectLoot(now, Knowledge.Goal?.Kind == "wait" ? 4 : 0)) return;
        if (Knowledge.Goal?.Kind == "service" && await FollowObjective(now)) return;
        if (!low && Knowledge.Goal?.Kind is "guild-create" or "guild-register" && await FollowObjective(now)) return;
        if(!Funding&&!low&&await InviteParty(now))return;
        if (Knowledge.Goal?.Kind == "wait")
        { await Status("待规划", Knowledge.Goal.Reason); nextAction = now + 3000; return; }
        if (await GuildActivity(now)) return;
        if (now < restUntil) { await Status("休息中", "人格偏好短目标，稍后继续"); nextAction = Math.Min(restUntil, now + 1000); return; }
        if (low)
        {
            await Status("休息中", "等待恢复或补给，避免连续送死"); nextAction = now + 1500; return;
        }
        if (Knowledge.Goal?.Kind is "accept" or "turn-in" && await FollowObjective(now)) return;
        if(!Funding && await PartyAndPk(now))return;
        // 跨图任务途中不捡无关掉落，避免尚未抵达就再次超重返城；实际遇袭已在上方处理。
        if(Knowledge.Goal is {Kind:"hunt"} journey&&journey.MapId!=Knowledge.MapId&&await FollowObjective(now))return;
        await SuggestGoal(now);
        if(goalContext.Length>0&&now<goalDeadline&&GoalDecision(now).Length>0){nextAction=now+250;return;}
        if(goalHint=="explore"&&await ExploreExit(now)){await UsedGoal("explore");return;}
        if(Profile.Goal=="探索优先"&&!Funding&&await ExploreExit(now))return;
        if(Funding&&await CollectLoot(now))return;
        if (Knowledge.Goal?.QuestId is > 0 && Knowledge.Quests.Any(quest => quest.Id == Knowledge.Goal.QuestId && quest.ItemIds.Length > 0) && await CollectLoot(now)) return;
        if (!Funding && await CollectHarvest(now)) return;
        var targets = CombatCandidates().OrderBy(a=>a.Id==combatTarget?0:1).ThenBy(a => Distance(a.Location,user.Location) - TaskPriority(a)*(Profile.Goal=="任务优先"?2:1) -
            ((Profile.Goal=="练级优先"||goalHint=="hunt")?Math.Log2(1+(Knowledge.Enemies?.FirstOrDefault(enemy=>enemy.ObjectId==a.Id)?.Experience??0)):0)).ToArray();
        // 一轮战斗结束先收近处战利品，持续刷怪也不丢下金币药品；不打断仍存活的交战目标。
        if (!targets.Any(a => a.Id == combatTarget) && await CollectLoot(now, 4)) return;
        if((Profile.Goal=="装备优先"||Profile.Traits.Collection>=75)&&!targets.Any(a=>Distance(a.Location,user.Location)<=2)&&await CollectLoot(now))return;
        if (targets.FirstOrDefault() is { } target && Knowledge?.NoFight != true)
        {
            await Status("打怪中", "选择可见目标：" + target.Name);
            if (await Attack(target, now, stats)) {await UsedGoal("hunt");return;}
        }
        if(await CollectLoot(now))return;
        if(await FollowObjective(now))return;
        if(await ExploreExit(now))return;
        if (Profile.Traits.Diligence < 40 && random.Next(100) > Profile.Traits.Diligence)
        { restUntil = now + 3000 + random.Next(7000); return; }
        if (now >= nextWander)
        {
            nextWander = now + 2000;
            await Status("待目标", "服务器未找到适合当前等级的可达怪物或可接任务，稍后重新查询");
            nextAction = now + 3000;
        }
    }
    private IEnumerable<Crystal.Native.Actor> CombatCandidates() => Knowledge==null||state.User==null ? [] : ClientAutomation.Targets(state).Where(a=>
    {
        if(Knowledge.Goal?.Kind=="party"&&Knowledge.Party?.Targets.Contains(a.Id)!=true)return false;
        var known=Knowledge.Enemies?.FirstOrDefault(enemy=>enemy.ObjectId==a.Id);
        var level=known?.Level??Knowledge.Spawns.FirstOrDefault(spawn=>spawn.Name==a.Name)?.Level;
        var wanted=Knowledge.Goal?.MonsterIds;
        if(wanted?.Length>0&&!wanted.Contains(MonsterId(a))&&(Funding||Distance(a.Location,state.User.Location)>1))return false;
        return known?.Suitable??(level==null?Profile.Traits.Risk>=50:level<=state.User.Level+2+Profile.Traits.Risk/20);
    });
    private async Task<bool> Attack(Actor target, long now, Stats stats)
    {
        var user=state.User!;
        combatTarget=target.Id;
        var range = ClientAutomation.RequiresPhysicalAttack(target) ? 1 :
            Client.MirObjects.OriginalPlayerArt.HasBow(user.Class,state.Me!.Weapon) ? Globals.MaxAttackRange :
            ClassSkillRules.AttackMagic(state,preferences,target,now)?.Range ?? 1;
        if (target.Kind==ObjectType.Monster && Knowledge?.Goal is {Kind:"hunt",Key.Length:>0} goal &&
            goal.MonsterIds?.Contains(MonsterId(target))==true && combatProgress.TooHard(target.Id,target.Health,now,Distance(user.Location,target.Location)>range))
        {
            await DeclineCombat(goal,MonsterId(target),"当前目标久攻无效或击杀过慢，暂时改选更容易的怪物");return true;
        }
        if (await (target.Kind==ObjectType.Player?CastAttack(target,now):ClassSkills(target,now))) return true;
        var direction=Direction(user.Location,target.Location);
        if(!ClientAutomation.RequiresPhysicalAttack(target) && Client.MirObjects.OriginalPlayerArt.HasBow(user.Class,state.Me!.Weapon) && Distance(user.Location,target.Location)<=Globals.MaxAttackRange)
        {
            await connection.Send(new C.RangeAttack{Direction=direction,Location=user.Location,TargetID=target.Id,TargetLocation=target.Location});
            nextAction=now+Math.Max(750,1600-Math.Min(370,user.Level*14)-stats[Stat.AttackSpeed]*60);return true;
        }
        var thrusting=ClassSkillRules.Thrusting(state,preferences,target,true,now);
        if (!thrusting && Distance(user.Location,target.Location)>1) return await MoveTo(target.Location,1,avoidThreats:Funding);
        var spell=ClassSkillRules.Melee(state,direction,thrusting?Spell.Thrusting:Spell.None,preferences.SmartBattle);
        await connection.Send(new C.Attack { Direction=direction, Spell=spell });
        if(spell is Spell.Slaying or Spell.FlamingSword or Spell.TwinDrakeBlade)state.ActiveSkills.Remove(spell);
        if(spell==Spell.FlamingSword && user.Magics.FirstOrDefault(m=>m.Spell==spell) is {} used)used.CastTime=now;
        nextAction=now+Math.Max(700,1400-user.Level*14-stats[Stat.AttackSpeed]*60);return true;
    }
    private async Task<bool> CollectLoot(long now, int maxDistance = Globals.DataRange)
    {
        var user=state.User!;
        var loot=state.Actors.Values.Where(a=>a.Kind==ObjectType.Item&&Knowledge?.LootTargets?.Contains(a.Id)==true && Distance(user.Location,a.Location)<=maxDistance)
            .OrderBy(a=>Funding&&a.Gold==0).ThenBy(a=>Distance(user.Location,a.Location)).FirstOrDefault();
        if(loot==null)return false;
        await Status("拾取中","拾取实际地面物品");
        if(Distance(user.Location,loot.Location)==0){await connection.Send(new C.PickUp());nextAction=now+900;return true;}
        return await MoveTo(loot.Location,avoidThreats:Funding || Knowledge?.Goal?.Kind is "service" or "wait");
    }
    private int TaskPriority(Crystal.Native.Actor actor) => Knowledge?.Quests.Where(q => state.Quests.ContainsKey(q.Id))
        .Any(q => q.MonsterIds.Contains(MonsterId(actor))) == true ? 20 : 0;
}
