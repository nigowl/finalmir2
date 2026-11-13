using System.Drawing;
using Mir2.Companion;

internal static class CompanionPathChecks
{
    public static void Run(Action<bool, string> check)
    {
        var calls = 0;
        bool Walkable(Point p)
        {
            calls++;
            return p.X >= 0 && p.Y >= 0 && p.X < 240 && p.Y < 240 && (p.X != 100 || p.Y > 200);
        }
        var path = new CompanionPath();
        var occupied = new HashSet<Point>();
        var origin = new Point(90, 100); var target = new Point(110, 100);
        var first = path.Next(origin, target, 0, Walkable, occupied);
        check(first != null, "陪玩未找到需要先远离目标的大范围绕行路线");
        calls = 0;
        var second = path.Next(first!.Value, target, 0, Walkable, occupied);
        check(second != null && calls <= 2, "陪玩每走一步都重新搜索整张地图，没有沿用已找到路线");
        occupied.Add(second!.Value);
        var changed = path.Next(first.Value, target, 0, Walkable, occupied);
        check(changed != null && changed != second, "缓存路线下一格被占用后没有重新规划");
        occupied.Clear(); path.Clear();
        var point = origin; var steps = 0;
        while (point != target && steps++ < 1000)
        {
            var next = path.Next(point, target, 0, Walkable, occupied);
            if (next == null || !Walkable(next.Value)) break;
            point = next.Value;
        }
        check(point == target && steps > 200, "陪玩在长障碍前兜圈，未走完实际绕行路线");
        path.Clear(); occupied.Add(target);
        var approach = path.Next(new(108, 100), target, 1, Walkable, occupied);
        check(approach != null && approach != target, "NPC占格时没有搜索附近交互格");
        check(path.Next(new(109, 100), target, 1, Walkable, occupied) == null, "已到交互范围仍继续走动");
        check(path.Next(new(90, 100), new(-10, -10), 0, Walkable, occupied) == null, "越界目的地没有拒绝");
        var retreat=CompanionSafety.Retreat(new(5,5),[new(5,6)],point=>point.X>=4&&point.X<=6&&point.Y>=4&&point.Y<=6,new HashSet<Point>{new(5,4)});
        check(retreat is { Y:4 }&&retreat!=new Point(5,4),"撤退没有避开占格并远离近身怪物");
        check(CompanionSafety.Retreat(new(5,5),[new(5,6)],point=>point.Y>=5,new HashSet<Point>())==null,"撤退受阻时反而向怪物绕行");
        check(CompanionSafety.Retreat(new(5,5),[new(5,6),new(5,4)],_=>true,new HashSet<Point>())==null,"被夹击时未同时考虑两侧威胁");
        check(CompanionSafety.Retreat(new(5,5),[],_=>true,new HashSet<Point>())==null,"没有威胁仍进行撤退");
    }
}
