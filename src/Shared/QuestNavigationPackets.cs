using System.Drawing;

public sealed class QuestNavigationTarget
{
    public int Id, MapIndex;
    public uint NpcId;
    public string Name = "", MapTitle = "";
    public Point Location, NextLocation;
    public bool Reachable;
}

namespace ClientPackets
{
    public sealed class QuestNavigationRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.QuestNavigationRequest;
        public override bool Observable => false;
        public int QuestIndex, RequestId;
        protected override void ReadPacket(BinaryReader reader) { QuestIndex = reader.ReadInt32(); RequestId = reader.ReadInt32(); }
        protected override void WritePacket(BinaryWriter writer) { writer.Write(QuestIndex); writer.Write(RequestId); }
    }
}
namespace ServerPackets
{
    public sealed class QuestNavigation : Packet
    {
        public override short Index => (short)ServerPacketIds.QuestNavigation;
        public override bool Observable => false;
        public int QuestIndex, RequestId, OriginMap;
        public List<QuestNavigationTarget> Targets = new();
        protected override void ReadPacket(BinaryReader reader)
        {
            QuestIndex = reader.ReadInt32(); RequestId = reader.ReadInt32(); OriginMap = reader.ReadInt32();
            var count = reader.ReadByte();
            if (count > 64) throw new InvalidDataException("任务导航目标过多。");
            for (var i = 0; i < count; i++) Targets.Add(new QuestNavigationTarget { Id = reader.ReadInt32(), MapIndex = reader.ReadInt32(),
                NpcId = reader.ReadUInt32(), Name = reader.ReadString(), MapTitle = reader.ReadString(),
                Location = new Point(reader.ReadInt32(), reader.ReadInt32()), NextLocation = new Point(reader.ReadInt32(), reader.ReadInt32()), Reachable = reader.ReadBoolean() });
        }
        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write(QuestIndex); writer.Write(RequestId); writer.Write(OriginMap); writer.Write((byte)Targets.Count);
            foreach (var t in Targets)
            { writer.Write(t.Id); writer.Write(t.MapIndex); writer.Write(t.NpcId); writer.Write(t.Name); writer.Write(t.MapTitle);
                writer.Write(t.Location.X); writer.Write(t.Location.Y); writer.Write(t.NextLocation.X); writer.Write(t.NextLocation.Y); writer.Write(t.Reachable); }
        }
    }
}
