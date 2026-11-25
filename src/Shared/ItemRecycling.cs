[Flags]
public enum ItemActions : byte { None = 0, Display = 1, Pickup = 2, Recycle = 4 }

public sealed record ItemCatalogEntry(int Index, string Name, ItemType Type);

public static class ItemRecycling
{
    public static string Rejection(UserItem item)
    {
        if (item?.Info == null || item.Count == 0) return "物品不存在。";
        if (item.Info.Type == ItemType.Quest) return "任务专用品不能回收。";
        if (item.SoulBoundId >= 0 || item.Info.Bind.HasFlag(BindMode.DontSell) || item.RentalInformation != null ||
            item.ExpireInfo != null || item.SealedInfo != null || item.WeddingRing >= 0) return "绑定、租赁、时效或封印物品不能回收。";
        if (item.IsAdded || item.GemCount != 0 || item.Awake.GetAwakeLevel() > 0 || item.RefineAdded != 0 ||
            item.RefinedValue != RefinedValue.None || item.RefineSuccessChance != 0 || item.Slots.Any(slot => slot != null)) return "强化或镶嵌物品已保留。";
        return item.Price() < 2 ? "物品没有回收价值。" : "";
    }
}

namespace ClientPackets
{
    public sealed class RecycleItem : Packet
    {
        public override short Index => (short)ClientPacketIds.RecycleItem;
        public override bool Observable => false;
        public int RequestId;
        public ulong UniqueID;
        public ushort Count;
        protected override void ReadPacket(BinaryReader reader) { RequestId = reader.ReadInt32(); UniqueID = reader.ReadUInt64(); Count = reader.ReadUInt16(); }
        protected override void WritePacket(BinaryWriter writer) { writer.Write(RequestId); writer.Write(UniqueID); writer.Write(Count); }
    }
}

namespace ServerPackets
{
    public sealed class RecycleItemResult : Packet
    {
        public override short Index => (short)ServerPacketIds.RecycleItemResult;
        public override bool Observable => false;
        public int RequestId;
        public ulong UniqueID;
        public ushort Count;
        public bool Success;
        public uint Gold;
        public string Message = "";
        protected override void ReadPacket(BinaryReader reader)
        { RequestId = reader.ReadInt32(); UniqueID = reader.ReadUInt64(); Count = reader.ReadUInt16(); Success = reader.ReadBoolean(); Gold = reader.ReadUInt32(); Message = reader.ReadString(); }
        protected override void WritePacket(BinaryWriter writer)
        { writer.Write(RequestId); writer.Write(UniqueID); writer.Write(Count); writer.Write(Success); writer.Write(Gold); writer.Write(Message); }
    }
}
