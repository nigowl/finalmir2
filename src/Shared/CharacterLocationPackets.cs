using System.Drawing;

namespace ClientPackets
{
    public sealed class CharacterLocationRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.CharacterLocationRequest;
        public override bool Observable => false;
        public int CharacterIndex;
        protected override void ReadPacket(BinaryReader reader) => CharacterIndex = reader.ReadInt32();
        protected override void WritePacket(BinaryWriter writer) => writer.Write(CharacterIndex);
    }
}

namespace ServerPackets
{
    public sealed class CharacterLocation : Packet
    {
        public override short Index => (short)ServerPacketIds.CharacterLocation;
        public override bool Observable => false;
        public int CharacterIndex;
        public string MapTitle = "";
        public Point Location;
        public bool Found;

        protected override void ReadPacket(BinaryReader reader)
        {
            CharacterIndex = reader.ReadInt32(); Found = reader.ReadBoolean();
            MapTitle = reader.ReadString(); Location = new Point(reader.ReadInt32(), reader.ReadInt32());
        }
        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(CharacterIndex); writer.Write(Found); writer.Write(MapTitle);
            writer.Write(Location.X); writer.Write(Location.Y);
        }
    }
}
