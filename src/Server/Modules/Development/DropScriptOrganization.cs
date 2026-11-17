using System.Text;
using System.Text.Json;
using Mir2.Hosting;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public static class DropScriptOrganization
{
    public static string Apply(ProjectPaths paths, SqliteDatabaseStorage storage, bool checkOnly = false, bool compose = false, bool supplies = false)
    {
        var world = Envir.Main;
        if (world.Running || !storage.Contains(Envir.DatabasePath)) throw new InvalidOperationException("请先正常保存并停止所选版本，再整理公共掉落。");
        if (!world.LoadDB()) throw new InvalidDataException("世界存档读取失败，未整理掉落。");
        var itemChanges = supplies ? BasicDropBalance.PrepareItems() : (() => { }, false);
        try
        {
            var result = ApplyPlan(paths, storage, checkOnly, compose, supplies, itemChanges.Item2);
            if (checkOnly) itemChanges.Item1();
            return result;
        }
        catch { itemChanges.Item1(); throw; }
    }
    private static string ApplyPlan(ProjectPaths paths, SqliteDatabaseStorage storage, bool checkOnly, bool compose, bool supplies, bool itemsChanged)
    {
        var world = Envir.Main;
        var plan = new DropScriptPlan(paths, compose, supplies);
        var writes = plan.Writes.Where(pair => !File.Exists(pair.Key) || File.ReadAllText(pair.Key) != pair.Value).ToDictionary();
        var deletes = plan.Moves.Where(pair => pair.Key != Path.Combine(paths.World, "Envir", "Drops", pair.Value)).Select(pair => pair.Key).Concat(plan.Removed)
            .Where(file => !plan.Writes.ContainsKey(file)).Distinct(StringComparer.Ordinal).ToArray();
        var pathsChanged = world.MonsterInfoList.Count(m => m.DropPath != plan.Monsters[m.Index]);
        var count = plan.Moves.Values.Concat(plan.Writes.Keys.Where(file => file.StartsWith(ProjectPaths.SafePath(paths.World, "Envir/Drops") + Path.DirectorySeparatorChar, StringComparison.Ordinal)).Select(file => Path.GetRelativePath(ProjectPaths.SafePath(paths.World, "Envir/Drops"), file))).Distinct(StringComparer.Ordinal).Count();
        var summary = $"公共掉落{(compose ? "组合" : "整理")}：{count} 个中文脚本、{writes.Count} 个文件待写入、{deletes.Length} 个旧/无效文件待移除、{pathsChanged} 个怪物引用待更新、{plan.Notes.Count} 条旧规则清理/规范记录。";
        if (checkOnly || writes.Count + deletes.Length + pathsChanged == 0 && !itemsChanged) return summary;

        var backup = ChineseWorldTranslation.Backup(paths, storage, "drop-scripts");
        var report = new { Moves = plan.Moves.Select(pair => new { Source = Path.GetRelativePath(paths.World, pair.Key), Target = "Envir/Drops/" + pair.Value }),
            Removed = plan.Removed.Select(file => Path.GetRelativePath(paths.World, file)), plan.Notes };
        ChineseWorldTranslation.Replace(Path.Combine(backup, "掉落整理报告.json"), JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var touched = writes.Keys.Concat(deletes).Distinct(StringComparer.Ordinal).ToArray();
        var oldPaths = world.MonsterInfoList.ToDictionary(m => m.Index, m => m.DropPath);
        try
        {
            storage.Transaction(() =>
            {
                foreach (var (file, text) in writes) ChineseWorldTranslation.Replace(file, Encoding.UTF8.GetBytes(text));
                foreach (var monster in world.MonsterInfoList) monster.DropPath = plan.Monsters[monster.Index];
                foreach (var file in deletes) File.Delete(file);
                var dropRoot = ProjectPaths.SafePath(paths.World, "Envir/Drops");
                foreach (var directory in Directory.GetDirectories(dropRoot, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                // 仅引用变化才保存世界，纯文本整理不重序列化离线未加载的运行派生字段。
                if (pathsChanged != 0 || itemsChanged) world.SaveDB();
            });
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var file in touched)
            {
                try
                {
                    var original = Path.Combine(backup, "world", Path.GetRelativePath(paths.World, file));
                    if (File.Exists(original)) ChineseWorldTranslation.Replace(file, File.ReadAllBytes(original));
                    else if (File.Exists(file)) File.Delete(file);
                }
                catch (Exception restore) { errors.Add(restore); }
            }
            foreach (var monster in world.MonsterInfoList) monster.DropPath = oldPaths[monster.Index];
            if (errors.Count > 1) throw new AggregateException("掉落整理失败且部分文件恢复失败，请使用整理备份恢复。", errors);
            throw;
        }
        return summary + " 已完成；完整备份及报告：" + backup;
    }
}
