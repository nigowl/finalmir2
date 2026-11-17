using Microsoft.Data.Sqlite;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class BasicDropBalanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "mir2-basic-supplies-" + Guid.NewGuid().ToString("N"));
        var paths = new ProjectPaths(temporary, Path.Combine(temporary, "runtime"));
        var cwd = Directory.GetCurrentDirectory(); var previous = DatabaseStorage.Current;
        try
        {
            Directory.CreateDirectory(paths.World); Directory.SetCurrentDirectory(paths.World);
            void Write(string name, string text) { var path = Path.Combine(paths.World, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
            Write("Configs/Setup.ini", "[Other]\nKeep=value\n");
            foreach (var name in new[] { "00Fishing", "00Awakening", "00Strongbox", "00Blackstone", "DragonItem" }) Write("Envir/Drops/" + name + ".txt", ";系统规则保持\n");
            var world = Envir.Main;
            world.ItemInfoList = Enumerable.Range(658, 14).Select(id => new ItemInfo { Index = id, Name = "测试药" + id, Type = ItemType.Potion,
                Shape = (short)(id >= 666 ? 1 : 0), StackSize = 20, Stats = new Stats { [Stat.HP] = 100 } }).ToList();
            world.ItemInfoList.Add(new ItemInfo { Index = 1, Name = "通用护甲", Type = ItemType.Armour, RequiredClass = (RequiredClass)7 });
            world.ItemInfoList.Add(new ItemInfo { Index = 2, Name = "任务材料", Type = ItemType.Quest }); world.ItemIndex = 671;
            world.MonsterInfoList = Enumerable.Range(0, 21).Select(i => new MonsterInfo { Index = i + 1, Name = "测试怪" + i, Level = (ushort)(i / 3 * 11),
                Stats = new Stats { [Stat.HP] = 1 }, DropPath = "怪物规则/测试怪" + i }).ToList();
            foreach (var monster in world.MonsterInfoList) Write("Envir/Drops/" + monster.DropPath + ".txt", DropComposition.Document([], "1/10 Gold 10\n1/9 测试药658\n1/7 任务材料 Q\n1/100 通用护甲\n"));
            using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage; world.SaveDB(); storage.Write("companions/marker", [1, 2, 3]);
            Dictionary<string, string> Files() => Directory.GetFiles(paths.World, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(paths.World, file), File.ReadAllText);
            bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(pair => b.GetValueOrDefault(pair.Key) == pair.Value);
            var before = Files(); DropScriptOrganization.Apply(paths, storage, checkOnly: true, compose: true, supplies: true);
            check(Same(before, Files()) && world.ItemInfoList.Count == 16 && (int)world.GetItemInfo(1).RequiredClass == 7, "补给预检写入或未撤回临时物品");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.Database, Pooling = false }.ToString()); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER fail_supplies BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirDB' BEGIN SELECT RAISE(ABORT,'injected'); END"; command.ExecuteNonQuery();
            try { DropScriptOrganization.Apply(paths, storage, compose: true, supplies: true); throw new Exception("没有注入保存失败"); }
            catch (SqliteException) { check(Same(before, Files()) && (int)world.GetItemInfo(1).RequiredClass == 7 && world.GetItemInfo("疗伤药") == null, "失败未恢复掉落文件或物品属性"); }
            command.CommandText = "DROP TRIGGER fail_supplies"; command.ExecuteNonQuery();
            DropScriptOrganization.Apply(paths, storage, compose: true, supplies: true);
            check((int)world.GetItemInfo(1).RequiredClass == 31 && world.GetItemInfo("万年雪霜").Stats[Stat.MP] == 500, "通用装备或高阶恢复药没有保存");
            foreach (var monster in world.MonsterInfoList)
            {
                var drops = new List<DropInfo>(); DropInfo.Load(drops, monster.Name, Path.Combine(Settings.DropPath, monster.DropPath + ".txt"), createIfNotExists: false);
                check(drops.Count(drop => drop.Gold > 0) == 1 && drops.Single(drop => drop.Gold > 0).BaseProbability == .5m, "基础金币叠加了旧规则或不是50%");
                check(drops.Where(drop => drop.Item?.Type == ItemType.Potion).All(drop => drop.BaseProbability == .5m && drop.Count == 1), "基础药品叠加旧概率/数量");
                check(drops.Single(drop => drop.QuestRequired).Chance == 7 && drops.Single(drop => drop.Item?.Index == 1).Chance == 100, "任务或装备掉落被改写");
            }
            var after = Files(); DropScriptOrganization.Apply(paths, storage, compose: true, supplies: true);
            check(Same(after, Files()), "再次整理重复加药、加金币或改变文件名");
            using var marker = storage.Read("companions/marker"); check(marker.ReadByte() == 1 && marker.Length == 3, "整理修改了陪玩存档");
        }
        finally { DatabaseStorage.Current = previous; Directory.SetCurrentDirectory(cwd); Directory.Delete(temporary, true); }
    }
}
