using System.Text.RegularExpressions;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

// 名称只描述原规则；分组、重复判定、数量和先后顺序不因分类而改变。
public sealed class DropScriptNames(Func<string, string> read)
{
    private readonly Dictionary<string, string> destinations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> namedRules = new(StringComparer.Ordinal);

    public static bool NeedsDescription(string path) => path.Split('/').Any(part => part is "备用" or "未启用" or "历史导入" ||
        Regex.IsMatch(part, @"^(公共掉落|分类)-?\d+|^\d+(\.txt)?$")) || Regex.IsMatch(path.Replace(".txt", ""), "[A-Za-z]");

    public string Assign(string? preferred, string content)
    {
        var signature = string.Join('\n', Lines(content));
        if (preferred != null && (!destinations.TryGetValue(preferred, out var existing) || existing == signature))
            return Remember(preferred, signature);
        if (namedRules.TryGetValue(signature, out var shared)) return shared;
        foreach (var candidate in Candidates(content).Distinct(StringComparer.Ordinal))
        {
            if (candidate.Length > 160 || Regex.IsMatch(candidate[..^4], "[A-Za-z]") || candidate.Any(char.IsControl)) continue;
            if (!destinations.TryGetValue(candidate, out var value) || value == signature) return Remember(candidate, signature);
        }
        throw new InvalidDataException("无法用掉落内容明确区分脚本名称，请检查重复规则和实际物品名称。");
    }

    private string Remember(string path, string signature)
    {
        destinations[path] = signature;
        if (!path.StartsWith("公共/系统/", StringComparison.Ordinal)) namedRules.TryAdd(signature, path);
        return path;
    }

    private static string[] Lines(string content) => content.Split('\n').Select(line => Regex.Replace(line.Trim(), @"\s+", " "))
        .Where(line => line.Length > 0 && !line.StartsWith(';')).ToArray();

    private IEnumerable<string> Expand(string content, HashSet<string> visiting, int depth = 0)
    {
        if (depth > 16) throw new InvalidDataException("掉落引用超过16层，不能推断名称。");
        foreach (var line in Lines(content))
        {
            var insert = Regex.Match(line, @"^#INSERT \[(.+)\]$", RegexOptions.IgnoreCase);
            if (!insert.Success) { yield return line; continue; }
            var path = insert.Groups[1].Value.Replace('\\', '/');
            if (!visiting.Add(path)) throw new InvalidDataException("掉落脚本循环引用，不能推断名称。");
            foreach (var child in Expand(read(path), visiting, depth + 1)) yield return child;
            visiting.Remove(path);
        }
    }

    private IEnumerable<string> Candidates(string content)
    {
        var lines = Expand(content, new(StringComparer.Ordinal)).ToArray();
        var drops = lines.Where(line => Regex.IsMatch(line, @"^(1/|\d+(?:\.\d+)?%)")).Select(DropInfo.FromLine).Where(drop => drop != null).ToArray();
        var items = drops.Where(drop => drop.Item != null).Select(drop => drop.Item).DistinctBy(item => item.Index).ToArray();
        var equipment = items.Where(item => Equipment(item.Type)).ToArray();
        var special = items.Where(item => !Equipment(item.Type) && item.Type is not (ItemType.Potion or ItemType.Scroll)).ToArray();
        var focus = (equipment.Length > 0 ? equipment : special.Length > 0 ? special : items)
            .OrderByDescending(item => item.RequiredType == RequiredType.Level ? item.RequiredAmount : 0)
            .ThenByDescending(item => item.Price).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var gold = drops.Where(drop => drop.Gold > 0).ToArray();
        var folder = items.Length == 0 && gold.Length == 0 ? "组合掉落/空分组" : Folder(items, equipment);
        var name = PrimaryName(focus, equipment, gold);
        if (drops.Any(drop => drop.Item != null) && drops.Where(drop => drop.Item != null).All(drop => drop.QuestRequired)) name += "任务限定";
        if (!drops.Any(drop => drop.GroupedDrop != null) && drops.Any(drop => drop.Count > 1)) name += "含多份规则";
        var stem = "公共/" + folder + "/" + name;
        var coins = gold.Length == 0 ? "无金币" : string.Join("与", gold.GroupBy(drop => (drop.Gold, drop.Chance)).Select(group => "金币基数" + Number(group.Key.Gold) + "基率分母" + Number(group.Key.Chance) + (group.Count() > 1 ? "共" + Number(group.Count()) + "次" : "")));
        yield return stem + ".txt";
        if (items.Length == 0 && gold.Length > 0) yield return stem + "·基率分母" + string.Join("与", gold.Select(drop => drop.Chance).Distinct().Select(value => Number(value))) + ".txt";
        yield return stem + "·" + coins + ".txt";
        yield return stem + "·" + coins + "·共" + Number(drops.Length) + "条判定.txt";
        yield return stem + "·" + coins + "·药剂" + Number(drops.Count(drop => drop.Item?.Type == ItemType.Potion)) + "次判定.txt";
        foreach (var drop in drops)
        {
            var label = drop.GroupedDrop != null ? drop.GroupedDrop.Random ? "随机单选分组" : drop.GroupedDrop.First ? "首中即止分组" : "独立分组"
                : drop.Item == null ? "金币基数" + Number(drop.Gold) : Chinese(drop.Item.Name);
            var rule = label + "基率分母" + Number(drop.Chance);
            if (drop.Item != null && drop.Count > 1) rule += "每次" + Number(drop.Count) + "份";
            if (drop.QuestRequired) rule += "限任务";
            yield return stem + "·" + coins + "·" + rule + ".txt";
        }
    }

