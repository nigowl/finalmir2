using System.Text.RegularExpressions;
using Mir2.Hosting;
using Mir2.Management;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed class MonsterDrops(ProjectPaths paths, DevelopmentFiles files, WorldDefinitions definitions, AdminChanges changes)
{
    private readonly MonsterDropScripts scripts = new(paths, files);
    private static MonsterInfo Monster(int id) => Envir.Main.GetMonsterInfo(id) ?? throw new FileNotFoundException("未找到怪物。");
    private string Source(MonsterInfo monster) => scripts.Normalize(string.IsNullOrWhiteSpace(monster.DropPath) ? monster.Name : monster.DropPath);
    public MonsterDropDocument Read(int id)
    {
        var monster = Monster(id); var source = Source(monster); var document = scripts.Read(source);
        var selected = Array.Empty<string>(); var content = "";
        if (DropComposition.IsComposed(document.Content))
        {
            var parts = document.Content.Replace("\r\n", "\n").Split(DropComposition.OwnRules + "\n", 2);
            if (parts.Length != 2) throw new InvalidDataException("组合脚本缺少怪物规则分隔标记。");
            selected = Regex.Matches(parts[0], @"(?im)^#INSERT\s+\[(.+)\.txt\]\s*$").Select(m => scripts.Normalize(m.Groups[1].Value)).ToArray();
            content = parts[1];
        }
        else if (document.Version.Length > 0)
        {
            if (MonsterDropScripts.Public(source)) selected = [source]; else content = document.Content;
        }
        var version = Version(monster);
        var preview = Preview(id, new(selected, content, version));
        return new(id, monster.Name, source, selected, content, preview.FinalContent, version, preview.Version, scripts.List(), monster.Drops.Count, preview.Rules);
    }
    private string Version(MonsterInfo monster) => AdminChanges.Version(new {
        Definition = definitions.Read("monsters", monster.Index).Version, Source = Source(monster),
        Dependencies = scripts.Revisions(Source(monster)), Own = scripts.Read(MonsterDropScripts.Own(monster.Index)).Version });

    private (string Text, Dictionary<string,string> Dependencies) Draft(int id, MonsterDropEdit edit)
    {
        if (edit.Scripts == null || edit.Content == null || edit.Scripts.Length > 256) throw new InvalidDataException("最多选择256份公共脚本，规则文本不能为空值。");
        var selected = edit.Scripts.Select(scripts.Normalize).ToArray();
        if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Length) throw new InvalidDataException("不能重复选择同一公共脚本。");
        if (selected.Any(name => !MonsterDropScripts.Public(name))) throw new InvalidDataException("请选择公共脚本，不能引用其他怪物规则或专用系统脚本。");
        // 自身规则的引用统一由选择器管理，避免隐藏的脚本叠加或引用其他怪物。
        if (Regex.IsMatch(edit.Content, @"(?im)^\s*#INSERT\b") || edit.Content.Contains(DropComposition.Header) || edit.Content.Contains(DropComposition.OwnRules))
            throw new InvalidDataException("怪物规则只填写实际掉落条目，公共脚本请通过上方选择器添加。");
        var text = DropComposition.Document(selected, DropRuleText.ItemNames(edit.Content));
        var versions = scripts.Validate(MonsterDropScripts.Own(id), text);
        return (text, versions);
    }
    public MonsterDropPreview Preview(int id, MonsterDropEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        AdminChanges.Match(Version(Monster(id)), edit.Version);
        var (text, dependencies) = Draft(id, edit);
        var rules = DropInfo.Parse(text, "掉落预览");
        return new(DropComposition.Preview(rules), AdminChanges.Version(new { text, dependencies }), rules.Count);
    }
    public MonsterDropDocument Save(int id, MonsterDropEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var monster = Monster(id); var preview = Preview(id, edit);
        AdminChanges.Match(preview.Version, edit.PreviewVersion);
        var (text, dependencies) = Draft(id, edit); var target = MonsterDropScripts.Own(id);
        if (Envir.Main.MonsterInfoList.Any(m => m.Index != id && m.DropPath.Replace('\\', '/') == target))
            throw new InvalidDataException("该怪物规则被其他怪物引用，请先解除引用。");
        var original = scripts.Read(target); var oldPath = monster.DropPath; var oldDrops = monster.Drops.ToArray(); var written = false;
        changes.Commit(() =>
        {
            AdminChanges.Match(Version(monster), edit.Version);
            foreach (var dependency in dependencies) AdminChanges.Match(scripts.Read(dependency.Key).Version, dependency.Value);
            written = true; files.Save(new(original.Path, text, original.Version));
            var loaded = new List<DropInfo>();
            DropInfo.Load(loaded, monster.Name, ProjectPaths.SafePath(paths.World, scripts.FileName(target)), createIfNotExists: false);
            monster.DropPath = target; monster.Drops.Clear(); monster.Drops.AddRange(loaded);
        }, () =>
        {
            monster.DropPath = oldPath; monster.Drops.Clear(); monster.Drops.AddRange(oldDrops);
            if (!written) return;
            var path = ProjectPaths.SafePath(paths.World, original.Path);
            if (original.Version.Length == 0) File.Delete(path); else AssetCatalog.AtomicText(path, original.Content);
        });
        return Read(id);
    }
}
