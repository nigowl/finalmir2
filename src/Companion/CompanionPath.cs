using System.Drawing;

namespace Mir2.Companion;

public sealed class CompanionPath
{
    private Point[] route = [];
    private Point destination;
    private int near, index;
    private string trackedGoal = "";

    public void Clear() { route = []; index = 0; trackedGoal = ""; }

    public Point? Next(Point origin, Point target, int radius, Func<Point, bool> walkable, ISet<Point> occupied, string goal = "", bool run = false)
    {
        if (Distance(origin, target) <= radius) return null;
        // 同一猎杀区域的最近怪物会跨墙跳换，先走完已选区域，避免每次刷新都折返。
        if (goal.Length > 0 && goal == trackedGoal && radius == near && route.Length > 0 && Distance(origin, destination) > radius)
            target = destination;
        trackedGoal = goal;
        if (index + 2 < route.Length && route[index + 2] == origin) index += 2;
        while (index + 1 < route.Length && route[index + 1] == origin) index++;
        if (destination != target || near != radius || index >= route.Length || route[index] != origin ||
            index + 1 >= route.Length || !walkable(route[index + 1]) || occupied.Contains(route[index + 1]))
        {
            destination = target; near = radius; index = 0;
            route = Find(origin, target, radius, walkable, occupied);
        }
        if (route.Length <= index + 1) return null;
        var first = route[index + 1];
        if (run && index + 2 < route.Length)
        {
            var second = route[index + 2];
            // 只合并缓存中连续同向的两格，不能跨弯角、交互终点或动态占格。
            if (second.X - first.X == first.X - origin.X && second.Y - first.Y == first.Y - origin.Y &&
                walkable(second) && !occupied.Contains(second)) return second;
        }
        return first;
    }

    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Point[] Find(Point origin, Point target, int radius, Func<Point, bool> walkable, ISet<Point> occupied)
    {
        if (radius == 0 && (!walkable(target) || occupied.Contains(target))) return [];
        var queue = new PriorityQueue<(Point Point, int Cost), (int Total, int Remaining)>();
        var costs = new Dictionary<Point, int> { [origin] = 0 };
        var previous = new Dictionary<Point, Point>();
        queue.Enqueue((origin, 0), (0, 0));
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
                // 总估值相同先接近目标，避免等长路线在大地图上大面积扩散；最短路估值和搜索上限不变。
                var remaining = Math.Max(0, Distance(next, target) - radius);
                queue.Enqueue((next, cost), (cost + remaining, remaining));
            }
        }
        return [];
    }
}
