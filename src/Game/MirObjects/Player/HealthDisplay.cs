namespace Server.MirObjects;

public partial class PlayerObject
{
    public bool HealthDisplayEnabled { get; private set; }

    public void EnableHealthDisplay()
    {
        if (HealthDisplayEnabled || CurrentMap == null) return;
        // 每个角色会话只初始化一次，关闭显示仍接收增量，避免反复勾选扫描地图。
        HealthDisplayEnabled = true;
        for (var y = Math.Max(0, CurrentLocation.Y - Globals.DataRange); y <= Math.Min(CurrentMap.Height - 1, CurrentLocation.Y + Globals.DataRange); y++)
        for (var x = Math.Max(0, CurrentLocation.X - Globals.DataRange); x <= Math.Min(CurrentMap.Width - 1, CurrentLocation.X + Globals.DataRange); x++)
        {
            var cell = CurrentMap.GetCell(x, y);
            if (!cell.Valid || cell.Objects == null) continue;
            foreach (var target in cell.Objects) target.SendExactHealth(this);
        }
    }
}
