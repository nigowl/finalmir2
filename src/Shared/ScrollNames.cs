public static class ScrollNames
{
    public static string Normalize(string name)
    {
        var suffix = System.Text.RegularExpressions.Regex.Match(name, @"(?:[（(][^（）()]*[）)]|\d+)*$").Value;
        var stem = name[..^suffix.Length];
        if (stem.EndsWith("卷", StringComparison.Ordinal)) return stem + "轴" + suffix;
        return (stem switch
        {
            "行会领地瞬息移动" => "行会领地传送卷轴",
            "瞬息移动家园" => "家园传送卷轴",
            "领地邀请" => "领地邀请卷轴",
            _ => stem
        }) + suffix;
    }
}
