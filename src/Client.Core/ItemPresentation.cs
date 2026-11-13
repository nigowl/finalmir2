namespace Crystal.Native;

public sealed record ItemRequirement(string Text, bool Met);

public static class ItemPresentation
{
    private static readonly (Stat min, Stat max, string name)[] Ranges =
    [(Stat.MinAC, Stat.MaxAC, "防御"), (Stat.MinMAC, Stat.MaxMAC, "魔御"), (Stat.MinDC, Stat.MaxDC, "攻击"),
     (Stat.MinMC, Stat.MaxMC, "魔法"), (Stat.MinSC, Stat.MaxSC, "道术")];

    public static string Name(UserItem item) => (item.Info?.FriendlyName ?? $"物品 {item.ItemIndex}") +
        (item.Count > 1 && item.Info?.IsEquipment != true ? $"×{item.Count}" : "");

    public static int[] Slots(ItemType type) => type switch
    {
        ItemType.Weapon => [0], ItemType.Armour => [1], ItemType.Helmet => [2], ItemType.Torch => [3], ItemType.Necklace => [4],
        ItemType.Bracelet => [5, 6], ItemType.Ring => [7, 8], ItemType.Amulet => [9], ItemType.Belt => [10],
        ItemType.Boots => [11], ItemType.Stone => [12], ItemType.Mount => [13], _ => []
    };

    public static IEnumerable<string> Stats(UserItem item)
    {
        if (item.Info == null) yield break;
        foreach (var (min, max, name) in Ranges)
            if (item.GetTotal(min) != 0 || item.GetTotal(max) != 0) yield return $"{name} {item.GetTotal(min)}–{item.GetTotal(max)}";
        foreach (var stat in Enum.GetValues<Stat>().Where(stat => stat > Stat.MaxSC && item.GetTotal(stat) != 0))
            yield return $"{stat.ToLocalizedString()} {item.GetTotal(stat)}";
    }

    public static IEnumerable<(string text, int change)> Comparison(UserItem item, UserItem worn)
    {
        foreach (var (min, max, name) in Ranges)
        {
            var low = item.GetTotal(min) - worn.GetTotal(min); var high = item.GetTotal(max) - worn.GetTotal(max);
            if (low != 0 || high != 0) yield return ($"{name} {low:+0;-0;0} / {high:+0;-0;0}", low != 0 && high != 0 && Math.Sign(low) != Math.Sign(high) ? 0 : low + high);
        }
        foreach (var stat in Enum.GetValues<Stat>().Where(stat => stat > Stat.MaxSC))
        {
            var difference = item.GetTotal(stat) - worn.GetTotal(stat);
            if (difference != 0) yield return ($"{stat.ToLocalizedString()} {difference:+0;-0;0}", difference);
        }
    }

    public static IEnumerable<ItemRequirement> Requirements(UserItem item, GameState state)
    {
        if (state.User is not { } user || item.Info is not { } info) yield break;
        var stats = state.CalculateStats();
        var requiredStat = info.RequiredType switch
        {
            RequiredType.MinAC => Stat.MinAC, RequiredType.MaxAC => Stat.MaxAC, RequiredType.MinMAC => Stat.MinMAC, RequiredType.MaxMAC => Stat.MaxMAC,
            RequiredType.MinDC => Stat.MinDC, RequiredType.MaxDC => Stat.MaxDC, RequiredType.MinMC => Stat.MinMC, RequiredType.MaxMC => Stat.MaxMC,
            RequiredType.MinSC => Stat.MinSC, RequiredType.MaxSC => Stat.MaxSC, _ => (Stat?)null
        };
        if (info.RequiredAmount > 0 || info.RequiredType == RequiredType.MaxLevel)
            yield return new($"要求{info.RequiredType.ToLocalizedString()} {info.RequiredAmount}", info.RequiredType == RequiredType.MaxLevel ? user.Level <= info.RequiredAmount :
                (requiredStat.HasValue ? stats[requiredStat.Value] : user.Level) >= info.RequiredAmount);
        if (info.RequiredClass != RequiredClass.None)
            yield return new($"职业 {info.RequiredClass.ToLocalizedString()}", info.RequiredClass.HasFlag((RequiredClass)(1 << (int)user.Class)));
        if (info.RequiredGender != RequiredGender.None)
            yield return new($"性别 {info.RequiredGender.ToLocalizedString()}", info.RequiredGender.HasFlag(user.Gender == MirGender.Male ? RequiredGender.Male : RequiredGender.Female));
        if (item.SoulBoundId >= 0) yield return new("灵魂绑定角色", item.SoulBoundId == user.RealId);
        var slots = Slots(info.Type);
        if (slots.Length == 0) yield break;
        var slot = slots.FirstOrDefault(slot => user.Equipment.ElementAtOrDefault(slot) == null, slots[^1]);
        var hand = info.Type is ItemType.Weapon or ItemType.Torch;
        var weight = item.Weight - (user.Equipment.ElementAtOrDefault(slot)?.Weight ?? 0) + (hand ? state.HandWeight : state.WearWeight);
        yield return new($"{(hand ? "手持" : "穿戴")}负重 {weight}/{stats[hand ? Stat.HandWeight : Stat.WearWeight]}", weight <= stats[hand ? Stat.HandWeight : Stat.WearWeight]);
        if (item.RentalInformation?.BindingFlags != null && item.RentalInformation.BindingFlags != BindMode.None) yield return new("租赁物品当前不可使用", false);
    }
}
