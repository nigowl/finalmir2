namespace ClientPackets
{
    public sealed class HealthDisplayRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.HealthDisplayRequest;
        public override bool Observable => false;
        protected override void ReadPacket(BinaryReader reader) { }
        protected override void WritePacket(BinaryWriter writer) { }
    }
}

namespace ServerPackets
{
    public sealed class ObjectVitals : Packet
    {
        public override short Index => (short)ServerPacketIds.ObjectVitals;
        public override bool Observable => false;
        public uint ObjectID;
        public int HP, MaxHP;
        protected override void ReadPacket(BinaryReader reader)
        { ObjectID = reader.ReadUInt32(); HP = reader.ReadInt32(); MaxHP = reader.ReadInt32(); }
        protected override void WritePacket(BinaryWriter writer)
        { writer.Write(ObjectID); writer.Write(HP); writer.Write(MaxHP); }
    }
}
