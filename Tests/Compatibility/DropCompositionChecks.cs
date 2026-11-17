using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class DropCompositionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "mir2-drop-compose-" + Guid.NewGuid().ToString("N"));
        var paths = new ProjectPaths(temporary, Path.Combine(temporary, "runtime"));
        var cwd = Directory.GetCurrentDirectory(); var previous = DatabaseStorage.Current;
        try
        {
            Directory.CreateDirectory(paths.World); Directory.SetCurrentDirectory(paths.World);
            void Write(string name, string text) { var path = Path.Combine(paths.World, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
            Write("Configs/Setup.ini", "[Other]\nKeep=value\n");
            Write("Envir/Drops/A.txt", "; MIR2-DROP-RULES 1\n1/10 小红药\n1/10 小蓝药\n1/4 任务材料 2\n1/4 任务材料\n1/5 任务材料 Q\n");
            Write("Envir/Drops/B.txt", "; MIR2-DROP-RULES 1\n1/10 小红药\n1/10 小蓝药\n1/2 Gold 120\n1/30 属性需求剑\n");
            Write("Envir/Drops/C.txt", "1/90 小蓝药\n1/60 小红药\n1/3 Gold 1500\n");
            Write("Envir/Drops/D.txt", "1/60 小红药\n");
            Write("Envir/Drops/E.txt", "1/1000 小红药\n1/1000 小蓝药\n");
            Write("Envir/Drops/F.txt", "1/7 Gold 77\n1/9 小红药\n1/30 属性需求剑\n");
            Write("Envir/Drops/G.txt", "1/6 Gold 1000\n1/8 小蓝药\n");
            Write("Envir/Drops/超量未用.txt", "; MIR2-DROP-RULES 1\n1/1 小红药 100\n1/1 小红药\n");
            foreach (var name in new[] { "00Fishing", "01Fishing", "00Awakening", "00Strongbox", "00Blackstone", "DragonItem" }) Write("Envir/Drops/" + name + ".txt", ";系统脚本\n");
            Write("Envir/NPCs/Drop.txt", "[@MAIN]\n#ACT\nDROP \"A.txt\"\n");
            using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
            var world = Envir.Main;
            world.ItemInfoList = [new ItemInfo { Index = 1, Name = "小红药", Type = ItemType.Potion, Stats = new Stats { [Stat.HP] = 10 } }, new ItemInfo { Index = 2, Name = "小蓝药", Type = ItemType.Potion, Stats = new Stats { [Stat.MP] = 10 } }, new ItemInfo { Index = 3, Name = "任务材料", Type = ItemType.CraftingMaterial }, new ItemInfo { Index = 4, Name = "属性需求剑", Type = ItemType.Weapon, RequiredType = RequiredType.MaxDC, RequiredAmount = 30 }];
            world.MonsterInfoList = Enumerable.Range(1,18).Select(i => new MonsterInfo { Index = i, Name = "测试兽" + i, DropPath = i <= 15 ? ((char)('A' + (i - 1) % 5)).ToString() : i <= 17 ? "F" : "G" }).ToList();
            world.SaveDB(); storage.Write("companions/marker", [1,2,3]);
            Dictionary<string,string> Files() => Directory.GetFiles(paths.World, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(paths.World, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
            bool Same(Dictionary<string,string> a, Dictionary<string,string> b) => a.Count == b.Count && a.All(pair => b.GetValueOrDefault(pair.Key) == pair.Value);
            var before = Files(); DropScriptOrganization.Apply(paths, storage, true, true);
            check(Same(before, Files()) && world.MonsterInfoList[0].DropPath == "A", "组合预检产生写入");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Database, Pooling = false }.ToString()); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_compose BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirDB' BEGIN SELECT RAISE(ABORT, 'injected'); END"; command.ExecuteNonQuery();
            try { DropScriptOrganization.Apply(paths, storage, compose:true); throw new Exception("未注入组合迁移失败"); }
            catch (SqliteException) { check(true, "组合提交失败可见"); }
            check(Same(before, Files()) && world.MonsterInfoList[0].DropPath == "A", "组合失败未恢复文件/引用");
            command.CommandText = "DROP TRIGGER fail_compose"; command.ExecuteNonQuery();
            DropScriptOrganization.Apply(paths, storage, compose:true);
            var scripts = new MonsterDropScripts(paths, new DevelopmentFiles(paths));
            check(scripts.List().All(name => name.Split('/').Length == 3 && (name.StartsWith("公共/金币/") || name.StartsWith("公共/补给/"))), "公共库包含非基础分类或超过3级");
            check(scripts.List().All(name => !Regex.IsMatch(name, "[A-Za-z]")), "文件名包含英文");
            check(scripts.List().Contains("公共/金币/金币1500-33") && scripts.List().Contains("公共/金币/金币120-50"), "金额不是阿拉伯数字或概率未四舍五入");
            check(scripts.List().Any(name => name.EndsWith("-0") && scripts.Read(name).Content.Contains("1/1000")), "统一低概率没有保留0后缀和精确正文");
            check(scripts.List().Any(name => name.EndsWith("-2") && scripts.Read(name).Content.Contains("1/60")), "1.666%文件名未四舍五入到2");
            foreach (var monster in world.MonsterInfoList)
            {
                var content = scripts.Read(monster.DropPath).Content;
                var references = Regex.Matches(content, @"#INSERT \[(.+)\.txt\]").Select(m => m.Groups[1].Value).ToArray();
                check((monster.Index <= 15 ? references.Length is >= 1 and <= 2 : references.Length == 0) && content.Contains(DropComposition.OwnRules), "公共脚本超过2份或仅1–2怪物引用时未合回自身");
                if (monster.Index >= 16) check(content.Contains(monster.Index == 18 ? "Gold 1000" : "Gold 77"), "低引用金币规则没有完整合回怪物自身");
                if (monster.Index == 3) check(references.Where(name => name.StartsWith("公共/补给/")).All(name => !Regex.IsMatch(name, @"-\d+$")), "混合概率补给增加了数字后缀");
                var loaded = new List<DropInfo>(); DropInfo.Load(loaded, monster.Name, Path.Combine(scripts.Root, monster.DropPath + ".txt"), createIfNotExists:false);
                check(loaded.Count == (monster.Index <= 15 ? new[] { 4,4,3,1,2 }[(monster.Index-1)%5] : monster.Index == 18 ? 2 : 3), "收缩公共库增加或丢失物品");
                if (monster.Index == 1) check(loaded.Single(d => d.Item?.Index == 3 && !d.QuestRequired).Count == 3 && loaded.Single(d => d.Item?.Index == 3 && !d.QuestRequired).BaseProbability == .5m && loaded.Single(d => d.QuestRequired).Count == 1 && content.Contains("任务材料"), "数量、任务条件或自身材料规则丢失");
                if (monster.Index == 2) check(content.Contains("属性需求剑"), "装备没有放回怪物自身规则");
            }
            var usage = world.MonsterInfoList.SelectMany(monster => Regex.Matches(scripts.Read(monster.DropPath).Content, @"#INSERT \[(.+)\.txt\]")
                .Select(match => (Monster: monster.Index, Script: match.Groups[1].Value))).GroupBy(entry => entry.Script);
            check(usage.All(group => group.Select(entry => entry.Monster).Distinct().Count() >= 3), "保留了少于3个怪物使用的公共脚本");
            check(!scripts.List().Any(name => name.StartsWith("公共/金币/金币77-") || name.StartsWith("公共/金币/金币1000-")), "低引用公共文件未删除");
            check(scripts.List().All(name => !scripts.Read(name).Content.Contains("任务材料") && !scripts.Read(name).Content.Contains("属性需求剑")), "公共文件仍有材料/装备");
            check(File.ReadAllText("Envir/NPCs/Drop.txt").Contains("事件/"), "NPC原DROP入口没有迁出公共库");
            check(!File.Exists("Envir/Drops/超量未用.txt") && Directory.GetFiles(Path.Combine(paths.Runtime, "backups"), "超量未用.txt", SearchOption.AllDirectories).Length > 0, "未使用模板未清理或备份丢失");
            var saved = Files(); DropScriptOrganization.Apply(paths, storage, compose:true); check(Same(saved, Files()), "重复收缩不幂等");
            byte[] Snapshot() { using var stream = storage.Read("Server.MirDB"); using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); }
            var worldSnapshot = Snapshot();
            Write("Envir/Drops/待清理.txt", ";不再使用\n");
            command.CommandText = "CREATE TRIGGER forbid_world_write BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirDB' BEGIN SELECT RAISE(ABORT, 'unexpected world write'); END"; command.ExecuteNonQuery();
            DropScriptOrganization.Apply(paths, storage, compose:true);
            check(Same(saved, Files()) && Snapshot().SequenceEqual(worldSnapshot), "引用不变的文本清理不应重写世界快照");
            command.CommandText = "DROP TRIGGER forbid_world_write"; command.ExecuteNonQuery();
            try { DropScriptOrganization.Apply(paths, storage); throw new Exception("旧整理器可以破坏组合"); }
            catch (InvalidOperationException) { check(Same(saved, Files()), "旧整理拒绝前发生写入"); }
            using var marker = storage.Read("companions/marker"); check(marker.ReadByte() == 1, "迁移修改陪玩标记");
            command.CommandText = "PRAGMA integrity_check"; check((string?)command.ExecuteScalar() == "ok", "迁移后SQLite不完整");
        }
        finally { DatabaseStorage.Current = previous; Directory.SetCurrentDirectory(cwd); if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
}
