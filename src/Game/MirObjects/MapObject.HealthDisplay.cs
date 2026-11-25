using S = ServerPackets;

namespace Server.MirObjects;

public abstract partial class MapObject
{
    public void SendExactHealth(HumanObject recipient)
    {
        if (recipient is not PlayerObject { HealthDisplayEnabled: true } viewer ||
            Race is not (ObjectType.Player or ObjectType.Monster) || MaxHealth <= 0 || CurrentMap == null ||
            viewer.CurrentMap != CurrentMap || !Functions.InRange(CurrentLocation, viewer.CurrentLocation, Globals.DataRange) ||
            Hidden && viewer != this) return;
        viewer.Enqueue(new S.ObjectVitals { ObjectID = ObjectID, HP = Math.Clamp(Health, 0, MaxHealth), MaxHP = MaxHealth });
    }

    protected void BroadcastExactHealth()
    {
        if (CurrentMap == null) return;
        foreach (var viewer in CurrentMap.Players) SendExactHealth(viewer);
    }

    protected void BroadcastHealthDelta(int previousHealth)
    {
        var change = Health - previousHealth;
        if (change != 0) BroadcastDamageIndicator(DamageType.Hit, change);
    }
}
