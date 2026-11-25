public static class PublicShopRules
{
    public static readonly string[] DefaultTabNames = ["药水", "卷轴", "符毒", "补给", "技能"];
    public static readonly (string Size, ushort Count)[] AmuletSizes = [("小", 500), ("中", 1000), ("大", 2000)];

    public static bool IsGuildHorn(ItemInfo item) => item.Type == ItemType.CraftingMaterial && item.Name == "沃玛角";

    public static int Category(ItemInfo item) => item.Type switch
    {
        ItemType.Potion when item.Shape == 0 && (item.Stats[Stat.HP] > 0 || item.Stats[Stat.MP] > 0) => 0,
        ItemType.Scroll when item.Shape is 1 or 2 => 1,
        ItemType.Book when item.Shape > 0 && item.Bind == BindMode.None => 4,
        ItemType.Amulet when item.Shape is >= 0 and <= 2 && AmuletSizes.Any(size =>
            item.Name == $"{GroupName(item)}（{size.Size}）" && item.StackSize == size.Count) => 2,
        ItemType.Torch when MagicBloodStone.IsStone(item) || item.Shape <= 1 => 3,
        ItemType.Food when item.Shape <= 1 => 3,
        ItemType.CraftingMaterial when IsGuildHorn(item) => 3,
        _ => -1
    };

    public static ushort UnitCount(ItemInfo item) => item.Type == ItemType.Amulet ? item.StackSize : (ushort)1;
    public static string GroupName(ItemInfo item) => item.Type == ItemType.Amulet && item.Shape <= 2 ? item.Shape switch { 0 => "护身符", 1 => "绿毒", _ => "红毒" } :
        MagicBloodStone.IsStone(item) ? "魔血石" : item.Type == ItemType.Potion && item.Shape == 0 ?
            item.Stats[Stat.HP] > 0 ? item.Stats[Stat.MP] > 0 ? "生命魔法药" : "金创药" : "魔法药" :
        System.Text.RegularExpressions.Regex.Replace(item.FriendlyName, @"[（(][^）)]*[）)]$", "").Trim();
    public static string UnitText(ItemInfo item) => item.Type == ItemType.Amulet ? $"{UnitCount(item):N0}次/包" : MagicBloodStone.IsStone(item) ? $"储量 {item.Stats[Stat.MagicBlood]:N0}" : "";
}

public sealed class PublicShopEntry
{
    public int ItemIndex;
    public byte Category;
    public uint Price;
    public ushort MaxCount;
}

namespace ClientPackets
{
    public sealed class PublicShopCatalogRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.PublicShopCatalogRequest;
        public override bool Observable => false;
        protected override void ReadPacket(BinaryReader reader) { }
        protected override void WritePacket(BinaryWriter writer) { }
    }
    public sealed class PublicShopRequest : Packet
    {
        public override short Index => (short)ClientPacketIds.PublicShopRequest;
        public override bool Observable => false;
        protected override void ReadPacket(BinaryReader reader) { }
        protected override void WritePacket(BinaryWriter writer) { }
    }
    public sealed class PublicShopBuy : Packet
    {
        public override short Index => (short)ClientPacketIds.PublicShopBuy;
        public override bool Observable => false;
        public int RequestId, ItemIndex;
        public uint UnitPrice;
        public ushort Count;
        protected override void ReadPacket(BinaryReader reader)
        { RequestId = reader.ReadInt32(); ItemIndex = reader.ReadInt32(); UnitPrice = reader.ReadUInt32(); Count = reader.ReadUInt16(); }
        protected override void WritePacket(BinaryWriter writer)
        { writer.Write(RequestId); writer.Write(ItemIndex); writer.Write(UnitPrice); writer.Write(Count); }
    }
}

namespace ServerPackets
{
    public sealed class PublicShopTabs : Packet
    {
        public override short Index => (short)ServerPacketIds.PublicShopTabs;
        public override bool Observable => false;
        public PublicShopTab[] Tabs = [];
        protected override void ReadPacket(BinaryReader reader)
        {
            var count = reader.ReadByte();
            if (count is < 1 or > 8) throw new InvalidDataException("商城选项卡数量无效。");
            Tabs = new PublicShopTab[count]; var ids = new HashSet<byte>();
            for (var i = 0; i < count; i++)
            {
                var id = reader.ReadByte(); var name = reader.ReadString();
                if (id > 31 || !ids.Add(id) || string.IsNullOrWhiteSpace(name) || name.Length > 16 || name.Any(char.IsControl)) throw new InvalidDataException("商城选项卡无效。");
                Tabs[i] = new PublicShopTab { Id = id, Name = name };
            }
        }
        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write((byte)Tabs.Length);
            foreach (var tab in Tabs) { writer.Write(tab.Id); writer.Write(tab.Name); }
        }
    }
    public sealed class PublicShop : Packet
    {
        public override short Index => (short)ServerPacketIds.PublicShop;
        public override bool Observable => false;
        public List<PublicShopEntry> Items = new();
        protected override void ReadPacket(BinaryReader reader)
        {
            var count = reader.ReadUInt16();
            if (count > 512) throw new InvalidDataException("公共商店商品过多。");
            for (var i = 0; i < count; i++) Items.Add(new PublicShopEntry { ItemIndex = reader.ReadInt32(),
                Category = reader.ReadByte(), Price = reader.ReadUInt32(), MaxCount = reader.ReadUInt16() });
        }
        protected override void WritePacket(BinaryWriter writer)
        {
            writer.Write((ushort)Items.Count);
            foreach (var item in Items)
            { writer.Write(item.ItemIndex); writer.Write(item.Category); writer.Write(item.Price); writer.Write(item.MaxCount); }
        }
    }
    public sealed class PublicShopResult : Packet
    {
        public override short Index => (short)ServerPacketIds.PublicShopResult;
        public override bool Observable => false;
        public int RequestId;
        public bool Success;
        public string Message = "";
        protected override void ReadPacket(BinaryReader reader)
        { RequestId = reader.ReadInt32(); Success = reader.ReadBoolean(); Message = reader.ReadString(); }
        protected override void WritePacket(BinaryWriter writer)
        { writer.Write(RequestId); writer.Write(Success); writer.Write(Message); }
    }
}
