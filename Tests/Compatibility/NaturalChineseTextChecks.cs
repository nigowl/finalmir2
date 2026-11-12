using System.Text.RegularExpressions;
using Mir2.Hosting;
using Server.ConsoleHost;

internal static class NaturalChineseTextChecks
{
    public static void Run(ProjectPaths source, ChineseWorldText text, Action<bool, string> check)
    {
        check(ChineseText.Name("Blacksmith_Smith") == "李铁匠" && ChineseText.Name("Merchant_Whitney") == "张大婶" &&
            ChineseText.Name("Merchant_John") == "王屠户", "NPC 未按身份使用自然中文姓名");
        check(ChineseText.Alias("商人·约翰") == "王屠户" && ChineseText.Alias("ShiZombie") == "行尸" &&
            ChineseText.Alias("尸僵尸") == "行尸", "旧中文或英文名称未直接指向最终名称");
        check(ChineseText.Name("SnowYeti") == "雪原巨人" && ChineseText.Name("LightBeast") == "白虎兽" &&
            ChineseText.Name("RedFoxMan") == "赤狐妖", "怪物名称仍为机械直译");
        check(ChineseText.Name("Luke_Thompson") == "卢克·汤普森" && ChineseText.Name("John_Schwartz") == "约翰·施瓦茨",
            "纪念人物身份被改写");
        check(ChineseText.ItemAlias("ManectricClub") == "魔雷战棍" && ChineseText.ItemName("魔雷棍棒") == "魔雷战棍" &&
            ChineseText.Alias("ManectricClub") == "雷魔棍卫", "同英文名的物品与怪物被混译");
        check(text.Field("返回比奇城296:220处找杰森。") == "返回比奇城296:220处找赵镖头。" &&
            text.Field("把伐木斧交给道馆166:339处的西格蒙德。") == "把伐木斧交给道馆166:339处的司镖头。" &&
            ChineseText.Name("Help Blacksmith Jang") == "帮助张铁匠", "任务指引与 NPC 新名不一致");
        var quest = File.ReadAllText(Path.Combine(source.Import, "Crystal Database", "Envir", "Quests", "BichonProvince", "BorderVillage", "5.txt"));
        var result = text.Script(quest);
        check(result.Contains("我叫{李铁匠/LimeGreen}，想去{比奇城/Yellow}走一趟") &&
            result.Contains("Deer 10") && result.Contains("Scarecrow 10"), "完整任务对话未自然改写或击杀条件改变");
        check(text.Script(result) == result, "完整任务对话重复迁移后变化");
        const string legacy = "[@Description]\r\n年轻的旅人，我的名字是 {史密斯/LimeGreen}.\r\n村外的怪物实在太多了。\r\n我不知道该怎么才能到达 {比奇城/Yellow}\r\n你能帮助我吗？\r\n\r\n[@TaskDescription]\r\n";
        check(text.Script(legacy).Contains("我叫{李铁匠/LimeGreen}，想去{比奇城/Yellow}走一趟"), "已汉化世界未升级到完整段落译文");
        var npc = File.ReadAllText(Path.Combine(source.Import, "Crystal Database", "Envir", "NPCs", "BichonProvince", "BorderVillage", "Transport.txt"));
        var dialogue = text.Script(npc);
        check(dialogue.Contains("鄙人姓刘，走镖为生") && dialogue.Contains("比奇城/@BichonWall") &&
            dialogue.Contains("TAKEGOLD 2000"), "NPC 口吻、菜单跳转或原费用改变");
        check(text.Script(dialogue) == dialogue, "NPC 对话重复迁移后变化");
        const string marked = "这段话带有 {人名/LimeGreen}、{地点/Yellow} 和 <$USERNAME>，请<继续/@next>。";
        text.Blocks[marked] = "请<继续/@next>。";
        try { text.Script("#SAY\n" + marked + "\n#ACT\nGIVE WoodSword 1\n"); throw new Exception("完整段落丢失标记未被拒绝"); }
        catch (InvalidDataException) { check(true, "完整段落标记保护"); }
        finally { text.Blocks.Remove(marked); }
    }

    public static Dictionary<string, string> Rules(string world) => Directory.EnumerateFiles(Path.Combine(world, "Envir"), "*.txt", SearchOption.AllDirectories)
        .Where(path => new[] { "NPCs", "Quests", "SystemScripts" }.Contains(Path.GetRelativePath(Path.Combine(world, "Envir"), path).Split(Path.DirectorySeparatorChar)[0]))
        .ToDictionary(path => Path.GetRelativePath(world, path), path => RulesIn(File.ReadAllLines(path)));

    private static string RulesIn(string[] lines)
    {
        var rules = new List<string>();
        var display = false;
        foreach (var line in lines.Select(line => line.Trim()).Where(line => line.Length > 0))
        {
            if (line.StartsWith('#') || line.StartsWith('['))
            {
                display = line.ToUpperInvariant() is "#SAY" or "#ELSESAY" or "[@DESCRIPTION]" or "[@TASKDESCRIPTION]" or "[@RETURNDESCRIPTION]" or "[@COMPLETION]";
                rules.Add(line);
            }
            else if (!display)
            {
                // LOCALMESSAGE 的引号正文可润色，其命令与消息类型仍须相同。
                rules.Add(Regex.Replace(line, @"(?i)^(LOCALMESSAGE\s+)""[^""]*""(.*)$", "$1<正文>$2"));
            }
        }
        return string.Join('\n', rules);
    }
}
