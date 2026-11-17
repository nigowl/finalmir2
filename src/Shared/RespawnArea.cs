using System.Drawing;

namespace Shared;

public static class RespawnArea
{
    // 引擎和地图预览共用中心修正，避免阻挡格上的范围显示与实际生成位置不同。
    public static Point? Center(Point location, int width, int height, Func<int, int, bool> walkable)
    {
        if (location.X < 0 || location.Y < 0 || location.X >= width || location.Y >= height) return null;
        if (walkable(location.X, location.Y)) return location;
        Point? center = null;
        long nearest = long.MaxValue;
        for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
            {
                if (!walkable(x, y)) continue;
                long dx = x - location.X, dy = y - location.Y;
                var distance = dx * dx + dy * dy;
                if (distance >= nearest) continue;
                nearest = distance; center = new(x, y);
            }
        return center;
    }

    public static bool Contains(Point center, Point point, int radius)
    {
        long dx = point.X - center.X, dy = point.Y - center.Y;
        return radius >= 0 && dx * dx + dy * dy <= (long)radius * radius;
    }
}
