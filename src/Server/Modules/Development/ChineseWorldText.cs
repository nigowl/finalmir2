using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Server.ConsoleHost;

public sealed class ChineseWorldText
{
    public Dictionary<string, string> Fields { get; set; } = [];
    public Dictionary<string, string> Blocks { get; set; } = [];
    public Dictionary<string, string> Lines { get; set; } = [];
    public HashSet<string> Missing { get; } = [];
    private HashSet<string> translated = [];
    public static ChineseWorldText Load()
    {
        using var stream = typeof(ChineseWorldText).Assembly.GetManifestResourceStream("Mir2.Server.Localization.World.zh-CN.json")
            ?? throw new InvalidDataException("缺少默认世界中文译文。");
        var text = JsonSerializer.Deserialize<ChineseWorldText>(stream) ?? throw new InvalidDataException("默认世界中文译文为空。");
        text.translated = text.Fields.Values.Concat(text.Blocks.Values).Select(Compact).ToHashSet();
        return text;
    }

    public string Field(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var translated = ChineseText.Name(value);
        return translated != value ? translated : Fields.GetValueOrDefault(value, Fields.GetValueOrDefault(value.Replace("\r\n", "\n"), value));
    }

    private static string Key(string value) => Regex.Replace(value, @"\s+", " ").Trim();
    private static string Compact(string value) => Regex.Replace(value, @"\s+", "");
    public bool IsTranslated(string value) => translated.Contains(Compact(value));
    private static bool Dialogue(string mode) => mode is "#SAY" or "#ELSESAY" or
        "[@DESCRIPTION]" or "[@TASKDESCRIPTION]" or "[@RETURNDESCRIPTION]" or "[@COMPLETION]";

    public string Script(string source)
    {
        var newline = source.Contains("\r\n") ? "\r\n" : "\n";
        var lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var output = new List<string>();
        var paragraph = new List<string>();
        var mode = "";
        void Flush()
        {
            if (paragraph.Count == 0) return;
            var key = Key(string.Join(" ", paragraph));
            if (Blocks.TryGetValue(key, out var text)) output.AddRange(Wrap(text));
            else
            {
                if (Regex.IsMatch(key, "[A-Za-z]") && !IsTranslated(key)) Missing.Add(key);
                output.AddRange(paragraph);
            }
            paragraph.Clear();
        }
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var text = line.Trim();
            if (text.StartsWith('[') || text.StartsWith('#'))
            {
                Flush(); mode = text.ToUpperInvariant(); output.Add(line);
                if (Dialogue(mode)) TranslateSection(lines, ref index, output);
                continue;
            }
            if (!Dialogue(mode) || text.Length == 0 || text.StartsWith(';') || text.StartsWith("//"))
            {
                Flush();
                var replacement = Lines.GetValueOrDefault(text, line);
                ValidateMarkers(line, replacement); output.Add(replacement); continue;
            }
            if (text.Contains('<') || text.Contains('{'))
            {
                Flush();
                var translated = Lines.GetValueOrDefault(text, line);
                ValidateMarkers(line, translated);
                output.Add(translated);
            }
            else paragraph.Add(line);
        }
        Flush();
        return string.Join(newline, output);
    }

    private void TranslateSection(string[] lines, ref int index, List<string> output)
    {
        var end = index + 1;
        while (end < lines.Length && !lines[end].TrimStart().StartsWith('#') && !lines[end].TrimStart().StartsWith('[')) end++;
        var source = string.Join("\n", lines[(index + 1)..end]);
        if (!Blocks.TryGetValue(Key(source), out var translated))
        {
            if (!IsTranslated(source)) return;
            output.AddRange(lines[(index + 1)..end]);
            index = end - 1;
            return;
        }
        // 跨颜色片段按整段行文，仍逐项校验原来的跳转、变量和颜色。
        ValidateMarkers(source, translated);
        foreach (var line in translated.Split('\n')) output.AddRange(Wrap(line));
        index = end - 1;
    }

    private static IEnumerable<string> Wrap(string text)
    {
        // 只重排纯正文；含跳转或变量的行保持完整，避免拆坏脚本标记。
        if (text.Contains('@') || text.Contains('/') || text.Contains('<')) return [text];
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var character in text)
        {
            current.Append(character);
            if (current.Length >= 28 && "，。！？；：、".Contains(character)) { result.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    private static void ValidateMarkers(string source, string translated)
    {
        static string[] Matches(string value, string pattern) => Regex.Matches(value, pattern).Select(m => m.Groups[1].Value).ToArray();
        if (!Matches(source, @"<[^<>]*/(@[^<>]*)>").SequenceEqual(Matches(translated, @"<[^<>]*/(@[^<>]*)>")) ||
            !Matches(source, @"(<\$[^>]+>)").SequenceEqual(Matches(translated, @"(<\$[^>]+>)")) ||
            !Matches(source, @"\{[^{}]+/([A-Za-z]+)\}").SequenceEqual(Matches(translated, @"\{[^{}]+/([A-Za-z]+)\}")))
            throw new InvalidDataException("中文译文改变了脚本跳转、变量或颜色标记：" + source);
    }
}