    private static string PrimaryName(ItemInfo[] focus, ItemInfo[] equipment, DropInfo[] gold)
    {
        if (focus.Length == 0) return gold.Length == 0 ? "空分组" : "基础金币" + string.Join("与", gold.Select(drop => drop.Gold).Distinct().Select(value => Number(value)));
        var name = string.Join("与", focus.Take(2).Select(item => Chinese(item.Name)));
        if (focus.Length <= 2) return name;
        var caption = focus.Select(item => item.Type).Distinct().Count() == 1 ? Caption(focus) : equipment.Length > 0 ? "装备" : "道具";
        return name + "等" + caption;
    }

    private static string Folder(ItemInfo[] items, ItemInfo[] equipment)
    {
        var types = items.Select(item => item.Type).ToHashSet();
        string kind;
        if (items.Length == 0) return "金币/基础金币";
        if (equipment.Length > 0) kind = items.All(item => Equipment(item.Type) || item.Type is ItemType.Potion or ItemType.Scroll) ? "武器装备" : "综合掉落";
        else if (types.All(type => type is ItemType.Potion or ItemType.Scroll)) kind = "药品补给";
        else if (types.Contains(ItemType.Quest)) kind = "任务材料";
        else if (types.Contains(ItemType.Book)) kind = "职业技能";
        else if (types.Overlaps([ItemType.Gem, ItemType.Awakening, ItemType.CraftingMaterial, ItemType.Ore, ItemType.Socket])) kind = "强化制作";
        else kind = "特殊道具";
        var core = equipment.Length > 0 ? equipment : items.Where(item => item.Type != ItemType.Potion).ToArray();
        if (core.Length == 0) core = items;
        kind += "/" + Caption(core);
        var levels = core.Where(item => item.RequiredType == RequiredType.Level && item.RequiredAmount > 0 && (Equipment(item.Type) || item.Type == ItemType.Book))
            .Select(item => (long)item.RequiredAmount).ToArray();
        if (levels.Length > 0) kind += "/" + Number(levels.Min()) + (levels.Min() == levels.Max() ? "" : "至" + Number(levels.Max())) + "级";
        var classes = core.Select(item => item.RequiredClass).Distinct().ToArray();
        if (classes.Length == 1 && classes[0] is RequiredClass.Warrior or RequiredClass.Wizard or RequiredClass.Taoist or RequiredClass.Assassin or RequiredClass.Archer)
            kind += "/" + classes[0].ToLocalizedString();
        return kind;
    }

    private static bool Equipment(ItemType type) => type is ItemType.Weapon or ItemType.Armour or ItemType.Helmet or ItemType.Necklace or ItemType.Bracelet or ItemType.Ring or ItemType.Belt or ItemType.Boots or ItemType.Amulet or ItemType.Stone or ItemType.Torch;
    private static string Caption(ItemInfo[] items)
    {
        var types = items.Select(item => item.Type).Distinct().Order().ToArray();
        if (types.Length == 1) return types[0] switch { ItemType.Nothing => "特殊道具", ItemType.Awakening => "觉醒材料", ItemType.Stone => "属性石", ItemType.Potion => "药剂", _ => types[0].ToLocalizedString() };
        if (types.All(Equipment)) return "装备组合";
        if (types.Length <= 2) return string.Join("与", types.Select(type => type == ItemType.Nothing ? "特殊道具" : type == ItemType.Awakening ? "觉醒材料" : type.ToLocalizedString()));
        return "道具组合";
    }

    internal static string Chinese(string value) => Regex.Replace(Regex.Replace(value, "[<>:\"/\\\\|?*\\s]", "、"), "[0-9]+", match => long.TryParse(match.Value, out var number) ? Number(number) : string.Concat(match.Value.Select(c => "零一二三四五六七八九"[c - '0'])));
    internal static string Number(long value, bool shortTens = true)
    {
        if (value < 10) return "零一二三四五六七八九"[(int)value].ToString();
        foreach (var (unit, text) in new[] { (100000000L, "亿"), (10000L, "万"), (1000L, "千"), (100L, "百"), (10L, "十") })
        {
            if (value < unit) continue;
            var head = unit == 10 && value < 20 && shortTens ? "" : Number(value / unit);
            var remainder = value % unit;
            return head + text + (remainder == 0 ? "" : (remainder < unit / 10 ? "零" : "") + Number(remainder, false));
        }
        throw new InvalidDataException("掉落数值无法转换为中文。");
    }
}
