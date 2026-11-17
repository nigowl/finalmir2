using System.Text.RegularExpressions;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

internal static class DropRuleText
{
    internal const string Header = "; MIR2-DROP-RULES 1";

    internal static string ItemNames(string source) => Regex.Replace(source, @"(?m)^(\s*(?:1/\d+|\d+(?:\.\d+)?%)\s+)(\S+)", match =>
        match.Groups[1].Value + (Envir.Main.GetItemInfo(match.Groups[2].Value)?.Name ?? match.Groups[2].Value));

    internal static string Organize(string source, bool dragon, Action<int, string, string> note)
    {
        var legacy = !source.Split('\n').Any(line => line.Trim() == Header);
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith(';') || line is "{" or "}" || line.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var reason = "原规则不能解析";
            if (parts.Length >= 2 && ((parts[0].StartsWith("1/") && int.TryParse(parts[0][2..], out var chance) && chance > 0) || (!dragon && parts[0].EndsWith("%") && DropInfo.FromLine(parts[0] + " GROUP") != null)))
            {
                if (parts[1].Equals("Gold", StringComparison.OrdinalIgnoreCase) && parts.Length >= (dragon ? 4 : 3) && uint.TryParse(parts[2], out var gold) && gold is > 0 and <= 100000000 &&
                    (!dragon || byte.TryParse(parts[3], out var goldLevel) && goldLevel > 0 && goldLevel <= Globals.MaxDragonLevel))
                {
                    lines[index] = string.Join(' ', parts.Take(dragon ? 4 : 3));
                    continue;
                }
                if (!dragon && parts[1].ToUpperInvariant() is "GROUP" or "GROUP*" or "GROUP^")
                {
                    lines[index] = parts[0] + " " + parts[1];
                    continue;
                }
                var item = Envir.Main.GetItemInfo(parts[1]);
                if (item != null)
                {
                    if (Regex.IsMatch(item.Name, "[A-Za-z]")) throw new InvalidDataException("当前版本物品尚无中文名称：" + item.Name);
                    if (dragon)
                    {
                        // 龙兽第三列是等级，不能按普通掉落的数量重写。
                        if (parts.Length >= 3 && byte.TryParse(parts[2], out var level) && level > 0 && level <= Globals.MaxDragonLevel)
                        {
                            lines[index] = string.Join(' ', parts.Take(3).Select((part, i) => i == 1 ? item.Name : part));
                            continue;
                        }
                        note(index + 1, lines[index], "龙兽掉落等级无效"); lines[index] = ""; continue;
                    }
                    var effective = line;
                    if (legacy)
                    {
                        var count = parts.Skip(2).FirstOrDefault(p => p.StartsWith("COUNT=", StringComparison.OrdinalIgnoreCase));
                        var quest = count == null ? parts.ElementAtOrDefault(2)?.Equals("Q", StringComparison.OrdinalIgnoreCase) == true : parts.Skip(2).Any(p => p.Equals("Q", StringComparison.OrdinalIgnoreCase));
                        effective = $"{parts[0]} {parts[1]}" + (quest ? " Q" : "") + (count == null ? "" : " " + count);
                        if (parts.Skip(2).Any(p => int.TryParse(p, out _))) note(index + 1, lines[index], "旧纯数字原先不表示数量，保留实际每次1份的行为");
                    }
                    var drop = DropInfo.FromLine(effective);
                    if (drop != null)
                    {
                        lines[index] = $"{parts[0]} {item.Name}" + (drop.QuestRequired ? " Q" : "") + (drop.Count == 1 ? "" : " " + drop.Count);
                        continue;
                    }
                }
                else reason = "当前版本未定义此物品，已忽略";
            }
            note(index + 1, lines[index], reason);
            lines[index] = "";
        }
        var content = string.Join('\n', lines);
        return legacy ? Header + "\n" + content : content;
    }
}
