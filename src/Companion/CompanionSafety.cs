using System.Drawing;
using Crystal.Native;
using Mir2.Companions;

namespace Mir2.Companion;

public static class CompanionSafety
{
    public static bool TownAllowed(CompanionKnowledge? knowledge) => knowledge is { NoTownTeleport:false, InSafeZone:false };

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
        // 只选择实际下一步，不能为了走到“远处安全点”先绕向怪物。
        return Enumerable.Range(0, 8).Select(direction => Functions.PointMove(origin, (MirDirection)direction, 1))
            .Where(point => walkable(point) && !occupied.Contains(point) && Distance(point) > Distance(origin))
            .OrderByDescending(Distance).Select(point => (Point?)point).FirstOrDefault();
    }
}
