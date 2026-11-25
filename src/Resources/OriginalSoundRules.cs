namespace Client.MirObjects;

public static partial class OriginalSoundRules
{
    public static byte[] WaveBytes(byte[] bytes)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("WAV 文件头无效。");
        var length = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) + 8;
        if (length < 12 || length > bytes.Length) throw new InvalidDataException("WAV 数据被截断。");
        // 部分素材在 RIFF 末尾填充到64KB；交给解码器会将填充值误读成超大数据块。
        return length == bytes.Length ? bytes : bytes[..(int)length];
    }

    // SoundList 只覆盖例外编号，其余声音沿用素材文件名规则。
    public static string SoundName(int id) => id == 10514 ? "pet_rat" : id > 20000 ? $"M{(id - 20000) / 10}-{id % 10}" : $"{id / 10:000}-{id % 10}";

    // 这些编号在现有素材中确实缺失，只复用对应类别的已有声音；上传专用声音后优先使用专用资源。
    public static int Fallback(int id) => id switch
    {
        10022 => 10021, 20160 => 20130, 20850 => 20840, 20861 => 20611,
        20940 or 20941 => 10110, 21071 => 21072, 20710 => 20690,
        21377 or 21407 => 21307, 21510 => 20370, 21530 => 20090, 21531 => 20091,
        1 or 21 or 3771 => 10052,
        1391 or 1411 or 3781 => 21210,
        3205 or 3395 or 3465 or 3665 or 3685 or 3705 or 3715 or 3725 => id / 10 * 10 + 4,
        8452 => 3486, 8454 => 3488,
        10509 => 10508, 10512 => 10501, 10513 => 10502,
        133 or 143 or 163 => 3323, // 静态树木倒下时可复用木箱破裂声。
        153 => 3523, // 大蘑菇倒下时可复用植物死亡声。
        705 => 20791,
        8303 or 8306 => 20501, 8304 or 8456 or 8457 => 8331,
        50000 => 10056, 50001 => 10070, 50002 => 10156, 50003 => 10105,
        _ when id is >= 0 and < 10000 && Enum.IsDefined((Monster)(id / 10)) && id % 10 is 1 or 6 or 7 or 8 or 9 => id / 10 * 10 + 4,
        _ when id is >= 0 and < 10000 && Enum.IsDefined((Monster)(id / 10)) && id % 10 == 4 => 10056,
        _ when id is >= 0 and < 10000 && Enum.IsDefined((Monster)(id / 10)) && id % 10 == 2 => 10070,
        _ => -1
    };

    public static int Item(ItemType? type) => type switch
    {
        ItemType.Weapon => 10111, ItemType.Armour => 10112, ItemType.Ring => 10113,
        ItemType.Bracelet => 10114, ItemType.Necklace => 10115, ItemType.Helmet => 10116,
        ItemType.Boots => 10117, ItemType.Potion => 10108, _ => 10118
    };

    public static int Struck(int weapon) => weapon < 0 ? 10070 : Swing(weapon) switch
    { 10051 => 10061, 10050 or 10056 => 10060, 10052 => 10062, 10053 => 10063, 10054 => 10064, 10055 => 10065, _ => 10070 };

    public static int Swing(int weapon, bool assassin = false) => assassin && weapon >= 0 ? 10050 : weapon switch
    {
        0 or 23 or 28 or 40 => 10051,
        1 or 12 => 10050,
        2 or 8 or 11 or 15 or 18 or 20 or 25 or 31 or 33 or 34 or 37 or 41 => 10052,
        3 or 5 or 7 or 9 or 13 or 19 or 24 or 26 or 29 or 32 or 35 => 10053,
        4 or 14 or 16 or 38 => 10054,
        6 or 10 or 17 or 22 or 27 or 30 or 36 or 39 => 10056,
        21 => 10055,
        _ => 10056
    };

    public static int Step(CellInfo cell, bool run, bool right)
    {
        var sound = 10001;
        // shortcut: Shanda/Mir3 地面暂用通用脚步声，覆盖这些地图时补齐原客户端分类。
        if (cell.BackIndex < 100) sound = WemadeStep(cell);
        return sound + (run ? 2 : 0) + (right ? 1 : 0);
    }

    private static int WemadeStep(CellInfo cell)
    {
        var index = (cell.BackImage & 0x1FFFF) - 1;
        if (index is < 0 or > 10000) return 10001;
        var sound = index switch
        {
            >= 330 and <= 349 or >= 450 and <= 454 or >= 550 and <= 554 or >= 750 and <= 754 or >= 950 and <= 954
                or >= 1250 and <= 1254 or >= 1400 and <= 1424 or >= 1455 and <= 1474 or >= 1500 and <= 1524 or >= 1550 and <= 1574 => 10009,
            >= 250 and <= 254 or >= 1005 and <= 1009 or >= 1050 and <= 1054 or >= 1060 and <= 1064 or >= 1450 and <= 1454 or >= 1650 and <= 1654 => 10013,
            >= 605 and <= 609 or >= 650 and <= 654 or >= 660 and <= 664 or >= 2000 and <= 2049 or >= 3025 and <= 3049
                or >= 2400 and <= 2424 or >= 4625 and <= 4649 or >= 4675 and <= 4678 => 10005,
            >= 1825 and <= 1924 or >= 2150 and <= 2174 or >= 3075 and <= 3099 or >= 3325 and <= 3349 or >= 3375 and <= 3399 => 10021,
            3230 or 3231 or 3246 or 3277 or >= 3780 and <= 3799 => 10017,
            >= 3825 and <= 4434 when index % 25 == 0 => 10017,
            >= 2075 and <= 2099 or >= 2125 and <= 2149 => 10025,
            >= 1800 and <= 1824 => 10029,
            _ => 10001
        };
        if (index is >= 825 and <= 1349 && (index - 825) / 25 % 2 == 0) sound = 10005;
        if (index is >= 1375 and <= 1799 && (index - 1375) / 25 % 2 == 0) sound = 10021;
        if (index is 1385 or 1386 or 1391 or 1392) sound = 10017;
        index = (cell.MiddleImage & 0x7FFF) - 1;
        if (index is >= 0 and <= 115) sound = 10001;
        else if (index is >= 120 and <= 124) sound = 10009;
        index = (cell.FrontImage & 0x7FFF) - 1;
        if (index is >= 221 and <= 289 or >= 583 and <= 658 or >= 1183 and <= 1206 or >= 7163 and <= 7295 or >= 7404 and <= 7414) sound = 10005;
        else if (index is >= 3125 and <= 3267 or >= 3757 and <= 3948 or >= 6030 and <= 6999) sound = 10017;
        if (index is >= 3316 and <= 3589) sound = 10025;
        return sound;
    }
}
