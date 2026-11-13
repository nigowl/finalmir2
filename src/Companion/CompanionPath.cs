using System.Drawing;

namespace Mir2.Companion;

public sealed class CompanionPath
{
    private Point[] route = [];
    private Point destination;
    private int near, index;

    public void Clear() { route = []; index = 0; }

    public Point? Next(Point origin, Point target, int radius, Func<Point, bool> walkable, ISet<Point> occupied)
    {
        if (Distance(origin, target) <= radius) return null;
        while (index + 1 < route.Length && route[index + 1] == origin) index++;
        if (destination != target || near != radius || index >= route.Length || route[index] != origin ||
            index + 1 >= route.Length || !walkable(route[index + 1]) || occupied.Contains(route[index + 1]))
        {
            destination = target; near = radius; index = 0;
            route = Find(origin, target, radius, walkable, occupied);
        }
        return route.Length > index + 1 ? route[index + 1] : null;
    }

    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Point[] Find(Point origin, Point target, int radius, Func<Point, bool> walkable, ISet<Point> occupied)
    {
        if (radius == 0 && (!walkable(target) || occupied.Contains(target))) return [];
        var queue = new PriorityQueue<(Point Point, int Cost), int>();
        var costs = new Dictionary<Point, int> { [origin] = 0 };
        var previous = new Dictionary<Point, Point>();
        queue.Enqueue((origin, 0), 0);
        var expanded = 0;
        // 完整绕路后缓存，不能每步朝局部最近点重算；限制单次搜索的内存和 CPU。
        while (queue.TryDequeue(out var entry, out _) && expanded < 100000)
        {
            var point = entry.Point;
            if (costs[point] != entry.Cost) continue;
            expanded++;
            if (Distance(point, target) <= radius)
            {
                var result = new List<Point> { point };
                while (previous.TryGetValue(point, out point)) result.Add(point);
                result.Reverse(); return result.ToArray();
            }
            for (var direction = 0; direction < 8; direction++)
            {
                var next = Functions.PointMove(point, (MirDirection)direction, 1);
                if (!walkable(next) || occupied.Contains(next)) continue;
                var cost = entry.Cost + 1;
                if (costs.TryGetValue(next, out var old) && old <= cost) continue;
                costs[next] = cost; previous[next] = point;
                queue.Enqueue((next, cost), cost + Math.Max(0, Distance(next, target) - radius));
            }
        }
        return [];
    }
}
