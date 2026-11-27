[Flags]
public enum EquipmentGlow : byte { None = 0, Weapon = 1, Equipment = 2 }

public static class EquipmentAppearance
{
    public static EquipmentGlow Glow(IEnumerable<UserItem> equipment)
    {
        var glow = EquipmentGlow.None;
        foreach (var item in equipment)
            if (item?.Info != null && item.Identified && item.EquipmentQuality == EquipmentGrade.Supreme &&
                EquipmentAppraisal.Supports(item.Info) && (item.MaxDura == 0 || item.CurrentDura > 0))
                glow |= item.Info.Type == ItemType.Weapon ? EquipmentGlow.Weapon : EquipmentGlow.Equipment;
        return glow;
    }
}

namespace ClientPackets
{
    public sealed class AppraiseEquipment : Packet
    {
        public override short Index => (short)ClientPacketIds.AppraiseEquipment;
        public override bool Observable => false;
        public ulong ScrollID, ItemID;
        protected override void ReadPacket(BinaryReader reader) { ScrollID = reader.ReadUInt64(); ItemID = reader.ReadUInt64(); }
        protected override void WritePacket(BinaryWriter writer) { writer.Write(ScrollID); writer.Write(ItemID); }
    }

    public sealed class ClientPresentationRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.ClientPresentationRequest;
        public override bool Observable => false;
        protected override void ReadPacket(BinaryReader reader) { }
        protected override void WritePacket(BinaryWriter writer) { }
    }
}

namespace ServerPackets
{
    public sealed class ManualItemDropped : Packet
    {
        public override short Index => (short)ServerPacketIds.ManualItemDropped;
        public override bool Observable => false;
        public uint ObjectID;
        protected override void ReadPacket(BinaryReader reader) => ObjectID = reader.ReadUInt32();
        protected override void WritePacket(BinaryWriter writer) => writer.Write(ObjectID);
    }

    public sealed class ObjectEquipmentGlow : Packet
    {
        public override short Index => (short)ServerPacketIds.ObjectEquipmentGlow;
        public override bool Observable => false;
        public uint ObjectID;
        public EquipmentGlow Glow;
        protected override void ReadPacket(BinaryReader reader) { ObjectID = reader.ReadUInt32(); Glow = (EquipmentGlow)reader.ReadByte(); }
        protected override void WritePacket(BinaryWriter writer) { writer.Write(ObjectID); writer.Write((byte)Glow); }
    }
}
