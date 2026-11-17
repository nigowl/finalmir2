using System.Text.RegularExpressions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class DropScriptNameChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var world = Envir.Main; var original = world.ItemInfoList;
        try
        {
            world.ItemInfoList = [
                new ItemInfo { Index = 1, Name = "青铜剑", Type = ItemType.Weapon, RequiredAmount = 10, RequiredClass = RequiredClass.Warrior, Price = 100 },
                new ItemInfo { Index = 2, Name = "玄铁剑", Type = ItemType.Weapon, RequiredAmount = 30, RequiredClass = RequiredClass.Warrior, Price = 200 },
                new ItemInfo { Index = 3, Name = "力量之刃", Type = ItemType.Weapon, RequiredType = RequiredType.MaxDC, RequiredAmount = 64 },
                new ItemInfo { Index = 4, Name = "太阳药水", Type = ItemType.Potion },
                new ItemInfo { Index = 5, Name = "鹿肉", Type = ItemType.Meat },
                new ItemInfo { Index = 6, Name = "残页", Type = ItemType.Quest },
                new ItemInfo { Index = 7, Name = "火球术", Type = ItemType.Book, RequiredClass = RequiredClass.Wizard, RequiredAmount = 7 },
                new ItemInfo { Index = 8, Name = "觉醒灵魂0", Type = ItemType.Awakening }
            ];
            var texts = new Dictionary<string, string> { ["材料.txt"] = "1/1 鹿肉" };
            DropScriptNames Names() => new(name => texts[name]);
            var names = Names();
            foreach (var path in new[] { "公共/备用/001.txt", "公共/怪物/未启用/妖兽.txt", "公共/分类-001/药品.txt", "公共/公共掉落-0123.txt" })
                check(DropScriptNames.NeedsDescription(path), "泛化命名未被识别：" + path);
            check(!DropScriptNames.NeedsDescription("公共/怪物/比奇省/尸王.txt"), "误判有真实含义的怪物路径");
            var weapons = names.Assign(null, "1/10 青铜剑\n1/100 玄铁剑");
            check(weapons.Contains("武器装备/武器/十至三十级/战士") && weapons.EndsWith("玄铁剑与青铜剑.txt"), "未按真实种类/等级/职业/物品名推断");
            var attack = Names().Assign(null, "1/1 力量之刃");
            check(!attack.Contains("六十四级"), "攻击力要求被误当成等级");
            check(Names().Assign(null, "1/1 残页 Q").StartsWith("公共/任务材料/"), "任务物品分类不符");
            check(Names().Assign(null, "1/1 火球术").Contains("职业技能/技能书/七级/法师"), "职业书籍未按真实等级和职业归类");
            check(Names().Assign(null, "1/1 觉醒灵魂0").Contains("强化制作/觉醒材料/觉醒灵魂零"), "觉醒物品仍使用泛化数字名称");
            check(Names().Assign(null, "#INSERT [材料.txt]").Contains("鹿肉"), "未根据引用内容推断名称");
            check(Names().Assign(null, "1/1 GROUP\n{\n}").Contains("空分组"), "空分组被误命名为金币来源");
            Sharing(Names, check);
            texts["循环.txt"] = "#INSERT [循环.txt]";
            try { Names().Assign(null, texts["循环.txt"]); throw new Exception("循环引用未拒绝"); }
            catch (InvalidDataException) { check(true, "名称推断拒绝循环引用"); }
        }
        finally { world.ItemInfoList = original; }
    }

    private static void Sharing(Func<DropScriptNames> create, Action<bool, string> check)
    {
        var names = create();
        var existing = names.Assign("公共/怪物/比奇省/鹿.txt", "1/1 鹿肉");
        check(names.Assign(null, ";不同注释\n1/1\t鹿肉\n") == existing, "完全相同规则未复用有意义名称");
        var paths = new[] { "1/2 太阳药水", "1/5 太阳药水", "1/5 太阳药水 2", "1/5 太阳药水 Q", "1/1 GROUP\n{\n1/5 太阳药水\n}" }.Select(content => names.Assign(null, content)).ToArray();
        check(paths.Distinct().Count() == paths.Length, "不同概率/数量/任务/分组规则被错误合并");
        check(paths.All(path => !Regex.IsMatch(path[..^4], "[A-Za-z0-9]") && !path.Contains("备用") && !path.Contains("公共掉落")), "用流水号或英文消歧");
        check(paths[2].Contains("多份规则") && paths[3].Contains("任务限定"), "数量或任务限定没有在重名文件中说明");
        check(names.Assign(null, "1/5 太阳药水") == paths[1], "重复推断名称不稳定");
        check(names.Assign(null, "1/1 Gold 2500") != names.Assign(null, "1/5 Gold 2500"), "不同金币概率被合并");
        check(create().Assign(null, "1/1 Gold 1110").Contains("一千一百一十"), "中文金额省略了百位后的十位一");
        var orderA = names.Assign(null, "1/1 鹿肉\n1/1 太阳药水");
        var orderB = names.Assign(null, "1/1 太阳药水\n1/1 鹿肉");
        check(orderA != orderB, "不同原始判定顺序被合并");
        var groups = Enumerable.Range(1, 7).Select(chance => names.Assign(null, $"1/{chance} GROUP^\n{{\n1/1 太阳药水\n}}"));
        check(groups.All(path => !path.Contains("金币基数零")), "分组被错误描述为零金币规则");
    }
}
