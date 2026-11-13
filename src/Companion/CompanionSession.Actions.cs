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
    private long monsterAttackedAt;
    private async Task Think(long now)
    {
        var user = state.User!; nextAction = now + 250;
        if (pendingAsset != null)
        {
            if (now - assetSent > 5000) throw new TimeoutException($"{AssetName(pendingAsset)}回执未知（物品/任务编号 {assetId}），已停止；重新登录核对后才能继续，不会自动重发。");
            return;
        }
        if (protection.Paused) throw new InvalidOperationException(protection.Notice);
        if (state.Me?.Dead == true)
        {
            if (deaths >= Profile.DeathLimit) throw new InvalidOperationException("达到本次会话死亡预算，请调整目标或配置后重新启动。");
            await connection.Send(new C.TownRevive()); nextAction = now + 5000; await Status("恢复中", "等待原回城复活"); return;
        }
        preferences.AutoHp=preferences.AutoMp=Knowledge?.NoDrug==false;
        preferences.AutoTown=CompanionSafety.TownAllowed(Knowledge);
        if (protection.NextItem(state, preferences, now) is { } potion)
        { await connection.Send(new C.UseItem { Grid = MirGridType.Inventory, UniqueID = potion.UniqueID }); return; }
        if (protection.PendingItem != 0) return;
        if (!moving && CompanionSafety.SelfHealing(state, now) is { } healing)
        {
            await connection.Send(new C.Magic { ObjectID=user.ObjectID, TargetID=user.ObjectID, Location=user.Location, Direction=user.Direction, Spell=healing.Spell });
            // 原治愈术持续回血，留出回血及全局施法间隔，不能每步重施。
            healing.CastTime=now; nextAction=now+1800;
            await Status("自疗中", "使用已学治愈术恢复生命"); return;
        }
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
        await SuggestGoal(now);
        if(Knowledge==null){await Status("准备中","等待当前版本服务器的目标信息");return;}
        if (lastProgress == 0 || user.Gold != lastGold || user.Experience != lastExperience)
        { lastProgress = now; lastGold = user.Gold; lastExperience = user.Experience; }
        if (now - lastProgress > (5+Profile.Traits.Persistence/10) * 60_000)
        {
            await Status("待规划", "长时间没有有效进度，暂缓行动并重新查询任务、掉落和刷新点；角色保持在线");
            lastProgress = now; nextAction = now + 15000; return;
        }
        if (await ImproveEquipment()) return;
        var stats = state.CalculateStats();
        var low = user.HP * 100L < Math.Max(1, stats[Stat.HP]) * (45 - Profile.Traits.Risk / 4);
        var attacker = now - monsterAttackedAt < 10000 ? state.Actors.GetValueOrDefault(monsterAttacker) : null;
        if (attacker is { Dead:false, Hidden:false } && Distance(user.Location,attacker.Location)<=3)
        {
            // 补给途中能走通就先离开，不能先打完每只追击怪才返城。
            if (Knowledge.Goal?.Kind=="service" && await FollowObjective(now, true)) return;
            if (low || Knowledge.Enemies?.FirstOrDefault(enemy=>enemy.ObjectId==attacker.Id)?.Suitable==false)
            {
                var threats=ClientAutomation.Targets(state).Where(actor=>Distance(user.Location,actor.Location)<=3).Select(actor=>actor.Location).ToArray();
                var occupied=state.Actors.Values.Where(actor=>actor.Id!=user.ObjectID&&!actor.Dead&&!actor.Hidden&&actor.Kind is not (ObjectType.Item or ObjectType.Spell)).Select(actor=>actor.Location).ToHashSet();
                foreach(var exit in Knowledge.Exits)occupied.Add(new(exit.X,exit.Y));
                var retreat=CompanionSafety.Retreat(user.Location,threats,Walkable,occupied);
                if(retreat!=null)
                {await Status("撤退中","正在承伤，优先脱离近身威胁");if(await MoveTo(retreat.Value))return;}
            }
            if (Distance(user.Location,attacker.Location)<=1 && Knowledge.NoFight!=true)
            {await Status("自卫中","优先反击正在攻击自己的怪物："+attacker.Name);if(await Attack(attacker,now,stats))return;}
        }
        if (await TasksAndSupplies(now)) return;
        if (Knowledge.Goal?.Kind == "service" && await FollowObjective(now)) return;
        if (now < restUntil) { await Status("休息中", "人格偏好短目标，稍后继续"); nextAction = Math.Min(restUntil, now + 1000); return; }
        if (low)
        {
            await Status("休息中", "等待恢复或补给，避免连续送死"); nextAction = now + 1500; return;
        }
        if (Knowledge.Goal?.Kind is "accept" or "turn-in" && await FollowObjective(now)) return;
        if(await PartyAndPk(now))return;
        // 跨图任务途中不捡无关掉落，避免尚未抵达就再次超重返城；实际遇袭已在上方处理。
        if(Knowledge.Goal is {Kind:"hunt"} journey&&journey.MapId!=Knowledge.MapId&&await FollowObjective(now))return;
        if(goalHint=="rest"){goalHint="";restUntil=now+5000;return;}
        if(Profile.Goal=="探索优先"&&await ExploreExit(now))return;
        if (Knowledge.Goal?.QuestId is > 0 && Knowledge.Quests.Any(quest => quest.Id == Knowledge.Goal.QuestId && quest.ItemIds.Length > 0) && await CollectLoot(now)) return;
        if (await CollectHarvest(now)) return;
        var targets = ClientAutomation.Targets(state).Where(a =>
        {
            var known = Knowledge.Enemies?.FirstOrDefault(enemy => enemy.ObjectId == a.Id);
            var level = known?.Level ?? Knowledge.Spawns.FirstOrDefault(spawn => spawn.Name == a.Name)?.Level;
            var wanted = Knowledge.Goal?.MonsterIds;
            if (wanted?.Length > 0 && !wanted.Contains(MonsterId(a)) && Distance(a.Location,user.Location)>1) return false;
            return known?.Suitable ?? (level == null ? Profile.Traits.Risk >= 50 : level <= user.Level + 2 + Profile.Traits.Risk / 20);
        }).OrderBy(a => Distance(a.Location,user.Location) - TaskPriority(a)*(goalHint=="quest"||Profile.Goal=="任务优先"?2:1) -
            ((Profile.Goal=="练级优先"||goalHint=="hunt")?Math.Log2(1+(Knowledge.Enemies?.FirstOrDefault(enemy=>enemy.ObjectId==a.Id)?.Experience??0)):0)).ToArray();
        if((Profile.Goal=="装备优先"||Profile.Traits.Collection>=75)&&!targets.Any(a=>Distance(a.Location,user.Location)<=2)&&await CollectLoot(now))return;
        if (targets.FirstOrDefault() is { } target && Knowledge?.NoFight != true)
        {
            await Status("打怪中", "选择可见目标：" + target.Name);
            if (await Attack(target, now, stats)) return;
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
    private async Task<bool> Attack(Actor target, long now, Stats stats)
    {
        var user=state.User!;
        if (await CastAttack(target, now)) return true;
        if (Distance(user.Location,target.Location)>1) return await MoveTo(target.Location,1);
        await connection.Send(new C.Attack { Direction=Direction(user.Location,target.Location), Spell=Spell.None });
        nextAction=now+Math.Max(700,1400-user.Level*14-stats[Stat.AttackSpeed]*60);return true;
    }
    private async Task<bool> CollectLoot(long now)
    {
        var user=state.User!;
        var loot=state.Actors.Values.Where(a=>a.Kind==ObjectType.Item&&Knowledge?.LootTargets?.Contains(a.Id)==true)
            .OrderBy(a=>Distance(user.Location,a.Location)).FirstOrDefault();
        if(loot==null)return false;
        await Status("拾取中","拾取实际地面物品");
        if(Distance(user.Location,loot.Location)==0){await connection.Send(new C.PickUp());nextAction=now+900;return true;}
        return await MoveTo(loot.Location);
    }
    private int TaskPriority(Crystal.Native.Actor actor) => Knowledge?.Quests.Where(q => state.Quests.ContainsKey(q.Id))
        .Any(q => q.MonsterIds.Contains(MonsterId(actor))) == true ? 20 : 0;
    private async Task<bool> CastAttack(Actor target, long now)
    {
        if(ClientAutomation.RequiresPhysicalAttack(target))return false;
        var spell = state.User!.Magics.Where(m => ClientAutomation.IsSingleSkill(m.Spell) && m.Spell != Spell.None && !ClientAutomation.IsAttackSkill(m.Spell) &&
            m.BaseCost + m.LevelCost*m.Level <= state.User.MP)
            .OrderBy(m => Math.Max(now,m.CastTime + Math.Max(1800,m.Delay))).FirstOrDefault();
        if (spell == null || Distance(state.User.Location,target.Location)>7) return false;
        // 冷却时保留射程，不能为了填补施法间隔主动贴脸；已贴身仍可普攻自卫。
        if(now < spell.CastTime + Math.Max(1800,spell.Delay))return Distance(state.User.Location,target.Location)>1;
        await connection.Send(new C.Magic { ObjectID=state.User.ObjectID, TargetID=target.Id, Location=target.Location, Direction=Direction(state.User.Location,target.Location), Spell=spell.Spell });
        spell.CastTime=now; nextAction=now+1800; return true;
    }
}
