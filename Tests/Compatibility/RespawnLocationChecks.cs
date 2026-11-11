using System.Drawing;
using System.Runtime.CompilerServices;
using Server.MirDatabase;
using Server.MirEnvir;

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
        var normal = new RespawnInfo { Location = new(1, 2), Spread = 4 };
        check(map.GetRespawnCells(normal).SequenceEqual(map.WalkableCells), "Normal spawn spread lost its original candidate cells");
        blocked.Spread = 1;
        check(map.GetRespawnCells(blocked).All(point => Math.Abs(point.X - 1) <= 1 && Math.Abs(point.Y - 2) <= 1), "Spawn spread was not centered on the corrected point");
        check(map.GetRespawnCells(new RespawnInfo { Location = new(-1, 2) }).Count == 0, "Out-of-map spawn was silently moved into the map");
        map.WalkableCells.Clear();
        check(map.GetRespawnCells(blocked).Count == 0, "Fully blocked map produced an invalid spawn candidate");
    }
}
