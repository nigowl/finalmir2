using System.Drawing;

namespace Client.MirObjects;

public readonly record struct MapViewBounds(int FromX, int ToX, int FromY, int FloorToY, int ObjectToY)
{
    public const int HorizontalRadius = 16, VerticalRadius = 17, ObjectOverhang = 25, PrefetchMargin = 6;

    public static MapViewBounds Around(Point centre, int width, int height) => new(
        Math.Max(0, centre.X - HorizontalRadius), Math.Min(width - 1, centre.X + HorizontalRadius),
        Math.Max(0, centre.Y - VerticalRadius), Math.Min(height - 1, centre.Y + VerticalRadius + 5),
        Math.Min(height - 1, centre.Y + VerticalRadius + ObjectOverhang));
}
