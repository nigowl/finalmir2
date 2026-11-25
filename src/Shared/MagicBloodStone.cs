// 储量沿用物品已有的整数属性保存；附加值记录消耗，不改变物品包和存档布局。
public static class MagicBloodStone
{
    public static bool IsStone(ItemInfo info) => info?.Type == ItemType.Torch && info.Stats[Stat.MagicBlood] > 0;
    public static int Remaining(UserItem item) => IsStone(item?.Info) ? Math.Clamp(item.GetTotal(Stat.MagicBlood), 0, item.Info.Stats[Stat.MagicBlood]) : 0;
    public static (int HP, int MP) Consume(UserItem item, int missingHp, int missingMp)
    {
        var remaining = Remaining(item);
        var hp = Math.Min(remaining, Math.Clamp(missingHp, 0, 30));
        var mp = Math.Min(remaining - hp, Math.Clamp(missingMp, 0, 30));
        if (hp + mp > 0) item.AddedStats[Stat.MagicBlood] -= hp + mp;
        return (hp, mp);
    }
}
