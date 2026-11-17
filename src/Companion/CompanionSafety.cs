using System.Drawing;
using Crystal.Native;
using Mir2.Companions;

namespace Mir2.Companion;

public static class CompanionSafety
{
    public static bool LowHealth(int hp,int maximum,int risk) => hp*100L<=Math.Max(1,maximum)*(55L-Math.Clamp(risk,0,100)/10);

    public static bool CanReconnect(GameState state,Exception error,int deaths,int limit,bool pending,bool serverDisconnect) =>
        (error is IOException or TimeoutException) && error is not InvalidDataException && state.User!=null && state.Me?.Dead==false &&
        !pending && !serverDisconnect && !state.Trading && !state.StorageActive && deaths<limit;
    public static bool TownAllowed(CompanionKnowledge? knowledge) => knowledge is { NoTownTeleport:false, InSafeZone:false };
    public static bool DeathBudgetReached(GameState state,int deaths,int limit) => state.Me?.Dead==true&&deaths>=limit;

    public static bool TravelBeforeCombat(CompanionKnowledge knowledge,int attackerMonster,bool low)
    {
        var goal=knowledge.Goal;
        if(goal==null)return false;
        if(goal.Kind=="service" || goal.Kind=="hunt" && goal.Key.StartsWith("fund/",StringComparison.Ordinal))return true;
        return !low && (goal.Kind is "accept" or "turn-in" || goal.Kind=="hunt" &&
            (goal.MapId!=knowledge.MapId || goal.QuestId>0 && attackerMonster>0 &&
             goal.MonsterIds is {Length:>0} && !goal.MonsterIds.Contains(attackerMonster)));
    }

    public static bool CanRun(GameState state,long now,long lastMoved)
    {
        var user=state.User;var me=state.Me;
        if(user==null||me==null||me.Dead||me.Hidden||state.Trading||state.StorageActive||lastMoved<=0||now<lastMoved||now-lastMoved>1000)return false;
        if((me.Poison&(PoisonType.Slow|PoisonType.Paralysis|PoisonType.LRParalysis|PoisonType.Frozen))!=0)return false;
        // shortcut: 骑乘/疾风步保守步行，支持三格路线后再启用跑步。
        if(user.Equipment.ElementAtOrDefault((int)EquipmentSlot.Mount)!=null||state.UserBuffs.ContainsKey(BuffType.SwiftFeet))return false;
        var stats=state.CalculateStats();
        return stats[Stat.HP]>0&&user.HP*100L>stats[Stat.HP]*65L&&state.InventoryWeight<=stats[Stat.BagWeight];
    }

    public static ClientMagic? SelfHealing(GameState state, long now)
    {
        var user = state.User;
        if (user == null || state.Me?.Dead != false || state.Trading || state.StorageActive || user.HP <= 0) return null;
        var stats = state.CalculateStats();
        if (stats[Stat.HP] <= 0 || user.HP * 100L > stats[Stat.HP] * 80L) return null;
        return user.Magics.FirstOrDefault(magic =>
        {
            if (magic.Spell != Spell.Healing || state.DisabledSkills.Contains(magic.Spell) || now < magic.CastTime + Math.Max(5000, magic.Delay)) return false;
            var cost = magic.BaseCost + magic.LevelCost * magic.Level;
            if (stats[Stat.ManaPenaltyPercent] > 0) cost += cost * stats[Stat.ManaPenaltyPercent] / 100;
            return cost <= user.MP;
        });
    }

    public static Point? Retreat(Point origin, Point[] threats, Func<Point, bool> walkable, ISet<Point> occupied)
    {
        if (threats.Length == 0) return null;
        int Distance(Point point) => threats.Min(threat => Functions.MaxDistance(point, threat));
        int Exposure(Point point) => threats.Sum(threat => Functions.MaxDistance(point,threat) is <=1 ? 10 : Functions.MaxDistance(point,threat)==2 ? 1 : 0);
        // 只选择实际下一步，不能为了走到“远处安全点”先绕向怪物。
        return Enumerable.Range(0, 8).Select(direction => Functions.PointMove(origin, (MirDirection)direction, 1))
            .Where(point => walkable(point) && !occupied.Contains(point) && Distance(point)>=Distance(origin) &&
                (Distance(point)>Distance(origin)||Exposure(point)<Exposure(origin)))
            .OrderBy(Exposure).ThenByDescending(Distance).Select(point => (Point?)point).FirstOrDefault();
    }
}
