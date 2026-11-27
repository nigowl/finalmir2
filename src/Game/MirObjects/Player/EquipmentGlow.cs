namespace Server.MirObjects;

public partial class PlayerObject
{
    public bool ClientPresentationEnabled { get; private set; }

    public void EnableClientPresentation()
    {
        if (ClientPresentationEnabled || CurrentMap == null) return;
        ClientPresentationEnabled = true;
        for (var y = Math.Max(0, CurrentLocation.Y - Globals.DataRange); y <= Math.Min(CurrentMap.Height - 1, CurrentLocation.Y + Globals.DataRange); y++)
        for (var x = Math.Max(0, CurrentLocation.X - Globals.DataRange); x <= Math.Min(CurrentMap.Width - 1, CurrentLocation.X + Globals.DataRange); x++)
        {
            var cell = CurrentMap.GetCell(x, y);
            if (!cell.Valid || cell.Objects == null) continue;
            foreach (var target in cell.Objects)
                if (target is HumanObject human) human.SendEquipmentGlow(this);
                else if (target is ItemObject item) item.SendManualDrop(this);
        }
    }
}

public partial class HumanObject
{
    private EquipmentGlow equipmentGlow;

    public void SendEquipmentGlow(HumanObject recipient)
    {
        if (recipient is not PlayerObject { ClientPresentationEnabled: true } viewer || CurrentMap == null || CurrentMap != viewer.CurrentMap ||
            !Functions.InRange(CurrentLocation, viewer.CurrentLocation, Globals.DataRange) || Hidden && viewer != this) return;
        viewer.Enqueue(new ServerPackets.ObjectEquipmentGlow { ObjectID = ObjectID, Glow = EquipmentAppearance.Glow(Info.Equipment) });
    }

    private void RefreshEquipmentGlow()
    {
        var current = EquipmentAppearance.Glow(Info.Equipment);
        if (current == equipmentGlow) return;
        equipmentGlow = current;
        if (CurrentMap == null) return;
        foreach (var viewer in CurrentMap.Players) SendEquipmentGlow(viewer);
    }
}
