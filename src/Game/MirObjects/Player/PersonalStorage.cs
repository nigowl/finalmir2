namespace Server.MirObjects;

public partial class PlayerObject
{
    public bool CanOpenStorage => !Dead && CurrentMap != null && Account != null &&
        TradePartner == null && ItemRentalPartner == null && !Fishing;

    public void RequestStorage()
    {
        if (!CanOpenStorage) return;
        ResetStorageUnlock();
        Connection.StorageSent = false;
        SendStorage();
        Enqueue(new ServerPackets.NPCStorage());
    }
}
