namespace Crystal.Native;

public sealed record ItemRequirement(string Text, bool Met);

public static class ItemPresentation
{
    // DNItems已逐帧核对；高额金币只换外观，仍按ObjectGold数量拾取。
    public static int GroundGoldImage(uint amount) => amount switch
    {
        < 100 => 112,
        < 1_000 => 114,
        < 1_000_000 => 116,
        < 5_000_000 => 117,
        < 10_000_000 => 121,
        _ => 122
    };

    public static bool CanQuickUse(UserItem? item) => item?.Info is { } info &&
        (info.IsConsumable || info.Type is ItemType.Book or ItemType.Pets or ItemType.Deco or ItemType.MonsterSpawn);

    // 旧快捷格里的非使用物品仍须可取出；正常快捷道具只显示在快捷栏。
    public static int[] BagSlots(UserItem[] inventory) => Enumerable.Range(Math.Min(6, inventory.Length), Math.Max(0, inventory.Length - 6))
        .Concat(Enumerable.Range(0, Math.Min(6, inventory.Length)).Where(slot => inventory[slot] != null && !CanQuickUse(inventory[slot]))).ToArray();

    public static string Details(UserItem item) =>
        (MagicBloodStone.IsStone(item.Info) ? $"魔血储量 {MagicBloodStone.Remaining(item):N0}/{item.Info.Stats[Stat.MagicBlood]:N0} · " :
            item.Info.Type == ItemType.Amulet ? $"剩余次数 {item.Count:N0} · " :
            item.CurrentDura != 0 || item.MaxDura != 0 ? item.Info.IsEquipment ? $"耐久 {item.CurrentDura / 1000}/{item.MaxDura / 1000} · " :
                $"耐久 {item.CurrentDura / 1000f:0.###}/{item.MaxDura / 1000f:0.###} · " : "") +
        (item.Count > 1 && item.Info.Type is not (ItemType.Amulet or ItemType.Bait) ? $"单重 {item.Info.UnitWeight:0.##} · 总重 {item.Weight:0.##}" : $"重量 {item.Weight:0.##}");

    public static string Category(UserItem item) => item.Info?.Type == ItemType.Quest ? "任务物品 · 材料物品栏" :
        $"{item.Info?.Type.ToLocalizedString()}" + (item.Info?.IsEquipment == true ? $" · {EquipmentAppraisal.Name(item)}" : "");

    private static readonly (Stat min, Stat max, string name)[] Ranges =
    [(Stat.MinAC, Stat.MaxAC, "防御"), (Stat.MinMAC, Stat.MaxMAC, "魔御"), (Stat.MinDC, Stat.MaxDC, "攻击"),
     (Stat.MinMC, Stat.MaxMC, "魔法"), (Stat.MinSC, Stat.MaxSC, "道术")];

    public static string Name(UserItem item) => (item.Info?.FriendlyName ?? $"物品 {item.ItemIndex}") +
        (item.Count > 1 && item.Info?.IsEquipment != true && item.Info?.Type != ItemType.Amulet ? $"×{item.Count}" : "");

    public static int[] Slots(ItemType type) => type switch
    {
        ItemType.Weapon => [0], ItemType.Armour => [1], ItemType.Helmet => [2], ItemType.Torch => [3], ItemType.Necklace => [4],
        ItemType.Bracelet => [5, 6], ItemType.Ring => [7, 8], ItemType.Amulet => [9], ItemType.Belt => [10],
        ItemType.Boots => [11], ItemType.Stone => [12], ItemType.Mount => [13], _ => []
    };

    public static IEnumerable<string> Stats(UserItem item) => StatLines(item).Select(line => string.Concat(line.Select(part => part.Text)));

    public static IEnumerable<(string Text, bool Quality)[]> StatLines(UserItem item)
    {
        if (item.Info == null) yield break;
        var bonuses = item.Identified ? EquipmentAppraisal.Bonuses(item) : new Stats();
        var lines = Ranges.Select(range => (range.min, max: (Stat?)range.max, range.name))
            .Concat(Enum.GetValues<Stat>().Where(stat => stat > Stat.MaxSC && stat != Stat.MagicBlood)
                .Select(stat => (stat, (Stat?)null, stat.ToLocalizedString())));
        foreach (var (min, max, name) in lines)
        {
            if (item.GetTotal(min) == 0 && (!max.HasValue || item.GetTotal(max.Value) == 0)) continue;
            var parts = new List<(string Text, bool Quality)> { (name + "：", false) };
            void Value(Stat stat)
            {
                var bonus = bonuses[stat];
                parts.Add(($"{item.GetTotal(stat) - bonus}", false));
                if (bonus > 0) parts.Add(($"(+{bonus})", true));
            }
            Value(min);
            if (max.HasValue) { parts.Add((" - ", false)); Value(max.Value); }
            yield return parts.ToArray();
        }
    }

