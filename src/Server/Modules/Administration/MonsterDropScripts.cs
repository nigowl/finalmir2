using System.Text;
using System.Text.RegularExpressions;
using Mir2.Hosting;
using Mir2.Management;
using Server.MirDatabase;

namespace Server.ConsoleHost;

// 校验边界与原FromLine共用物品/概率语义，执行仍由原DropInfo.Load完成。
public sealed class MonsterDropScripts(ProjectPaths paths, DevelopmentFiles files)
{
    public string Root => ProjectPaths.SafePath(paths.World,
        Path.GetRelativePath(paths.World, Path.GetFullPath(Settings.DropPath.Replace('\\', '/'), paths.World)));
    public static string Own(int id) => $"怪物规则/怪物-{id}";
    public static bool Public(string script) => !script.StartsWith("怪物规则/", StringComparison.Ordinal) &&
        !script.StartsWith("Managed/Monster-", StringComparison.Ordinal) && !script.StartsWith("公共/系统/", StringComparison.Ordinal) &&
        !script.StartsWith("系统/", StringComparison.Ordinal) && !script.StartsWith("事件/", StringComparison.Ordinal);
    public string Normalize(string script)
    {
        if (string.IsNullOrWhiteSpace(script) || script.Length > 160 || script.Any(char.IsControl))
            throw new InvalidDataException("掉落脚本路径无效。");
        var normalized = script.Trim().Replace('\\', '/');
        ProjectPaths.SafePath(Root, normalized + ".txt");
        return normalized;
    }
    public string FileName(string script) => Path.GetRelativePath(paths.World,
        ProjectPaths.SafePath(Root, Normalize(script) + ".txt")).Replace('\\', '/');
    public TextDocument Read(string script) => files.Read(FileName(script));
    public string[] List() => files.List(Path.GetRelativePath(paths.World, Root).Replace('\\', '/') + "/")
        .Where(path => path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        .Select(path => Path.GetRelativePath(Root, ProjectPaths.SafePath(paths.World, path)).Replace('\\', '/')[..^4])
        .Distinct(StringComparer.Ordinal).Where(Public)
        .Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public Dictionary<string, string> Revisions(string source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal); var count = 0;
        Visit(Normalize(source), 0); return result;
        void Visit(string name, int depth)
        {
            if (result.ContainsKey(name)) return;
            if (depth > 16 || result.Count >= 512) throw new InvalidDataException("掉落引用超过读取上限。");
            var document = Read(name); result[name] = document.Version;
            foreach (var line in document.Content.Split('\n'))
            {
                if (++count > 10000) throw new InvalidDataException("掉落引用超过10000行。");
                var match = Regex.Match(line.Trim(), @"^#INSERT\s+\[(.+)\.txt\]$", RegexOptions.IgnoreCase);
                if (match.Success) Visit(Normalize(match.Groups[1].Value), depth + 1);
            }
        }
    }

    public Dictionary<string, string> Validate(string script, string? content = null, IReadOnlyDictionary<string, string>? proposed = null)
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var lines = 0; var quantity = 0;
        Visit(Normalize(script), content, 0);
        return versions;

        void Visit(string name, string? replacement, int depth)
        {
            if (depth > 16 || !visiting.Add(name)) throw new InvalidDataException("掉落脚本循环引用或超过16层。");
            var document = Read(name);
            if (replacement == null) proposed?.TryGetValue(name, out replacement);
            if (replacement == null && document.Version.Length == 0) throw new InvalidDataException($"掉落脚本不存在：{name}.txt");
            var text = replacement ?? document.Content;
            if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024 || text.Contains('\0')) throw new InvalidDataException("掉落文本无效或超过1兆字节。");
            if (!versions.ContainsKey(name) && versions.Count >= 512) throw new InvalidDataException("掉落引用超过512份文件。 ");
            versions[name] = document.Version;
            var groups = 0; var pending = false; var number = 0;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                number++; if (++lines > 10000) throw new InvalidDataException("掉落脚本展开后超过10000行。");
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';')) continue;
                void Invalid(string message) => throw new InvalidDataException($"{name}.txt 第{number}行：{message}");
                if (line == "{") { if (!pending) Invalid("缺少GROUP规则。"); if (++groups > 16) Invalid("分组不能超过16层。"); pending = false; continue; }
                if (pending) Invalid("GROUP下一条内容必须为左花括号。");
                if (line == "}") { if (--groups < 0) Invalid("多余的右花括号。"); continue; }
                if (line.StartsWith("#INSERT", StringComparison.OrdinalIgnoreCase))
                {
                    if (groups > 0) Invalid("INSERT须写在分组外，引用内容会追加到脚本末尾。");
                    var match = Regex.Match(line, @"^#INSERT\s+\[(.+)\]$", RegexOptions.IgnoreCase);
                    if (!match.Success || !match.Groups[1].Value.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) Invalid("引用格式为 #INSERT [子目录/脚本.txt]。");
                    Visit(Normalize(match.Groups[1].Value[..^4]), null, depth + 1); continue;
                }
                var drop = DropInfo.FromLine(line);
                if (drop == null) Invalid("请使用 1/N 或 百分比% 物品名称 数量（1–100，可省略，默认1），任务物品追加Q；物品必须存在。");
                if (drop!.Gold > 100000000) Invalid("单条金币上限为100000000。");
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (drop.Gold > 0 && parts.Length != 3 || drop.GroupedDrop != null && parts.Length != 2) Invalid("金币/分组规则包含多余内容。");
                if (drop.GroupedDrop != null) pending = true;
                else if ((quantity += drop.Count) > 10000) Invalid("总物品数量超过10000。");
            }
            if (groups != 0 || pending) throw new InvalidDataException($"{name}.txt 的分组花括号未闭合。");
            visiting.Remove(name);
        }
    }
}
