using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class MonsterDropChecks
{
    public static void Run(Action<bool,string> check)
    {
        var world = Envir.Main; var old = world.ItemInfoList;
        var data = Environment.GetEnvironmentVariable("MIR2_DATA_ROOT"); var runtime = Environment.GetEnvironmentVariable("MIR2_RUNTIME_ROOT");
        var directory = Directory.GetCurrentDirectory();
        var root = Path.Combine(Path.GetTempPath(), "mir2-drops-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("MIR2_DATA_ROOT", root); Environment.SetEnvironmentVariable("MIR2_RUNTIME_ROOT", null);
            var paths = new ProjectPaths();
            Directory.CreateDirectory(Path.Combine(paths.World, "Envir", "Drops")); Directory.CreateDirectory(Path.Combine(paths.World, "Configs"));
            Directory.SetCurrentDirectory(paths.World);
            var item = new ItemInfo { Index = 971, Name = "测试药品" }; world.ItemInfoList = [item];
            var scripts = new MonsterDropScripts(paths, new DevelopmentFiles(paths));
            void Reject(Action action, string message)
            { try { action(); } catch (InvalidDataException) { check(true, message); return; } throw new Exception(message); }
            void Write(string name, string value) => File.WriteAllText(ProjectPaths.SafePath(paths.World, scripts.FileName(name)), value);
            foreach (var line in new[] { "1/1 测试药品", "1/1 测试药品 Q", "1/1 测试药品 3", "1/1 测试药品 3 Q", "1/1\t测试药品\tQ\t3", "1/1 测试药品 COUNT=3" })
            {
                var drop = DropInfo.FromLine(line);
                check(drop?.Item == item && drop.AttemptDrop().Items.Count == (line.Contains('3') ? 3 : 1), "数量或原Q语义不符：" + line);
                check(drop!.QuestRequired == line.Contains('Q'), "任务掉落限制改变：" + line);
            }
            foreach (var line in new[] { "1/0 测试药品", "1/-1 测试药品", "1/1 测试药品 0", "1/1 测试药品 101", "1/1 不存在", "1 测试药品", "2/3 测试药品", "1/1 测试药品 2 3", "1/1 测试药品 2 COUNT=3", "1/1 测试药品 垃圾修饰", "1/1 GroupToken" })
                check(DropInfo.FromLine(line) == null, "接受了无效掉落：" + line);
            check(DropInfo.FromLine("1/1 测试药品 6").Count == 6 && DropInfo.FromLine("1/1 测试药品 LV1").Count == 1,
                "纯数字数量或LV边界不符");
            check(DropInfo.FromLine("1/1 测试药品 Q LV1").QuestRequired, "旧Q+LV任务规则发生变化");
            Write("Common", "1/1 测试药品 COUNT=2\n");
            Write("Shared", "#INSERT [Common.txt]\n1/2 GROUP^\n{\n1/1 测试药品 Q\n}\n");
            check(scripts.Validate("Shared").Count == 2, "未校验共用引用");
            var before = scripts.Revisions("Shared")["Common"]; Write("Common", "1/1 测试药品 COUNT=3\n");
            check(before != scripts.Revisions("Shared")["Common"], "未检测嵌套共用脚本变化");
            var drops = new List<DropInfo>(); DropInfo.Load(drops, "测试", Path.Combine(scripts.Root, "Shared.txt"), createIfNotExists: false);
            check(drops.Any(d => d.Item == item && d.Count == 3) && drops.Any(d => d.GroupedDrop?.First == true), "原加载器未保留引用、数量或分组");
            var accumulated = new List<DropInfo> { DropInfo.FromLine("1/1 Gold 100") };
            DropInfo.Load(accumulated, "累加系统", Path.Combine(scripts.Root, "Common.txt"), type: 2, createIfNotExists: false);
            check(accumulated.First().Item == item && accumulated.First().Type == 2 && accumulated.Last().Gold == 100, "多次Load没有沿用原全列表排序/类型");
            foreach (var content in new[] { "1/2 测试药品 COUNT=0", "1/2 测试药品 COUNT=101", "1/1 不存在", "1/2 GROUP\n1/1 测试药品", "}", "1/1 GROUP\n{", "#INSERT [Missing.txt]", "#INSERT [../outside.txt]", "1/1 Gold 4294967295" })
                Reject(() => scripts.Validate("Independent", content), "未拒绝无效规则：" + content);
            Write("Cycle", "#INSERT [Cycle.txt]"); Reject(() => scripts.Validate("Cycle"), "未拒绝循环引用");
            Reject(() => scripts.Normalize("../outside"), "掉落脚本路径可越界");
            var missing = scripts.Read("DoesNotExist");
            check(missing.Version == "" && !File.Exists(Path.Combine(scripts.Root, "DoesNotExist.txt")), "读取意外创建脚本");
            check(scripts.Validate(MonsterDropScripts.Own(1), ";空掉落是有效规则\n").Count == 1, "不能保存明确空掉落");
            CompositionChecks(check);
            SaveChecks(paths, check);
        }
        finally
        {
            world.ItemInfoList = old;
            Directory.SetCurrentDirectory(directory);
            Environment.SetEnvironmentVariable("MIR2_DATA_ROOT", data); Environment.SetEnvironmentVariable("MIR2_RUNTIME_ROOT", runtime);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void CompositionChecks(Action<bool,string> check)
    {
        DropInfo Rule(string line) => DropInfo.FromLine(line) ?? throw new Exception("不能解析：" + line);
        var composed = DropComposition.Merge([Rule("10% 测试药品"), Rule("20% 测试药品 2")]).Single();
        check(composed.BaseProbability == .3m && composed.Count == 3 && DropComposition.Line(composed) == "30% 测试药品 3", "概率和数量没有分别相加");
        check(composed.AdjustedProbability(2) == .6m && composed.AdjustedProbability(1, 50) == .6m && composed.AdjustedProbability(10) == 1m, "精确概率倍率或加成边界错误");
        check(DropComposition.Preview([Rule("1/15 测试药品")]) == "6.667% 测试药品\n", "最终预览没有统一百分比和3位小数");
        check(DropComposition.Preview([Rule("1.2345% 测试药品")]) == "1.235% 测试药品\n", "最终预览未按四舍五入处理边界");
        check(DropComposition.Preview([Rule("1/1000000 测试药品")]) == "<0.001% 测试药品\n", "极低正概率被误显示成0");
        var exact = Rule("1.23456789% 测试药品"); _ = DropComposition.Preview([exact]);
        check(DropComposition.Line(exact) == "1.23456789% 测试药品" && exact.BaseProbability == .0123456789m, "预览舍入修改了持久或执行精度");
        var hits = 0;
        for (var i = 0; i < 100000; i++) { var reward = composed.AttemptDrop(); if (reward != null) { if (reward.Items.Count != 3) throw new Exception("实际掉落数量不符"); hits++; } }
        check(hits is > 28500 and < 31500, "30%实际判定偏离合理区间：" + hits);
        check(DropComposition.Merge([Rule("80% 测试药品"), Rule("80% 测试药品")]).Single().BaseProbability == 1m, "概率未封顶");
        check(DropComposition.Merge([Rule("10% 测试药品 Q"), Rule("20% 测试药品")]).Count == 2, "任务与普通物品被错误合并");
        var group = Rule("1/2 GROUP*"); group.GroupedDrop.Add(Rule("1/1 测试药品 3"));
        check(DropComposition.Merge([group, Rule("1/1 测试药品")]).Count == 2 && group.AttemptDrop()?.Items.Count is null or 1, "随机分组被摊平或改变单份语义");
        var native = DropInfo.Parse("10% 测试药品\n20% 测试药品 2", "原生");
        check(native.Count == 2, "未启用组合的旧脚本被隐式合并");
        var root = DropInfo.Parse(DropComposition.Document([], "10% 测试药品\n20% 测试药品 2"), "组合");
        check(root.Single().BaseProbability == .3m && root.Single().Count == 3, "实际加载路径未合并");
        check(Rule("0.00001% 测试药品").BaseProbability == .0000001m, "低于1%的精度丢失");
        foreach (var bad in new[] { "0% 测试药品", "100.1% 测试药品", "-10% 测试药品", "NaN% 测试药品" }) check(DropInfo.FromLine(bad) == null, "接受无效百分比");
        try { DropComposition.Merge([Rule("1/1 测试药品 100"), Rule("1/1 测试药品")]); throw new Exception("未拒绝过量合并"); }
        catch (InvalidDataException) { check(true, "拒绝合并超过100份"); }
        try { DropInfo.Parse("#INSERT [Cycle.txt]", "循环"); throw new Exception("引擎未拒绝循环"); }
        catch (InvalidDataException) { check(true, "引擎循环有界"); }
        var gold = DropComposition.Merge([Rule("10% Gold 100"), Rule("20% Gold 200")]).Single();
        check(gold.Gold == 300 && gold.BaseProbability == .3m, "金币合并丢失基础额或概率");
        check(DropComposition.Format(root) == DropComposition.Format(DropInfo.Parse(DropComposition.Document([], DropComposition.Format(root)), "往返")), "精确概率文本往返不一致");
    }

    private static void SaveChecks(ProjectPaths paths, Action<bool,string> check)
    {
        var world = Envir.Main; var old = world.MonsterInfoList; var storageBefore = DatabaseStorage.Current;
        using var sqlite = new SqliteDatabaseStorage(Path.Combine(paths.Runtime, "drop-tests.sqlite3"));
        var failing = new FailingStorage(sqlite); DatabaseStorage.Current = failing;
        var monster = new MonsterInfo { Index = 937, Name = "DropTest", DropPath = "Common", Stats = new Stats { [Stat.HP] = 100 } };
        var original = DropInfo.FromLine("1/1 测试药品"); monster.Drops.Add(original);
        world.MonsterInfoList = [monster];
        try
        {
            var definitions = new WorldDefinitions(paths, null!);
            var changes = new AdminChanges(paths, sqlite);
            var drops = new MonsterDrops(paths, new DevelopmentFiles(paths), definitions, changes);
            failing.WritesBeforeFailure = int.MaxValue; world.SaveAll();
            var writes = int.MaxValue - failing.WritesBeforeFailure;
            var before = drops.Read(monster.Index); var target = Path.Combine(paths.World, "Envir", "Drops", "怪物规则", "怪物-937.txt");
            Mir2.Management.MonsterDropEdit Edit(Mir2.Management.MonsterDropDocument doc, string text, string[]? selected = null)
            {
                var edit = new Mir2.Management.MonsterDropEdit(selected ?? [], text, doc.Version);
                return edit with { PreviewVersion = drops.Preview(monster.Index, edit).Version };
            }
            void Fail(Action action)
            {
                failing.WritesBeforeFailure = writes + 1;
                try { action(); throw new Exception("未注入提交失败"); } catch (IOException) { check(true, "提交失败可见"); }
                finally { failing.WritesBeforeFailure = int.MaxValue; }
            }
            Fail(() => drops.Save(monster.Index, Edit(before, "1/1 测试药品 COUNT=2")));
            check(!File.Exists(target) && monster.DropPath == "Common" && monster.Drops.Single() == original, "失败后新脚本/引用/内存规则没有整体撤回");
            var saved = drops.Save(monster.Index, Edit(before, "1/1 测试药品 COUNT=2"));
            check(File.ReadAllText(target) == DropComposition.Document(saved.SelectedScripts, saved.Content) && monster.Drops.Single().Count == 2, "原子保存未加载数量规则");
            Fail(() => drops.Save(monster.Index, Edit(saved, "1/1 测试药品 COUNT=3")));
            check(File.ReadAllText(target) == DropComposition.Document(saved.SelectedScripts, saved.Content) && monster.Drops.Single().Count == 2, "失败后已有脚本/内存规则没有恢复");
            void Conflict(Action action, string message)
            { try { action(); throw new Exception(message); } catch (InvalidOperationException) { check(true, message); } }
            Conflict(() => drops.Save(monster.Index, Edit(before, "1/1 测试药品")), "旧怪物版本未拒绝");
            var current = drops.Read(monster.Index);
            var candidate = Edit(current, "10% 测试药品", ["Common"]);
            File.AppendAllText(Path.Combine(paths.World, scriptsPath("Common")), "\n;预览后公共文件改变\n");
            Conflict(() => drops.Save(monster.Index, candidate), "新选脚本变化后仍可保存旧预览");
            var combined = drops.Save(monster.Index, Edit(current, "10% 测试药品 2", ["Common"]));
            check(combined.SelectedScripts.SequenceEqual(new[] { "Common" }) && combined.Content == "10% 测试药品 2" &&
                monster.Drops.Single().BaseProbability == 1m && monster.Drops.Single().Count == 5 && combined.FinalContent.Contains("100.000%"), "公共与自身没有合并或预览不符");
            saved = combined;
            string scriptsPath(string name) => "Envir/Drops/" + name + ".txt";
            var service = new MonsterDefinitions(definitions, changes); var definition = service.Read(monster.Index);
            var edited = definition with { Name = "NewDropTest", Stats = new(definition.Stats), IsBoss = true }; edited.Stats[Stat.HP] = 200;
            Fail(() => service.Save(monster.Index, edited));
            check(monster.Name == "DropTest" && monster.Stats[Stat.HP] == 100 && !monster.IsBoss && monster.DropPath == saved.Script, "失败后全部属性或掉落引用没有恢复");
            service.Save(monster.Index, edited);
            check(monster.Name == "NewDropTest" && monster.Stats[Stat.HP] == 200 && monster.IsBoss && monster.DropPath == saved.Script, "完整属性保存丢失值或掉落引用");
            foreach (var invalid in new[] { edited with { Id = 1 }, edited with { AttackSpeed = 0 }, edited with { Image = 65536 }, edited with { Ai = -1 } })
            {
                try { MonsterDefinitions.Validate(monster.Index, invalid); throw new Exception("未拒绝无效怪物属性"); }
                catch (InvalidDataException) { check(true, "字段边界被服务端拒绝"); }
            }
        }
        finally { world.MonsterInfoList = old; DatabaseStorage.Current = storageBefore; }
    }
}
