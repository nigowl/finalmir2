namespace ClientPackets
{
    public sealed class DeleteHeroRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.DeleteHeroRequest;
        public int HeroIndex;
        protected override void ReadPacket(BinaryReader reader) => HeroIndex = reader.ReadInt32();
        protected override void WritePacket(BinaryWriter writer) => writer.Write(HeroIndex);
    }
}

namespace ServerPackets
{
    public sealed class DeleteHeroResult : Packet
    {
        public override short Index => (short)ServerPacketIds.DeleteHeroResult;
        public int HeroIndex;
        public bool Success;
        public string Message = "";
        protected override void ReadPacket(BinaryReader reader)
        { HeroIndex = reader.ReadInt32(); Success = reader.ReadBoolean(); Message = reader.ReadString(); }
        protected override void WritePacket(BinaryWriter writer)
        { writer.Write(HeroIndex); writer.Write(Success); writer.Write(Message); }
    }
}
