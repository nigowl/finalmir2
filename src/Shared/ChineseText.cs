using System.Text.Json;

// 只翻译原版定义和界面枚举，不用于玩家姓名、协议指令或资源路径。
public static class ChineseText
{
    private static readonly Dictionary<string, string> Names = Read("Names");
    private static readonly Dictionary<string, string> Aliases = Names
        .GroupBy(p => p.Key.Replace(" ", ""), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
    internal static Dictionary<string, string> Read(string name)
    {
        using var source = typeof(ChineseText).Assembly.GetManifestResourceStream($"Mir2.Shared.Localization.{name}.zh-CN.json")
            ?? throw new InvalidDataException($"缺少中文文本资源：{name}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(source)
            ?? throw new InvalidDataException($"中文文本资源为空：{name}");
    }

    public static string Name(string original) => original != null && Names.TryGetValue(original, out var value) ? value : original;
    // 原版有物品与怪物共用英文名称，按物品查找时不能命中怪物译名。
    public static string ItemName(string original) => original != null && Names.TryGetValue("Item:" + original, out var value) ? value : Name(original);
    public static string Alias(string original) => original != null && Aliases.TryGetValue(original.Replace(" ", ""), out var value) ? value : original;
    public static string ItemAlias(string original) => original != null && Aliases.TryGetValue("Item:" + original.Replace(" ", ""), out var value) ? value : Alias(original);
    public static bool Matches(string current, string reference) =>
        string.Equals(current.Replace(" ", ""), reference.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(current.Replace(" ", ""), Alias(reference).Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
}
