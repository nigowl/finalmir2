using S = ServerPackets;

namespace Server.MirObjects;

public partial class HumanObject
{
    private long magicBloodTime;
    protected void ProcessMagicBloodStone()
    {
        var owner = this as PlayerObject ?? (this as HeroObject)?.Owner;
        if (Dead || owner?.TradePartner != null || CurrentMap?.Info.NoDrug == true || Envir.Time < magicBloodTime) return;
        magicBloodTime = Envir.Time + 2000;
        var item = Info.Equipment[(int)EquipmentSlot.Torch];
        if (!global::MagicBloodStone.IsStone(item?.Info)) return;
        var restored = global::MagicBloodStone.Consume(item, Stats[Stat.HP] - HP, Stats[Stat.MP] - MP);
        if (restored.HP + restored.MP == 0) return;
        ChangeHP(restored.HP); ChangeMP(restored.MP);
        Enqueue(new S.RefreshItem { Item = item.Clone() });
        if (global::MagicBloodStone.Remaining(item) == 0) ReceiveChat("魔血石储量已用尽，请更换。", ChatType.System);
    }
}
