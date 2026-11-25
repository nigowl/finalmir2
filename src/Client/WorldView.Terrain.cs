using Godot;
using Client.MirObjects;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class WorldView
{
    private readonly Queue<Point> terrainPrefetch = new();
    private Point? prefetchCentre;
    private readonly Queue<Point> terrainWarmup = new();
    private bool initialTerrainReady, warmupStarted;
    internal int PrefetchedCells { get; private set; }
    internal int PrefetchQueueCount => terrainPrefetch.Count;

    private SpriteImage? TerrainSprite(CellInfo cell, bool front, long now, out bool floor)
    {
        var library = MapLibrary(front ? cell.FrontIndex : cell.MiddleIndex);
        var index = front ? (cell.FrontImage & 0x7fff) - 1 : cell.MiddleImage - 1;
        var first = Assets.Sprite(library, index);
        // 层次由原始地表尺寸决定，动画和裁边不能把地砖突然变成人物前景。
        floor = first != null && IsFloor(first);
        if (first == null) return null;
        if (front && State.OpenDoors.Contains(cell.DoorIndex)) index += cell.DoorOffset;
        var frames = front ? cell.FrontAnimationFrame & 0x7f : cell.MiddleAnimationFrame is > 0 and < 255
            ? (cell.MiddleAnimationFrame & 0x0f) > 0 ? cell.MiddleAnimationFrame & 0x0f : cell.MiddleAnimationFrame : 0;
        if (frames == 0) return Assets.Sprite(library, index) ?? first;
        var tick = front ? cell.FrontAnimationTick : cell.MiddleAnimationTick;
        return Assets.Sprite(library, index + (int)(now / (100 * (1 + tick)) % frames)) ?? first;
    }

    private void ResetTerrainPrefetch()
    {
        terrainPrefetch.Clear(); terrainWarmup.Clear(); prefetchCentre = null; PrefetchedCells = 0; initialTerrainReady = warmupStarted = false;
    }

    private void PrefetchTerrain()
    {
        if (Map == null || State.User == null || loadingMap) return;
        if (!initialTerrainReady)
        {
            if (!warmupStarted)
            {
                warmupStarted = true;
                var visible = MapViewBounds.Around(State.User.Location, Map.Width, Map.Height);
                for (var y = visible.FromY; y <= visible.ObjectToY; y++)
                for (var x = visible.FromX; x <= visible.ToX; x++) terrainWarmup.Enqueue(new Point(x, y));
            }
            for (var budget = 0; budget < 48 && terrainWarmup.TryDequeue(out var point); budget++)
                if (!PrepareTerrainCell(point)) terrainWarmup.Enqueue(point);
            if (terrainWarmup.Count > 0) return;
            initialTerrainReady = true;
        }
        var centre = State.User.Location;
        if (prefetchCentre != centre)
        {
            prefetchCentre = centre; terrainPrefetch.Clear();
            // 可见范围外再备好六格，按距离加载；队列仅保留当前位置周围一圈。
            var bounds = MapViewBounds.Around(centre, Map.Width, Map.Height);
            var margin = MapViewBounds.PrefetchMargin;
            var left = Math.Max(0, bounds.FromX - margin); var right = Math.Min(Map.Width - 1, bounds.ToX + margin);
            var top = Math.Max(0, bounds.FromY - margin); var bottom = Math.Min(Map.Height - 1, bounds.ObjectToY + margin);
            var cells = from y in Enumerable.Range(top, bottom - top + 1)
                        from x in Enumerable.Range(left, right - left + 1)
                        where x < bounds.FromX || x > bounds.ToX || y < bounds.FromY || y > bounds.ObjectToY
                        orderby Math.Max(Math.Abs(x - centre.X), Math.Abs(y - centre.Y))
                        select new Point(x, y);
            foreach (var point in cells) terrainPrefetch.Enqueue(point);
        }
        // 先让当前画面和人物动作完成，分帧预加载，避免后台地块挤占下载并发。
        if (Assets.PendingDownloads > 0) return;
        for (var budget = 0; budget < 12 && terrainPrefetch.TryDequeue(out var point); budget++)
        {
            if (PrepareTerrainCell(point)) PrefetchedCells++; else terrainPrefetch.Enqueue(point);
        }
    }

    private bool PrepareTerrainCell(Point point)
    {
        var cell = Map!.MapCells[point.X, point.Y]; var ready = true;
        if (point.X % 2 == 0 && point.Y % 2 == 0) ready &= Assets.PrepareSprite(MapLibrary(cell.BackIndex), (cell.BackImage & 0x1FFFFFFF) - 1);
        var middleCount = cell.MiddleAnimationFrame is > 0 and < 255 ? (cell.MiddleAnimationFrame & 0x0f) > 0 ? cell.MiddleAnimationFrame & 0x0f : cell.MiddleAnimationFrame : 1;
        for (var frame = 0; frame < middleCount; frame++) ready &= Assets.PrepareSprite(MapLibrary(cell.MiddleIndex), cell.MiddleImage - 1 + frame);
        for (var frame = 0; frame < Math.Max(1, cell.FrontAnimationFrame & 0x7f); frame++)
        {
            var index = (cell.FrontImage & 0x7fff) - 1 + frame;
            ready &= Assets.PrepareSprite(MapLibrary(cell.FrontIndex), index);
            // 两种门图一起预备，开门回执不能造成临时空白。
            if (cell.DoorIndex != 0 && (cell.FrontImage & 0x7fff) > 0)
                ready &= Assets.PrepareSprite(MapLibrary(cell.FrontIndex), index + cell.DoorOffset);
        }
        return ready;
    }
}
