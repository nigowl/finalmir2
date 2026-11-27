using System.Text.Json.Serialization;

public sealed record ItemPrice
{
    [JsonRequired] public int ItemIndex { get; set; }
    [JsonRequired] public uint Buy { get; set; }
    [JsonRequired] public uint Sell { get; set; }
    [JsonRequired] public uint Recycle { get; set; }
}

public sealed record ItemPriceCatalog
{
    [JsonRequired] public int Schema { get; set; } = 1;
    [JsonRequired] public ItemPrice[] Items { get; set; } = [];
}

namespace ClientPackets
{
    public sealed class ItemPricesRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.ItemPricesRequest;
        public override bool Observable => false;
        protected override void ReadPacket(BinaryReader reader) { }
        protected override void WritePacket(BinaryWriter writer) { }
    }
}

namespace ServerPackets
{
    public sealed class ItemPriceChanged : Packet
    {
        public override short Index => (short)ServerPacketIds.ItemPriceChanged;
        public override bool Observable => false;
        public ItemPrice Prices = new();
        public bool Configured;
        protected override void ReadPacket(BinaryReader reader)
        { Prices = new() { ItemIndex = reader.ReadInt32(), Buy = reader.ReadUInt32(), Sell = reader.ReadUInt32(), Recycle = reader.ReadUInt32() }; Configured = reader.ReadBoolean(); }
        protected override void WritePacket(BinaryWriter writer)
        { writer.Write(Prices.ItemIndex); writer.Write(Prices.Buy); writer.Write(Prices.Sell); writer.Write(Prices.Recycle); writer.Write(Configured); }
    }
}