    public static IEnumerable<(string text, int change)> Comparison(UserItem item, UserItem worn)
        => ComparisonLines(item, worn).Select(line => (string.Concat(line.Select(part => part.Text)),
            line.Any(part => part.Change > 0) && line.Any(part => part.Change < 0) ? 0 : line.Sum(part => part.Change)));

    public static IEnumerable<(string Text, int Change)[]> ComparisonLines(UserItem item, UserItem worn)
    {
        foreach (var (min, max, name) in Ranges)
        {
            var low = item.GetTotal(min) - worn.GetTotal(min); var high = item.GetTotal(max) - worn.GetTotal(max);
            if (low != 0 || high != 0) yield return [(name + " ", 0), ($"{low:+0;-0;0}", low), (" / ", 0), ($"{high:+0;-0;0}", high)];
        }
        foreach (var stat in Enum.GetValues<Stat>().Where(stat => stat > Stat.MaxSC && stat != Stat.MagicBlood))
        {
            var difference = item.GetTotal(stat) - worn.GetTotal(stat);
            if (difference != 0) yield return [(stat.ToLocalizedString() + " ", 0), ($"{difference:+0;-0;0}", difference)];
        }
    }

    public static decimal EquipmentWeight(ServerPackets.UserInformation user, bool hand) => user.Equipment.Where(item => item?.Info != null)
        .SelectMany(item => new[] { item }.Concat(item.Slots ?? []))
        .Where(item => item?.Info != null && (item.Info.Type is ItemType.Weapon or ItemType.Torch) == hand).Sum(item => item.Weight);

    public static IEnumerable<ItemRequirement> Requirements(UserItem item, GameState state, bool hero = false)
    {
        var user = hero ? state.Hero : state.User;
        if (user == null || item.Info is not { } info) yield break;
        var stats = hero ? state.CalculateHeroStats() : state.CalculateStats();
        var requiredStat = info.RequiredType switch
        {
            RequiredType.MinAC => Stat.MinAC, RequiredType.MaxAC => Stat.MaxAC, RequiredType.MinMAC => Stat.MinMAC, RequiredType.MaxMAC => Stat.MaxMAC,
            RequiredType.MinDC => Stat.MinDC, RequiredType.MaxDC => Stat.MaxDC, RequiredType.MinMC => Stat.MinMC, RequiredType.MaxMC => Stat.MaxMC,
            RequiredType.MinSC => Stat.MinSC, RequiredType.MaxSC => Stat.MaxSC, _ => (Stat?)null
        };
        if (info.RequiredAmount > 0 || info.RequiredType == RequiredType.MaxLevel)
            yield return new($"{info.RequiredType.ToLocalizedString()} {info.RequiredAmount}", info.RequiredType == RequiredType.MaxLevel ? user.Level <= info.RequiredAmount :
                (requiredStat.HasValue ? stats[requiredStat.Value] : user.Level) >= info.RequiredAmount);
        if (info.RequiredClass != RequiredClass.None && (int)info.RequiredClass != 31)
            yield return new($"职业 {info.RequiredClass.ToLocalizedString()}", info.RequiredClass.HasFlag((RequiredClass)(1 << (int)user.Class)));
        if (info.RequiredGender != RequiredGender.None)
            yield return new($"性别 {info.RequiredGender.ToLocalizedString()}", info.RequiredGender.HasFlag(user.Gender == MirGender.Male ? RequiredGender.Male : RequiredGender.Female));
        if (item.SoulBoundId >= 0) yield return new($"绑定：{state.ItemOwnerName(item.SoulBoundId)}", item.SoulBoundId == state.User?.RealId);
        var slots = Slots(info.Type);
        if (slots.Length == 0) yield break;
        var slot = slots.FirstOrDefault(slot => user.Equipment.ElementAtOrDefault(slot) == null, slots[^1]);
        var hand = info.Type is ItemType.Weapon or ItemType.Torch;
        var weight = item.Weight - (user.Equipment.ElementAtOrDefault(slot)?.Weight ?? 0) + EquipmentWeight(user, hand);
        yield return new($"负重 {weight:0.##}/{stats[hand ? Stat.HandWeight : Stat.WearWeight]}", weight <= stats[hand ? Stat.HandWeight : Stat.WearWeight]);
        if (hero && info.Bind.HasFlag(BindMode.NoHero)) yield return new("英雄不可使用", false);
        if (item.RentalInformation?.BindingFlags != null && item.RentalInformation.BindingFlags != BindMode.None) yield return new("租赁物品当前不可使用", false);
    }
}
