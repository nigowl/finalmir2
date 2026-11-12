using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class ChineseTranslationChecks
{
    public static void Run(ProjectPaths source)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "mir2-chinese-" + Guid.NewGuid());
        var paths = new ProjectPaths(temporary, Path.Combine(temporary, "runtime"));
        var cwd = Directory.GetCurrentDirectory(); var previous = DatabaseStorage.Current;
        var checks = 0;
        void Check(bool result, string message) { if (!result) throw new Exception(message); checks++; }
        try
        {
            foreach (var file in Directory.EnumerateFiles(source.World, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(paths.World, Path.GetRelativePath(source.World, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            Directory.SetCurrentDirectory(paths.World);
            Directory.CreateDirectory(Path.Combine(paths.World, "Envir", "NPCs"));
            File.WriteAllText(Path.Combine(paths.World, "Envir", "NPCs", "ChineseMigrationProbe.txt"), "[@MAIN]\n#SAY\n<Close/@exit>\n#ACT\nGIVE WoodSword 1\n");
            File.Delete(Path.Combine(paths.World, "Localization", "Chinese.json"));
            using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
            storage.Write(Envir.DatabasePath, File.ReadAllBytes(Path.Combine(source.Import, "Crystal Database", "Server.MirDB")));
            storage.Write(Envir.AccountPath, [1, 2, 3, 4]); storage.Write("Guilds/1.mgd", [5, 6, 7]);
            var world = Envir.Main;
            Check(world.LoadDB(), "原世界无法载入"); world.SaveDB();
            var original = Bytes(storage, Envir.DatabasePath); var files = Files(paths.World);
            var shape = Shape(world);
            var items = world.ItemInfoList.Select(i => (i.Index, i.Name)).ToArray();
            var families = world.ItemInfoList.Where(i => i.ClassBased || i.LevelBased).ToDictionary(i => i.Index,
                i => world.ItemInfoList.Where(other => other.Name.StartsWith(i.Name)).Select(other => other.Index).ToHashSet());
            var monsters = world.MonsterInfoList.Select(m => (m.Index, m.Name, m.DropPath)).ToArray();
            var npcs = world.NPCInfoList.Select(n => (n.Index, n.Name)).ToArray();
            var text = ChineseWorldText.Load();
            NaturalChineseTextChecks.Run(source, text, Check);
            var scriptRules = NaturalChineseTextChecks.Rules(paths.World);
            Check(MirClass.Warrior.ToLocalizedString() == "战士" && MirGender.Female.ToLocalizedString() == "女", "职业或性别仍为英文");
            Check((RequiredClass.Warrior | RequiredClass.Wizard).ToLocalizedString().Contains("战士"), "组合枚举未翻译");
            ChineseWorldTranslation.Apply(paths, storage, true);
            Check(Bytes(storage, Envir.DatabasePath).SequenceEqual(original) && SameFiles(files, Files(paths.World)), "预检写入了世界或文件");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Database, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_translation BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirDB' BEGIN SELECT RAISE(ABORT, 'injected translation failure'); END";
            command.ExecuteNonQuery();
            try { ChineseWorldTranslation.Apply(paths, storage); throw new Exception("迁移吞掉了存储故障"); }
            catch (SqliteException) { checks++; }
            Check(Bytes(storage, Envir.DatabasePath).SequenceEqual(original) && SameFiles(files, Files(paths.World)), "迁移失败后文件或世界未整体撤回");
            command.CommandText = "DROP TRIGGER fail_translation"; command.ExecuteNonQuery();
            var result = ChineseWorldTranslation.Apply(paths, storage);
            Check(result.Contains("完整备份") && Directory.GetFiles(Path.Combine(paths.Runtime, "backups"), "mir2.sqlite3", SearchOption.AllDirectories).Length == 2, "缺少故障和成功迁移前的完整备份");
            foreach (var backup in Directory.GetDirectories(Path.Combine(paths.Runtime, "backups")))
                Check(SameFiles(files, Files(Path.Combine(backup, "world"))), "备份丢失了文件或改写了原文件名");
            Check(world.LoadDB() && Shape(world) == shape, "汉化改变了定义编号、属性、坐标或路径");
            foreach (var family in families)
            {
                var name = world.GetItemInfo(family.Key).Name;
                Check(family.Value.SetEquals(world.ItemInfoList.Where(i => i.Name.StartsWith(name)).Select(i => i.Index)), "汉化破坏了职业/等级物品的名称族：" + name);
            }
            foreach (var (index, name) in items)
            {
                var item = world.GetItemInfo(index);
                Check(item.Name == ChineseText.ItemName(name) && world.GetItemInfo(name.Replace(" ", "").ToLowerInvariant())?.Name == item.Name, "物品原名引用失效：" + name);
            }
            foreach (var (index, name, drop) in monsters)
            {
                var monster = world.MonsterInfoList.Single(m => m.Index == index);
                Check(monster.Name == text.Field(name) && ReferenceEquals(world.GetMonsterInfo(name.ToLowerInvariant()), monster), "怪物原名引用失效：" + name);
                Check(monster.DropPath == (string.IsNullOrWhiteSpace(drop) && name != text.Field(name) ? name : drop), "掉落路径失效：" + name);
            }
            Check(npcs.All(n => world.NPCInfoList.Single(i => i.Index == n.Index).Name == text.Field(n.Name)), "NPC 名称缺译");
            var translatedRules = NaturalChineseTextChecks.Rules(paths.World);
            foreach (var rules in scriptRules)
                Check(translatedRules.GetValueOrDefault(rules.Key) == rules.Value, "润色改变了任务条件、奖励或脚本指令：" + rules.Key);
            var petCheck = new NPCSegment(new NPCPage("[@MAIN]"), [], [], [], [], []);
            petCheck.ParseCheck("CHECKPET RedViper");
            var owner = new PlayerObject { Info = new CharacterInfo { Name = "TranslationTest" } };
            owner.Pets.Add(MonsterObject.GetMonster(world.GetMonsterInfo("RedViper")));
            Check(petCheck.Check(owner), "汉化后原英文 CHECKPET 条件失效");
            owner.Pets.Clear(); Check(!petCheck.Check(owner), "缺少宠物仍通过 CHECKPET");
            Check(ChineseText.Matches("白蛇", "WhiteSerpent") && !ChineseText.Matches("白蛇", "Oma"), "怪物名称条件匹配错误");
            Check(Bytes(storage, Envir.AccountPath).SequenceEqual(new byte[] { 1, 2, 3, 4 }) && Bytes(storage, "Guilds/1.mgd").SequenceEqual(new byte[] { 5, 6, 7 }), "汉化修改了玩家或行会快照");
            var translated = Bytes(storage, Envir.DatabasePath); var translatedFiles = Files(paths.World);
            ChineseWorldTranslation.Apply(paths, storage);
            Check(Bytes(storage, Envir.DatabasePath).SequenceEqual(translated) && SameFiles(translatedFiles, Files(paths.World)), "重复汉化不是幂等操作");
            var direct = new ItemInfo { Index = int.MaxValue, Name = items[0].Name };
            world.ItemInfoList.Add(direct);
            Check(ReferenceEquals(world.GetItemInfo(direct.Name.Replace(" ", "")), direct) && world.GetItemInfo("unmapped-name") == null, "旧名兼容覆盖直接匹配或错误匹配未知名称");
            world.ItemInfoList.Remove(direct);
            var marker = text.Lines.First(p => p.Key.Contains("<$") && p.Key.Contains("/@"));
            var script = "[@MAIN]\n#SAY\n" + marker.Key + "\n#ACT\nGIVE " + items[0].Name + " 1\n";
            var output = text.Script(script);
            Check(output.Contains(marker.Value) && output.EndsWith("#ACT\nGIVE " + items[0].Name + " 1\n"), "脚本正文未翻译或动作参数被改动");
            text.Lines[marker.Key] = "错误译文";
            try { text.Script(script); throw new Exception("脚本跳转或变量丢失未被拒绝"); }
            catch (InvalidDataException) { checks++; }
            var bad = new MonsterInfo { Index = int.MaxValue, Name = "Unknown Translation Fixture" };
            world.MonsterInfoList.Add(bad); world.SaveDB();
            var beforeInvalid = Bytes(storage, Envir.DatabasePath);
            try { ChineseWorldTranslation.Apply(paths, storage); throw new Exception("缺译被静默忽略"); }
            catch (InvalidDataException) { checks++; }
            Check(Bytes(storage, Envir.DatabasePath).SequenceEqual(beforeInvalid) && SameFiles(translatedFiles, Files(paths.World)), "缺译预检失败后发生写入");
            Console.WriteLine($"Passed {checks} Chinese translation checks using disposable world data.");
        }
        finally
        {
            DatabaseStorage.Current = previous; Directory.SetCurrentDirectory(cwd);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    private static byte[] Bytes(SqliteDatabaseStorage storage, string name)
    { using var stream = storage.Read(name); using var result = new MemoryStream(); stream.CopyTo(result); return result.ToArray(); }
    private static Dictionary<string, string> Files(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    private static bool SameFiles(Dictionary<string, string> first, Dictionary<string, string> second) => first.Count == second.Count && first.All(p => second.GetValueOrDefault(p.Key) == p.Value);
    private static string Shape(Envir world) => JsonSerializer.Serialize(new
    {
        Items = world.ItemInfoList.Select(i => new { i.Index, i.Type, i.Image, i.Price, i.Weight, i.StackSize, i.Stats.Values }),
        Monsters = world.MonsterInfoList.Select(m => new { m.Index, m.Image, m.AI, m.Level, m.Experience, m.MoveSpeed, m.AttackSpeed, m.Stats.Values }),
        Maps = world.MapInfoList.Select(m => new { m.Index, m.FileName, Respawns = m.Respawns.Select(r => new { r.MonsterIndex, r.Location.X, r.Location.Y, r.Count, r.Spread, r.Delay }) }),
        Npcs = world.NPCInfoList.Select(n => new { n.Index, n.MapIndex, n.FileName, n.Image, n.Location.X, n.Location.Y }),
        Quests = world.QuestInfoList.Select(q => new { q.Index, q.FileName, q.RequiredMinLevel, q.RequiredMaxLevel })
    });
}
