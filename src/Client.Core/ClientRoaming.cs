using System.Drawing;

namespace Crystal.Native;

public sealed class ClientRoaming
{
    private readonly List<Point> recent = new();
    private readonly Dictionary<Point, long> blockedUntil = new();
    private int mapIndex = int.MinValue, turn;

    public void Reset() { recent.Clear(); blockedUntil.Clear(); mapIndex = int.MinValue; turn = 0; }

    public void Observe(int map, Point location)
    {
        if (map != mapIndex) { Reset(); mapIndex = map; }
        var region = new Point(location.X / 16, location.Y / 16);
        if (recent.Count > 0 && recent[^1] == region) return;
        recent.Remove(region); recent.Add(region);
        if (recent.Count > 128) recent.RemoveAt(0);
    }

    public void Block(Point point, long now)
    {
        if (blockedUntil.Count >= 32) blockedUntil.Remove(blockedUntil.MinBy(pair => pair.Value).Key);
        blockedUntil[point] = now + 30000;
    }

    public Point? Next(int map, Point location, int width, int height, long now, Func<Point, bool> walkable, Func<Point, bool> reachable)
    {
        Observe(map, location);
        var candidates = new HashSet<Point>();
        for (var radius = 32; radius <= 96; radius += 32)
        for (var direction = 0; direction < 16; direction++)
        {
            var angle = (direction + turn) * Math.PI / 8;
            var point = new Point(Math.Clamp(location.X + (int)Math.Round(Math.Cos(angle) * radius), 0, width - 1),
                Math.Clamp(location.Y + (int)Math.Round(Math.Sin(angle) * radius), 0, height - 1));
            if (Functions.MaxDistance(point, location) < Math.Min(16, Math.Min(width, height) / 3) || point == location || !walkable(point)) continue;
            if (blockedUntil.GetValueOrDefault(point) > now) continue;
            candidates.Add(point);
        }
        // 优先远离近期实际走过的区域；目标持续保留到到达，不能每帧随机折返。
        double Score(Point point) => recent.Min(region => Math.Max(Math.Abs(point.X - region.X * 16 - 8), Math.Abs(point.Y - region.Y * 16 - 8)))
            + Functions.MaxDistance(location, point) * .15;
        foreach (var point in candidates.OrderByDescending(Score).ThenBy(point => (point.X + point.Y + turn) % 17).Take(8))
        {
            if (reachable(point)) { turn = (turn + 3) % 16; return point; }
            Block(point, now);
        }
        return null;
    }
}
