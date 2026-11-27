using System.Drawing;

public enum EquipmentGrade : byte { Legacy, Normal, Fine, Superior, Excellent, Supreme }

public static class EquipmentAppraisal
{
    public const string Page = "[@APPRAISE]";
    public const short ScrollShape = 1000;
    public const uint ScrollPrice = 5000;
    public static bool IsScroll(ItemInfo info) => info?.Type == ItemType.Scroll && info.Shape == ScrollShape;
    public static bool Supports(ItemInfo info) => info != null && !info.IsFishingRod && info.Type is
        ItemType.Weapon or ItemType.Armour or ItemType.Helmet or ItemType.Necklace or ItemType.Bracelet or ItemType.Ring or
        ItemType.Belt or ItemType.Boots or ItemType.Stone or ItemType.Mask;
    public static bool Pending(UserItem item) => item != null && Supports(item.Info) && item.EquipmentQuality != EquipmentGrade.Legacy && !item.Identified;
    public static EquipmentGrade Roll(int roll) => roll is < 0 or >= 10000 ? throw new ArgumentOutOfRangeException(nameof(roll)) :
        roll < 6500 ? EquipmentGrade.Normal : roll < 8800 ? EquipmentGrade.Fine : roll < 9700 ? EquipmentGrade.Superior : roll < 9950 ? EquipmentGrade.Excellent : EquipmentGrade.Supreme;
    public static string Name(UserItem item) => Pending(item) ? "未鉴定" : item.EquipmentQuality switch
    { EquipmentGrade.Fine => "良品", EquipmentGrade.Superior => "上品", EquipmentGrade.Excellent => "精品", EquipmentGrade.Supreme => "极品", _ => "普通" };
    public static Color Colour(UserItem item) => Pending(item) ? Color.Silver : item.EquipmentQuality switch
    { EquipmentGrade.Fine => Color.FromArgb(106, 193, 102), EquipmentGrade.Superior => Color.FromArgb(93, 162, 225),
        EquipmentGrade.Excellent => Color.FromArgb(218, 83, 67), EquipmentGrade.Supreme => Color.FromArgb(238, 151, 55), _ => Color.White };
    public static uint Price(UserItem item) => (uint)Math.Clamp(item.Info.Price / 10, 100, 50000);
    public static Stats Bonuses(UserItem item)
    {
        var percent = item.EquipmentQuality switch { EquipmentGrade.Fine => 5, EquipmentGrade.Superior => 10, EquipmentGrade.Excellent => 15, EquipmentGrade.Supreme => 25, _ => 0 };
        var stats = new Stats();
        if (percent == 0) return stats;
        foreach (var stat in new[] { Stat.MinAC, Stat.MaxAC, Stat.MinMAC, Stat.MaxMAC, Stat.MinDC, Stat.MaxDC,
            Stat.MinMC, Stat.MaxMC, Stat.MinSC, Stat.MaxSC, Stat.HP, Stat.MP })
            if (item.Info.Stats[stat] > 0) stats[stat] = (int)Math.Min(int.MaxValue, Math.Max(1, ((long)item.Info.Stats[stat] * percent + 99) / 100));
        return stats;
    }
}

namespace ServerPackets
{
    public sealed class EquipmentAppraisalResult : Packet
    {
        public override short Index => (short)ServerPacketIds.EquipmentAppraisalResult;
        public ulong UniqueID;
        public bool Success;
        public string Message = "";
        protected override void ReadPacket(BinaryReader reader)
        { UniqueID = reader.ReadUInt64(); Success = reader.ReadBoolean(); Message = reader.ReadString(); }
        protected override void WritePacket(BinaryWriter writer)
        { writer.Write(UniqueID); writer.Write(Success); writer.Write(Message); }
    }
}
