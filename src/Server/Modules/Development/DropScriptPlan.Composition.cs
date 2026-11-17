using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mir2.Hosting;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

internal sealed partial class DropScriptPlan
{
    private void Compose(ProjectPaths paths, bool supplies = false)
    {
        var existing = ChineseWorldTranslation.WorldFiles(root).Where(file => Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase)).ToArray();
        string Read(string name) => Writes.GetValueOrDefault(Resolve(name)) ?? File.ReadAllText(Resolve(name));
        string MappedSource(string name) => Moves.GetValueOrDefault(Resolve(name)) ?? name;
        var expected = Monsters.ToDictionary(pair => pair.Key, pair => DropComposition.Merge(DropInfo.Parse(Read(pair.Value + ".txt"), pair.Value, read: Read)));
        if (supplies)
            foreach (var monster in Envir.Main.MonsterInfoList) expected[monster.Index] = BasicDropBalance.Rebuild(monster, expected[monster.Index]).ToList();
        var output = new Dictionary<string,string>(StringComparer.Ordinal);
        var names = new Dictionary<string,string>(StringComparer.Ordinal);
        var destinations = new Dictionary<string,string>(StringComparer.Ordinal);
        string Assign(string folder, string label, DropInfo[] rules)
        {
            rules = Ordered(rules).ToArray();
            var content = DropComposition.Format(rules);
            var signature = folder + "\n" + content;
            if (names.TryGetValue(signature, out var assigned)) return assigned;
            var suffix = ProbabilitySuffix(rules);
            var details = rules.Select(drop => (drop.Item?.Name ?? "金币" + drop.Gold) + (drop.Count == 1 ? "" : "×" + drop.Count)).ToArray();
            var brief = string.Join("与", details.Take(2)) + (details.Length > 2 ? "等" + details.Length + "种" : "");
            var candidates = new List<string> { label, brief };
            foreach (var drop in rules)
            {
                var item = drop.Item?.Name ?? "金币" + drop.Gold;
                var reciprocal = decimal.Round(1m / drop.BaseProbability, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
                candidates.Add(brief + "·" + item + "每次" + (drop.Item == null ? drop.Gold : drop.Count) + "份基率约1比" + reciprocal);
            }
            candidates.Add(label + "·" + string.Join("、", rules.Select(drop => (drop.Item?.Name ?? "金币" + drop.Gold) + "数量" + drop.Count + "基率约1比" + decimal.Round(1m / drop.BaseProbability, 0, MidpointRounding.AwayFromZero))));
            foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
            {
                var clean = Regex.Replace(candidate, "[<>:\"/\\\\|?*\\s]", "、");
                if (Regex.IsMatch(clean, "[A-Za-z]")) continue;
                var name = "公共/" + folder + "/" + clean + suffix + ".txt";
                if (name.Length > 160 || Encoding.UTF8.GetByteCount(clean + suffix + ".txt") > 240) continue;
                if (destinations.TryGetValue(name, out var previous) && previous != content) continue;
                output[name] = content; destinations[name] = content; names[signature] = name[..^4]; return name[..^4];
            }
            throw new InvalidDataException("基础公共掉落无法用内容区分名称：" + brief);
        }
        string PublicKey(string folder, DropInfo[] rules) => folder + "\n" + DropComposition.Format(Ordered(rules));
        var selections = new Dictionary<int, List<(string Folder, string Label, DropInfo[] Rules)>>();
        foreach (var monster in Envir.Main.MonsterInfoList.OrderBy(m => m.Index))
        {
            var rules = expected[monster.Index]; var selected = new List<(string Folder, string Label, DropInfo[] Rules)>();
            var gold = rules.Where(drop => drop.GroupedDrop == null && drop.Gold > 0 && !drop.QuestRequired).ToArray();
            var medicine = rules.Where(BasicSupply).ToArray();
            if (gold.Length > 0) selected.Add(("金币", "金币" + gold.Sum(drop => (long)drop.Gold), gold));
            if (medicine.Length > 0) selected.Add(("补给", "基础补给", medicine));
            selections[monster.Index] = selected;
        }
        var usage = selections.SelectMany(pair => pair.Value.Select(selection => (Id: pair.Key, Key: PublicKey(selection.Folder, selection.Rules))))
            .GroupBy(value => value.Key).ToDictionary(group => group.Key, group => group.Select(value => value.Id).Distinct().Count(), StringComparer.Ordinal);
        foreach (var (id, selected) in selections)
        {
            var kept = selected.Where(selection => usage[PublicKey(selection.Folder, selection.Rules)] >= 3).ToArray();
            var own = expected[id].Except(kept.SelectMany(selection => selection.Rules));
            var target = MonsterDropScripts.Own(id);
            output[target + ".txt"] = DropComposition.Document(kept.Select(selection => Assign(selection.Folder, selection.Label, selection.Rules)), DropComposition.Format(Ordered(own)));
            Monsters[id] = target;
        }
        var setupPath = ProjectPaths.SafePath(paths.World, "Configs/Setup.ini");
        var setup = Writes.GetValueOrDefault(setupPath) ?? File.ReadAllText(setupPath);
        var ini = new InIReader(setupPath);
        var specialNames = new Dictionary<string,string> { ["Fishing"]="系统/钓鱼00", ["Awakening"]="系统/觉醒", ["Strongbox"]="系统/宝箱", ["Blackstone"]="系统/黑石", ["Dragon"]="系统/龙兽" };
        var defaults = new Dictionary<string,string> { ["Fishing"]="00Fishing", ["Awakening"]="00Awakening", ["Strongbox"]="00Strongbox", ["Blackstone"]="00Blackstone", ["Dragon"]="DragonItem" };
        var redirects = new Dictionary<string,string>(StringComparer.Ordinal);
        void Preserve(string source, string target)
        {
            if (!redirects.TryAdd(source, target)) return;
            var content = Read(source);
            content = Regex.Replace(content, @"(?im)^\s*#INSERT\s+\[(.+)\]\s*$", match => {
                var reference = match.Groups[1].Value.Replace('\\', '/');
                var destination = "系统/引用/" + Path.GetFileName(reference);
                Preserve(reference, destination); return "#INSERT [" + destination + "]";
            });
            if (output.TryGetValue(target, out var old) && old != content) throw new InvalidDataException("系统引用名称冲突：" + target);
            output[target] = content;
        }
        foreach (var (key, target) in specialNames)
        {
            var source = ini.ReadString("DropFiles", key, defaults[key], false) + ".txt";
            for (var index = 0; index < (key == "Fishing" ? 19 : 1); index++)
            {
                var original = key == "Fishing" ? source.Replace("00", index.ToString("D2")) : source;
                if (!Moves.ContainsKey(Resolve(original)) && !File.Exists(Resolve(original))) continue;
                Preserve(MappedSource(original), (key == "Fishing" ? target.Replace("00", index.ToString("D2")) : target) + ".txt");
            }
            setup = SetupValue(setup, key, target);
        }
        // NPC的DROP不是怪物附属规则，留在事件目录；不能因收缩公共库丢失原事件奖励。
        foreach (var folder in new[] { "NPCs", "Quests", "SystemScripts" })
        {
            var directory = ProjectPaths.SafePath(paths.World, "Envir/" + folder);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in ChineseWorldTranslation.WorldFiles(directory).Where(file => Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                var original = Writes.GetValueOrDefault(file) ?? File.ReadAllText(file);
                var updated = Regex.Replace(original, "(?im)^(\\s*DROP\\s+)(?:\"([^\"]+)\"|(\\S+))", match => {
                    var source = MappedSource(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
                    if (redirects.TryGetValue(source, out var system)) return match.Groups[1].Value + "\"" + system + "\"";
                    var target = "事件/" + Path.GetFileName(source);
                    var rules = DropInfo.Parse(Read(source), source, read: Read);
                    var text = DropComposition.Format(Ordered(rules));
                    var content = DropComposition.IsComposed(Read(source)) ? DropComposition.Document([], text) : text;
                    if (output.TryGetValue(target, out var old) && old != content) throw new InvalidDataException("事件掉落名称冲突：" + target);
                    output[target] = content; return match.Groups[1].Value + "\"" + target + "\"";
                });
                if (updated != original) Writes[file] = updated;
            }
        }
        foreach (var path in Writes.Keys.Where(file => file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray()) Writes.Remove(path);
        Moves.Clear(); Removed.AddRange(existing);
        foreach (var (name, content) in output) Writes[Resolve(name)] = content;
        Writes[setupPath] = setup;
        var scripts = new MonsterDropScripts(paths, new DevelopmentFiles(paths));
        var proposed = output.ToDictionary(pair => pair.Key[..^4], pair => pair.Value, StringComparer.Ordinal);
        foreach (var (id, path) in Monsters)
        {
            scripts.Validate(path, output[path + ".txt"], proposed);
            var actual = DropInfo.Parse(output[path + ".txt"], path, read: name => output[name]);
            if (Signature(actual) != Signature(expected[id])) throw new InvalidDataException("基础组合改变了怪物最终掉落：" + id);
        }
        foreach (var (name, content) in output.Where(pair => pair.Key.StartsWith("系统/", StringComparison.Ordinal) && pair.Key != "系统/龙兽.txt"))
            scripts.Validate(name[..^4], content, proposed);
        Notes.Add(new { Composition = $"公共仅保留{usage.Count(pair => pair.Value >= 3)}份至少3怪物使用的金币/普通补给；{Monsters.Count}怪物各用0–2份公共脚本，其余均为自身规则。文件名统一概率四舍五入为整数，低于1%用0、混合概率无后缀，正文精确概率不变。" });
    }

    private static IEnumerable<DropInfo> Ordered(IEnumerable<DropInfo> rules) => rules.OrderBy(drop => drop.Item?.Index ?? (drop.Gold > 0 ? int.MaxValue - 1 : int.MaxValue))
        .ThenBy(drop => drop.QuestRequired).ThenBy(drop => drop.Type);
    private static string Signature(IEnumerable<DropInfo> drops) => string.Join('\n', drops.Select(DropComposition.Line).Order(StringComparer.Ordinal));
    private static bool BasicSupply(DropInfo drop) => drop.GroupedDrop == null && !drop.QuestRequired && drop.Item != null &&
        (drop.Item.Type == ItemType.Potion && drop.Item.Shape is 0 or 1 && drop.Item.Stats[Stat.HP] >= 0 && drop.Item.Stats[Stat.MP] >= 0 &&
            (drop.Item.Stats[Stat.HP] > 0 || drop.Item.Stats[Stat.MP] > 0) || drop.Item.Type == ItemType.Scroll && drop.Item.Shape is 1 or 2);
    internal static string ProbabilitySuffix(IEnumerable<DropInfo> rules)
    {
        var values = rules.ToArray();
        if (values.Length == 0 || values.Any(drop => drop.GroupedDrop != null)) return "";
        var probabilities = values.Select(drop => drop.BaseProbability).Distinct().ToArray();
        return probabilities.Length != 1 ? "" : "-" + (probabilities[0] < .01m ? "0" : decimal.Round(probabilities[0] * 100, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture));
    }
}
