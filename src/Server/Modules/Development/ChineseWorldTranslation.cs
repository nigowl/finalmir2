using System.Text;
using System.Text.RegularExpressions;
using Mir2.Hosting;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public static class ChineseWorldTranslation
{
    public static string Apply(ProjectPaths paths, SqliteDatabaseStorage storage, bool checkOnly = false)
    {
        if (Envir.Main.Running || !storage.Contains(Envir.DatabasePath))
            throw new InvalidOperationException("汉化需要先正常保存并停止所选版本，且已有完整世界存档。");
        var text = ChineseWorldText.Load();
        var world = Envir.Main;
        if (!world.LoadDB()) throw new InvalidDataException("无法读取世界存档，已取消汉化。");
        var changes = new List<Action>();
        var missing = new SortedSet<string>();
        void Plan(string original, Action<string> set, string? translated = null)
        {
            var value = translated ?? text.Field(original);
            if (value == original)
            {
                if (original != null && Regex.IsMatch(original, "[A-Za-z]") && !text.IsTranslated(original)) missing.Add(original);
                return;
            }
            changes.Add(() => set(value));
        }
        ValidateNames(world.ItemInfoList.Select(i => i.Name), ChineseText.ItemName);
        ValidateNames(world.MonsterInfoList.Select(m => m.Name), text.Field);
        foreach (var item in world.ItemInfoList) { Plan(item.Name, v => item.Name = v, ChineseText.ItemName(item.Name)); Plan(item.ToolTip, v => item.ToolTip = v); }
        foreach (var monster in world.MonsterInfoList)
        {
            var original = monster.Name;
            if (text.Field(original) != original && string.IsNullOrWhiteSpace(monster.DropPath))
                changes.Add(() => monster.DropPath = original);
            Plan(original, v => monster.Name = v);
        }
        foreach (var map in world.MapInfoList) Plan(map.Title, v => map.Title = v);
        foreach (var npc in world.NPCInfoList) Plan(npc.Name, v => npc.Name = v);
        foreach (var magic in world.MagicInfoList) Plan(magic.Name, v => magic.Name = v);
        foreach (var quest in world.QuestInfoList)
        {
            Plan(quest.Name, v => quest.Name = v); Plan(quest.Group, v => quest.Group = v);
            Plan(quest.GotoMessage, v => quest.GotoMessage = v); Plan(quest.KillMessage, v => quest.KillMessage = v);
            Plan(quest.ItemMessage, v => quest.ItemMessage = v); Plan(quest.FlagMessage, v => quest.FlagMessage = v);
        }
        foreach (var item in world.GameShopList) { Plan(item.Class, v => item.Class = v); Plan(item.Category, v => item.Category = v); }
        foreach (var conquest in world.ConquestInfoList)
        {
            Plan(conquest.Name, v => conquest.Name = v);
            foreach (var guard in conquest.ConquestGuards) Plan(guard.Name, v => guard.Name = v);
            foreach (var gate in conquest.ConquestGates) Plan(gate.Name, v => gate.Name = v);
            foreach (var wall in conquest.ConquestWalls) Plan(wall.Name, v => wall.Name = v);
            foreach (var siege in conquest.ConquestSieges) Plan(siege.Name, v => siege.Name = v);
            foreach (var flag in conquest.ConquestFlags.Concat(conquest.ControlPoints)) Plan(flag.Name, v => flag.Name = v);
        }
        if (missing.Count > 0) throw new InvalidDataException("以下世界文案缺少译文，尚未写入：\n" + string.Join("\n", missing));
        var files = PrepareFiles(paths, text);
        if (checkOnly || changes.Count + files.Count == 0) return $"中文预检通过：{changes.Count} 项定义字段、{files.Count} 个文本文件待更新。";

        var backup = Backup(paths, storage, "chinese");
        var applied = new List<string>();
        try
        {
            foreach (var file in files)
            {
                Replace(file.Key, Encoding.UTF8.GetBytes(file.Value)); applied.Add(file.Key);
            }
            foreach (var change in changes) change();
            // 只写世界快照，账户、角色、行会、物品实例和托管快照不进入此事务。
            storage.Transaction(world.SaveDB);
        }
        catch
        {
            foreach (var file in applied)
            {
                var original = Path.Combine(backup, "world", Path.GetRelativePath(paths.World, file));
                if (File.Exists(original)) Replace(file, File.ReadAllBytes(original));
                else File.Delete(file);
            }
            if (!world.LoadDB()) throw new InvalidDataException("汉化失败且无法重读原世界，请从汉化备份恢复。");
            throw;
        }
        return $"已汉化 {changes.Count} 项定义字段与 {files.Count} 个文本文件；完整备份：{backup}";
    }

    private static void ValidateNames(IEnumerable<string> names, Func<string, string> translate)
    {
        var duplicate = names.Distinct(StringComparer.OrdinalIgnoreCase).GroupBy(translate, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidDataException("中文名称发生冲突：" + string.Join("、", duplicate));
    }

    private static Dictionary<string, string> PrepareFiles(ProjectPaths paths, ChineseWorldText text)
    {
        var files = new Dictionary<string, string>();
        foreach (var file in WorldFiles(paths.World))
        {
            if (Path.GetExtension(file).ToLowerInvariant() is not (".txt" or ".ini")) continue;
            var relative = Path.GetRelativePath(paths.World, file).Replace(Path.DirectorySeparatorChar, '/');
            var original = File.ReadAllText(file, new UTF8Encoding(false, true));
            var value = text.Field(original);
            if (relative.StartsWith("Envir/NPCs/") || relative.StartsWith("Envir/Quests/") || relative.StartsWith("Envir/SystemScripts/")) value = text.Script(original);
            else if (relative.StartsWith("Configs/")) value = Configuration(original, text, relative == "Configs/Language.ini");
            if (original != value) files.Add(file, value);
        }
        var language = Path.Combine(AppContext.BaseDirectory, "Localization", "Chinese.json");
        var destination = Path.Combine(paths.World, "Localization", "Chinese.json");
        if (!File.Exists(destination) || File.ReadAllText(destination) != File.ReadAllText(language)) files[destination] = File.ReadAllText(language);
        if (text.Missing.Count > 0) throw new InvalidDataException("以下脚本正文缺少译文，尚未写入：\n" + string.Join("\n", text.Missing));
        return files;
    }

    private static string Configuration(string source, ChineseWorldText text, bool languageFile) => Regex.Replace(source, @"(?m)^([^=\r\n]+)=([^\r\n]*)", m =>
    {
        var key = m.Groups[1].Value; var value = m.Groups[2].Value;
        if (key == "Language") return key + "=Chinese";
        if (languageFile || key.EndsWith("Name", StringComparison.Ordinal) || key.StartsWith("Zuma", StringComparison.Ordinal) ||
            Regex.IsMatch(key, @"Mob\d*$") || key is "HeroName" or "ItemName")
            return key + "=" + text.Field(value);
        return m.Value;
    });

    internal static string Backup(ProjectPaths paths, SqliteDatabaseStorage storage, string prefix)
    {
        var backup = Path.Combine(paths.Runtime, "backups", prefix + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        Directory.CreateDirectory(backup);
        storage.Backup(Path.Combine(backup, "mir2.sqlite3"));
        foreach (var file in WorldFiles(paths.World))
        {
            // 原Windows导入可能有反斜杠文件名，备份必须逐字保留，不能归并路径。
            var target = Path.Combine(backup, "world", Path.GetRelativePath(paths.World, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        return backup;
    }

    internal static IEnumerable<string> WorldFiles(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("汉化世界目录不能包含符号链接：" + entry);
            if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            else yield return entry;
        }
    }

    internal static void Replace(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid() + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
