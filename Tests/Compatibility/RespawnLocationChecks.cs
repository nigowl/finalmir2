using System.Drawing;
using System.Runtime.CompilerServices;
using Server.MirDatabase;
using Server.MirEnvir;
using Shared;

internal static class RespawnLocationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.Width = map.Height = 5; map.Cells = new Cell[5, 5];
        for (var x = 0; x < 5; x++) for (var y = 0; y < 5; y++) map.Cells[x, y] = Cell.HighWall;
        map.Cells[1, 2] = map.Cells[4, 4] = new Cell { Attribute = CellAttribute.Walk };
        map.WalkableCells = [new(1, 2), new(4, 4)];
        var blocked = new RespawnInfo { Location = new(2, 2), Spread = 0 };
        check(map.GetRespawnCells(blocked).SequenceEqual(new[] { new Point(1, 2) }) && blocked.Location == new Point(2, 2), "Blocked spawn did not use the nearest walkable cell while preserving its stored coordinates");
        var normal = new RespawnInfo { Location = new(1, 2), Spread = 5 };
        check(map.GetRespawnCells(normal).SequenceEqual(map.WalkableCells), "Normal spawn spread lost its original candidate cells");
        blocked.Spread = 1;
        check(map.GetRespawnCells(blocked).All(point => Math.Abs(point.X - 1) <= 1 && Math.Abs(point.Y - 2) <= 1), "Spawn spread was not centered on the corrected point");
        check(map.GetRespawnCells(new RespawnInfo { Location = new(-1, 2) }).Count == 0, "Out-of-map spawn was silently moved into the map");
        map.WalkableCells.Clear();
        check(map.GetRespawnCells(blocked).Count == 0, "Fully blocked map produced an invalid spawn candidate");
        map.WalkableCells = null!;
        for (var x = 0; x < 5; x++) for (var y = 0; y < 5; y++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        var circle = new RespawnInfo { Location = new(2, 2), Spread = 1 };
        var cells = map.GetRespawnCells(circle);
        check(cells.Count == 5 && cells.Contains(new(3, 2)) && !cells.Contains(new(3, 3)), "Radius one must keep the center and four cardinal cells, excluding square corners");
        circle.Spread = 2;
        check(map.GetRespawnCells(circle).Count == 13, "Circular spawn radius does not use map grid distance");
        circle.Location = new(0, 0);
        check(map.GetRespawnCells(circle).Count == 6, "Circular spawn crossed the map boundary or lost valid edge cells");
        check(RespawnArea.Contains(new(0, 0), new(3, 4), 5) && !RespawnArea.Contains(new(0, 0), new(4, 4), 5), "Circle boundary must include exact radius and exclude diagonals beyond it");
        check(RespawnArea.Contains(new(0, 0), new(0, 0), 0) && !RespawnArea.Contains(new(0, 0), new(0, 1), 0) && !RespawnArea.Contains(new(0, 0), new(0, 0), -1), "Zero and negative spawn radii are not bounded");
        check(RespawnArea.Center(new(2, 2), 5, 5, (x, y) => x == 1 && y == 2 || x == 2 && y == 1) == new Point(1, 2), "Preview and engine corrected centers must retain the original tie order");
        check(RespawnArea.Center(new(2, 2), 5, 5, (_, _) => false) == null && RespawnArea.Center(new(-1, 0), 5, 5, (_, _) => true) == null, "Preview produced a center on a fully blocked or out-of-map location");
    }
}
