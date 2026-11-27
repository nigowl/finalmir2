namespace ClientPackets;

public sealed class StorageRequest : Packet
{
    public override short Index => (short)ClientPacketIds.StorageRequest;
    public override bool Observable => false;
    protected override void ReadPacket(BinaryReader reader) { }
    protected override void WritePacket(BinaryWriter writer) { }
}
