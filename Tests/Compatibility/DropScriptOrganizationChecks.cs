using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class DropScriptOrganizationChecks
{
    // 独立进程入口，不借用正式SQLite；所有规则、玩家标记和故障注入均在临时版本。
    internal static void Run(Action<bool, string> check)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "mir2-drop-organize-" + Guid.NewGuid().ToString("N"));
        var paths = new ProjectPaths(temporary, Path.Combine(temporary, "runtime"));
        var cwd = Directory.GetCurrentDirectory(); var previous = DatabaseStorage.Current;
        try
        {
            Directory.CreateDirectory(paths.World); Directory.SetCurrentDirectory(paths.World);
            void Write(string path, string content)
            { var full = Path.Combine(paths.World, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, content); }
            Write("Configs/Setup.ini", "[Other]\nKeep=unchanged\n");
            Write("Envir/Drops/Legacy/Beast.txt", "#INSERT [Legacy/Common.txt]\n1/1 GROUP^\n{\n1/1 测试药品 Q COUNT=2\n}\n1/1 测试药品 6\n1/1 Missing 9\n");
            Write("Envir/Drops/Legacy/Common.txt", "1/1 测试药品 LV1\n");
            Write("Envir/Drops/Empty.txt", ";空脚本\n");
            Write("Envir/Drops/Legacy\\Beast.txt", ";Windows历史空文件\n");
            Write("Envir/Drops/Optional.txt", "1/5 测试药品\n");
            Write("Envir/Drops/公共/备用/001.txt", ";重复文件不应靠流水号保留\n1/5 测试药品\n");
            Write("Envir/Drops/公共/备用/002.txt", "1/7 测试药品\n");
            foreach (var name in new[] { "00Fishing", "01Fishing", "00Awakening", "00Strongbox", "00Blackstone" }) Write("Envir/Drops/" + name + ".txt", ";系统脚本\n");
            Write("Envir/Drops/DragonItem.txt", "1/1 测试药品 6\n1/1 测试药品 LV7\n");
            Write("Envir/NPCs/Drop.txt", "[@MAIN]\n#ACT\nDROP \"Legacy/Beast.txt\"\n");
            using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
            var world = Envir.Main;
            world.ItemInfoList = [new ItemInfo { Index = 971, Name = "测试药品" }];
            world.MonsterInfoList = [new MonsterInfo { Index = 1, Name = "测试兽", DropPath = "Legacy/Beast" }, new MonsterInfo { Index = 2, Name = "另一只兽", DropPath = "Legacy/Beast" }, new MonsterInfo { Index = 3, Name = "空掉落兽", DropPath = "Empty" }];
            world.SaveDB();
            foreach (var name in new[] { Envir.AccountPath, "Guilds/1.mgd", "companions/profiles/marker" }) storage.Write(name, [1, 2, 3, 4]);
            var original = Files(paths.World); var database = Bytes(storage, Envir.DatabasePath);
            DropScriptOrganization.Apply(paths, storage, true);
            check(Same(original, Files(paths.World)) && database.SequenceEqual(Bytes(storage, Envir.DatabasePath)), "预检产生写入");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Database, Pooling = false }.ToString()); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_drops BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirDB' BEGIN SELECT RAISE(ABORT, 'injected failure'); END"; command.ExecuteNonQuery();
            try { DropScriptOrganization.Apply(paths, storage); throw new Exception("未注入整理失败"); }
            catch (SqliteException) { check(true, "整理提交失败可见"); }
            check(Same(original, Files(paths.World)) && database.SequenceEqual(Bytes(storage, Envir.DatabasePath)), "整理失败未恢复全部文件/原路径和SQLite");
            check(world.MonsterInfoList.All(m => !m.DropPath.StartsWith("公共/")), "整理失败未恢复内存引用");
            foreach (var backup in Directory.GetDirectories(Path.Combine(paths.Runtime, "backups"))) check(Same(original, Files(Path.Combine(backup, "world"))), "备份丢失反斜杠原名");
            command.CommandText = "DROP TRIGGER fail_drops"; command.ExecuteNonQuery();
            DropScriptOrganization.Apply(paths, storage);
            var organized = Files(paths.World); var stored = Bytes(storage, Envir.DatabasePath);
            check(Directory.GetFiles(Path.Combine(paths.World, "Envir/Drops"), "*.txt").Length == 0, "仍有未整理根目录文件");
            check(!organized.Keys.Any(k => k.Contains('\\') || k.EndsWith("Empty.txt")), "未删除无用历史文件");
            check(world.MonsterInfoList[0].DropPath == world.MonsterInfoList[1].DropPath && world.MonsterInfoList.All(m => m.DropPath.StartsWith("公共/")), "共用关系丢失或误转独立规则");
            var scripts = new MonsterDropScripts(paths, new DevelopmentFiles(paths));
            check(scripts.List().All(path => !DropScriptNames.NeedsDescription(path)), "整理后仍有备用/流水号路径");
            check(scripts.List().Count(path => scripts.Read(path).Content.Split('\n').Contains("1/5 测试药品")) == 1, "同规则重复文件没有复用同一名称");
            var list = new List<DropInfo>(); DropInfo.Load(list, "测试", Path.Combine(scripts.Root, world.MonsterInfoList[0].DropPath + ".txt"), createIfNotExists: false);
            check(list.Count == 3 && list.Count(d => d.Count == 1 && d.Item != null) == 2, "旧数字或LV迁移改变实际产出");
            check(list.Single(d => d.GroupedDrop != null).GroupedDrop.Single().Count == 2 && list.Single(d => d.GroupedDrop != null).GroupedDrop.Single().QuestRequired, "分组/任务标记或有效数量丢失");
            check(!scripts.Read(world.MonsterInfoList[0].DropPath).Content.Contains("Missing"), "未移除无效物品规则");
            foreach (var monster in world.MonsterInfoList) check(scripts.Validate(monster.DropPath).Count >= 1, "整理后脚本或引用不可解析");
            check(File.ReadAllText(Path.Combine(paths.World, "Envir/NPCs/Drop.txt")).Contains(world.MonsterInfoList[0].DropPath + ".txt"), "NPC DROP引用未同步");
            var ini = new InIReader("Configs/Setup.ini");
            check(ini.ReadString("Other", "Keep", "", false) == "unchanged" && ini.ReadString("DropFiles", "Fishing", "", false) == "公共/系统/钓鱼/钓鱼00", "系统路径或未知配置丢失");
            var dragon = new DragonInfo(); var oldDragon = Settings.DragonDropFilename;
            try { Settings.DragonDropFilename = ini.ReadString("DropFiles", "Dragon", "", false); dragon.LoadDrops(); check(dragon.Drops[5].Single().Item.Index == 971, "龙兽等级误当成普通数量"); }
            finally { Settings.DragonDropFilename = oldDragon; }
            foreach (var name in new[] { Envir.AccountPath, "Guilds/1.mgd", "companions/profiles/marker" }) check(Bytes(storage, name).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "整理修改了玩家/行会/陪玩数据");
            DropScriptOrganization.Apply(paths, storage);
            check(Same(organized, Files(paths.World)) && stored.SequenceEqual(Bytes(storage, Envir.DatabasePath)), "重复整理不幂等");
            command.CommandText = "PRAGMA integrity_check"; check((string?)command.ExecuteScalar() == "ok", "整理后SQLite完整性失败");
            Write("Envir/Drops/Broken.txt", "#INSERT [../outside.txt]\n");
            var beforeBad = Files(paths.World);
            try { DropScriptOrganization.Apply(paths, storage, true); throw new Exception("未拒绝路径越界"); }
            catch (InvalidDataException) { check(Same(beforeBad, Files(paths.World)), "越界预检写入文件"); }
        }
        finally { DatabaseStorage.Current = previous; Directory.SetCurrentDirectory(cwd); if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
    private static byte[] Bytes(SqliteDatabaseStorage storage, string name)
    { using var stream = storage.Read(name); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }
    private static Dictionary<string, string> Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(root, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.GetValueOrDefault(pair.Key) == pair.Value);
}
