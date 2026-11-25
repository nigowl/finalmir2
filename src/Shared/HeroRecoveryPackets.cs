namespace ClientPackets
{
    public sealed class HeroRecoveryRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.HeroRecoveryRequest;
        public override bool Observable => false;
        protected override void ReadPacket(BinaryReader reader) { }
        protected override void WritePacket(BinaryWriter writer) { }
    }
}

namespace ServerPackets
{
    public sealed class HeroRecovery : Packet
    {
        public override short Index => (short)ServerPacketIds.HeroRecovery;
        public override bool Observable => false;
        public int HeroIndex, Milliseconds;
        protected override void ReadPacket(BinaryReader reader)
        {
            HeroIndex = reader.ReadInt32(); Milliseconds = reader.ReadInt32();
            if (HeroIndex < 0 || Milliseconds is < 0 or > 60000) throw new InvalidDataException("英雄恢复状态无效。");
        }
        protected override void WritePacket(BinaryWriter writer) { writer.Write(HeroIndex); writer.Write(Milliseconds); }
    }
}
