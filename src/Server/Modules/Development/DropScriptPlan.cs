using System.Text;
using System.Text.RegularExpressions;
using Mir2.Hosting;
using Server.MirEnvir;

namespace Server.ConsoleHost;

internal sealed partial class DropScriptPlan
{
    internal Dictionary<string, string> Moves { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> Writes { get; } = new(StringComparer.Ordinal);
    internal Dictionary<int, string> Monsters { get; } = [];
    internal List<object> Notes { get; } = [];
    internal List<string> Removed { get; } = [];
    private readonly Dictionary<string, string> specials = new(StringComparer.Ordinal);
    private readonly ChineseWorldText text = ChineseWorldText.Load();
    private readonly string root;

    internal DropScriptPlan(ProjectPaths paths, bool compose = false, bool supplies = false)
    {
        root = ProjectPaths.SafePath(paths.World, "Envir/Drops");
        var files = ChineseWorldTranslation.WorldFiles(root).Where(f => Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        if (files.Any(file => Server.MirDatabase.DropComposition.IsComposed(File.ReadAllText(file))))
        {
            if (!compose) throw new InvalidOperationException("该版本已使用组合掉落，请通过怪物管理编辑；旧整理入口不能重写组合规则。");
            if (Envir.Main.MonsterInfoList.Any(m => !m.DropPath.StartsWith("怪物规则/", StringComparison.Ordinal)))
                throw new InvalidDataException("存在未完成的组合迁移，请先检查上次报告，未覆盖文件。");
            foreach (var file in files) Moves[file] = Relative(file);
            foreach (var monster in Envir.Main.MonsterInfoList) Monsters[monster.Index] = monster.DropPath;
            Compose(paths, supplies);
            return;
        }
        var setupPath = ProjectPaths.SafePath(paths.World, "Configs/Setup.ini");
        var ini = new InIReader(setupPath);
        var config = new Dictionary<string, string> { ["Fishing"] = "公共/系统/钓鱼/钓鱼00", ["Awakening"] = "公共/系统/觉醒", ["Strongbox"] = "公共/系统/宝箱", ["Blackstone"] = "公共/系统/黑石", ["Dragon"] = "公共/系统/龙兽" };
        var oldDefaults = new Dictionary<string, string> { ["Fishing"] = "00Fishing", ["Awakening"] = "00Awakening", ["Strongbox"] = "00Strongbox", ["Blackstone"] = "00Blackstone", ["Dragon"] = "DragonItem" };
        foreach (var (key, target) in config)
        {
            var old = ini.ReadString("DropFiles", key, oldDefaults[key], false);
            if (key == "Fishing")
            {
                for (var i = 0; i < 19; i++) specials[Resolve((old + ".txt").Replace("00", i.ToString("D2")))] = target.Replace("00", i.ToString("D2")) + ".txt";
            }
            else specials[Resolve(old + ".txt")] = target + ".txt";
        }
        var owners = Envir.Main.MonsterInfoList.GroupBy(m => Resolve((string.IsNullOrWhiteSpace(m.DropPath) ? m.Name : m.DropPath) + ".txt")).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var references = owners.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var file in files)
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"(?im)^\s*#INSERT\s+\[(.+)\]\s*$")) references.Add(Resolve(match.Groups[1].Value));
        foreach (var folder in new[] { "NPCs", "Quests", "SystemScripts" })
        {
            var directory = ProjectPaths.SafePath(paths.World, "Envir/" + folder);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in ChineseWorldTranslation.WorldFiles(directory).Where(f => Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase)))
                foreach (Match match in Regex.Matches(File.ReadAllText(file), "(?im)^\\s*DROP\\s+(?:\"([^\"]+)\"|(\\S+))")) references.Add(Resolve(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value));
        }
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in files)
        {
            var dragon = specials.GetValueOrDefault(source) == config["Dragon"] + ".txt";
            contents[source] = DropRuleText.Organize(File.ReadAllText(source, new UTF8Encoding(false, true)), dragon,
                (line, original, reason) => Notes.Add(new { File = Relative(source), Line = line, Original = original, Reason = reason }));
        }
        var names = new DropScriptNames(name => contents.GetValueOrDefault(Resolve(name)) ?? throw new InvalidDataException("掉落引用缺少实际文件：" + name));
        foreach (var source in files.OrderBy(f => !DropScriptNames.NeedsDescription(Relative(f)) || specials.ContainsKey(f) ? 0 : 1).ThenBy(f => f, StringComparer.Ordinal))
        {
            var relative = Relative(source);
            var target = specials.GetValueOrDefault(source);
            var hasRules = Regex.IsMatch(contents[source], @"(?m)^\s*(1/|\d+(?:\.\d+)?%|#INSERT)");
            if (target == null && !hasRules)
            {
                if (references.Contains(source)) Moves.Add(source, "公共/怪物/无掉落.txt");
                else Removed.Add(source);
                continue;
            }
            if (target == null && relative.StartsWith("公共/", StringComparison.Ordinal) && !DropScriptNames.NeedsDescription(relative)) target = relative;
            if (target == null)
            {
                var live = owners.GetValueOrDefault(source);
                if (live != null && !relative.StartsWith("公共/", StringComparison.Ordinal))
                {
                    var folder = string.Join('/', (Path.GetDirectoryName(relative.Replace('\\', '/')) ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).Select(DirectoryName));
                    target = "公共/怪物/" + (folder.Length == 0 ? "" : folder + "/") + live.First().Name + ".txt";
                    if (DropScriptNames.NeedsDescription(target)) target = null;
                }
            }
            target = names.Assign(target, contents[source]);
            _ = ProjectPaths.SafePath(root, target);
            Moves.Add(source, target);
        }
        foreach (var monster in Envir.Main.MonsterInfoList)
            Monsters.Add(monster.Index, Mapped((string.IsNullOrWhiteSpace(monster.DropPath) ? monster.Name : monster.DropPath) + ".txt")[..^4]);
        foreach (var (source, target) in Moves)
        {
            var content = target == "公共/怪物/无掉落.txt" ? DropRuleText.Header + "\n; 当前版本无掉落。\n" : contents[source];
            content = Regex.Replace(content, @"(?im)^\s*#INSERT\s+\[(.+)\]\s*$", m => "#INSERT [" + Mapped(m.Groups[1].Value) + "]");
            Writes.TryAdd(ProjectPaths.SafePath(root, target), content);
        }
        var proposed = Writes.ToDictionary(pair => Relative(pair.Key)[..^4], pair => pair.Value, StringComparer.Ordinal);
        var scripts = new MonsterDropScripts(paths, new DevelopmentFiles(paths));
        foreach (var (name, content) in proposed)
            if (name != config["Dragon"]) scripts.Validate(name, content, proposed);
        foreach (var (key, target) in config)
        {
            if (key == "Fishing" && !Moves.ContainsKey(Resolve(ini.ReadString("DropFiles", key, oldDefaults[key], false) + ".txt"))) throw new InvalidDataException("缺少基础钓鱼脚本，取消整理。");
            if (key != "Fishing" && !Moves.ContainsKey(Resolve(ini.ReadString("DropFiles", key, oldDefaults[key], false) + ".txt"))) throw new InvalidDataException("缺少系统掉落脚本：" + key);
        }
        // 不调用会吞掉IO异常的InIReader.Save，配置与脚本共用可回滚的原子替换。
        var setup = File.ReadAllText(setupPath);
        foreach (var (key, target) in config) setup = SetupValue(setup, key, target);
        Writes[setupPath] = setup;
        foreach (var folder in new[] { "NPCs", "Quests", "SystemScripts" })
        {
            var directory = ProjectPaths.SafePath(paths.World, "Envir/" + folder);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in ChineseWorldTranslation.WorldFiles(directory).Where(f => Path.GetExtension(f).Equals(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                var original = File.ReadAllText(file, new UTF8Encoding(false, true));
                var updated = Regex.Replace(original, "(?im)^(\\s*DROP\\s+)(?:\"([^\"]+)\"|(\\S+))", m => m.Groups[1].Value + "\"" + Mapped(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) + "\"");
                if (updated != original) Writes[file] = updated;
            }
        }
        if (compose) Compose(paths, supplies);
    }

    private string Resolve(string name) => ProjectPaths.SafePath(root, name.Replace('\\', '/'));
    private string Relative(string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    private string Mapped(string name) => Moves.GetValueOrDefault(Resolve(name)) ?? throw new InvalidDataException("掉落引用缺少实际文件：" + name);

    private string DirectoryName(string value) => text.Field(Directories.GetValueOrDefault(value, value));
    private static string SetupValue(string source, string key, string value)
    {
        var section = Regex.Match(source, @"(?m)^\[DropFiles\]\r?$[\s\S]*?(?=^\[|\z)", RegexOptions.Multiline);
        if (!section.Success) return source.TrimEnd() + "\n\n[DropFiles]\n" + key + "=" + value + "\n";
        var contents = section.Value;
        var pattern = "(?m)^" + Regex.Escape(key) + "=[^\\r\\n]*";
        contents = Regex.IsMatch(contents, pattern) ? Regex.Replace(contents, pattern, _ => key + "=" + value) : contents.TrimEnd() + "\n" + key + "=" + value + "\n\n";
        return source[..section.Index] + contents + source[(section.Index + section.Length)..];
    }
    private static readonly Dictionary<string, string> Directories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AncientCaves"]="远古洞窟", ["Provinces"]="各省野外", ["Misc"]="杂项", ["TaoVillage"]="道村", ["TreePath"]="林间小径", ["RedValley"]="赤月峡谷",
        ["WoomyonWoods"]="沃玛森林", ["WoomaTemple"]="沃玛寺庙", ["Unused"]="未启用", ["SwampCavern"]="沼泽洞穴", ["Orc"]="半兽人", ["DeadForest"]="死亡森林",
        ["GonRyun"]="昆仑", ["BichonProvince"]="比奇省", ["DeadMine"]="废弃矿洞", ["InsectCave"]="蜈蚣洞", ["MongchonProvince"]="盟重省", ["Zuma"]="祖玛寺庙",
        ["Castle-Gi"]="城堡", ["BDD"]="未知暗殿", ["OmaCave"]="半兽洞穴", ["StoneTomb"]="石墓", ["Lunar"]="赤月", ["PranjaIsland"]="般若岛", ["PranjaTemple"]="般若寺庙",
        ["PastBichon"]="过去的比奇", ["OmaValley"]="半兽峡谷", ["BugCave"]="虫洞", ["MineralMines"]="矿区", ["Prajna"]="般若", ["Horned"]="角魔", ["Numa"]="诺玛",
        ["StoneTemple"]="石庙", ["WasteLand"]="荒原", ["RedCavern"]="赤色洞窟", ["PranjaStoneCave"]="般若石窟", ["IceHell"]="冰狱", ["AntCave"]="蚂蚁洞", ["Lords"]="领主",
        ["Havok"]="浩劫", ["Swamp"]="沼泽", ["Ruins"]="遗迹", ["DarkForest"]="黑暗森林", ["ZumaTemple"]="祖玛寺庙", ["Zombies"]="僵尸", ["Mysterious"]="神秘之地",
        ["HellFire"]="地狱烈焰", ["Trolls"]="巨魔", ["OmaCavern"]="半兽洞窟", ["HellCavern"]="地狱洞窟", ["WhiteValley"]="白雪峡谷", ["SnowCavern"]="雪洞", ["Fox"]="狐狸洞",
        ["ViperCave"]="毒蛇洞", ["SerpantValley"]="毒蛇山谷", ["SeokchoValley"]="石桥峡谷", ["TrollMine"]="巨魔矿洞", ["Scrolls"]="卷轴", ["Chieftain"]="酋长", ["Jinchon"]="金川",
        ["TeenageMutantNinjaCunts"]="变异忍者", ["EvilMir"]="邪恶米尔", ["PenalCavern"]="惩罚洞窟", ["SabukTomb"]="沙巴克古墓"
    };
}
