using System.Globalization;

namespace Server.MirDatabase;

// 组合只合并同一层的普通条目，不能把任务条件或随机分组摊平成无条件掉落。
public static class DropComposition
{
    public const string Header = "; MIR2-DROP-COMPOSE 1";
    public const string OwnRules = "; MIR2-MONSTER-RULES";
    public static bool IsComposed(string content) => content.Replace("\r\n", "\n").Split('\n').Any(line => line.Trim() == Header);

    public static string Document(IEnumerable<string> scripts, string content) => Header + "\n" +
        string.Concat(scripts.Select(script => "#INSERT [" + script + ".txt]\n")) + OwnRules + "\n" + content;

    public static List<DropInfo> Merge(IEnumerable<DropInfo> rules)
    {
        var result = new List<DropInfo>();
        var seen = new Dictionary<(int?, bool, byte), DropInfo>();
        foreach (var drop in rules)
        {
            if (drop.GroupedDrop != null) { result.Add(drop); continue; }
            var key = (drop.Item?.Index, drop.QuestRequired, drop.Type);
            if (!seen.TryGetValue(key, out var existing))
            {
                existing = new DropInfo { Chance = drop.Chance, Probability = drop.Probability, Item = drop.Item,
                    Gold = drop.Gold, Count = drop.Count, QuestRequired = drop.QuestRequired, Type = drop.Type };
                seen.Add(key, existing); result.Add(existing); continue;
            }
            existing.Probability = Math.Min(1m, existing.BaseProbability + drop.BaseProbability);
            if (existing.Item != null)
            {
                existing.Count = checked(existing.Count + drop.Count);
                if (existing.Count > 100) throw new InvalidDataException($"{existing.Item.Name} 合并数量超过100份。");
            }
            else
            {
                existing.Gold = checked(existing.Gold + drop.Gold);
                if (existing.Gold > 100000000) throw new InvalidDataException("合并金币基数超过100000000。");
            }
        }
        return result;
    }

    public static string Format(IEnumerable<DropInfo> rules) => string.Concat(rules.Select(drop => Line(drop) + "\n"));
    public static string Preview(IEnumerable<DropInfo> rules) => string.Concat(rules.Select(drop => Line(drop, true) + "\n"));
    public static string Line(DropInfo drop) => Line(drop, false);
    private static string Line(DropInfo drop, bool preview)
    {
        var chance = drop.Probability.HasValue ? (drop.Probability.Value * 100m).ToString("0.##########################", CultureInfo.InvariantCulture) + "%" : "1/" + drop.Chance;
        if (preview)
        {
            var percent = drop.BaseProbability * 100m;
            chance = percent > 0 && percent < .001m ? "<0.001%" : decimal.Round(percent, 3, MidpointRounding.AwayFromZero).ToString("0.000", CultureInfo.InvariantCulture) + "%";
        }
        if (drop.GroupedDrop != null) return chance + (drop.GroupedDrop.Random ? " GROUP*" : drop.GroupedDrop.First ? " GROUP^" : " GROUP") + "\n{\n" + (preview ? Preview(drop.GroupedDrop) : Format(drop.GroupedDrop)) + "}";
        if (drop.Gold > 0) return chance + " Gold " + drop.Gold;
        return chance + " " + drop.Item.Name + (drop.Count == 1 ? "" : " " + drop.Count) + (drop.QuestRequired ? " Q" : "");
    }
}
