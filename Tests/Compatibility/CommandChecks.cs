using Mir2.Commands;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CommandChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var command in GameCommands.All)
        {
            check(GameCommands.Resolve(command.Name) == command.Original, "Chinese command routed to a different handler: " + command.Name);
            check(GameCommands.Resolve(command.ShortName.ToUpperInvariant()) == command.Original, "Short command lost case-insensitive routing: " + command.ShortName);
            check(GameCommands.Resolve(command.Original.ToLowerInvariant()) == command.Original, "Original command stopped working: " + command.Original);
        }
        check(GameCommands.Resolve("mm") == "MAPMOVE" && GameCommands.Find("Teleport") == null && GameCommands.Find("t") == null, "MapMove shorthand or removed Teleport compatibility drifted");
        var maps = new[] { new Map(new MapInfo { Index = 801, FileName = "0", Title = "比奇省" }),
            new Map(new MapInfo { Index = 802, FileName = "1", Title = "重名地图" }), new Map(new MapInfo { Index = 803, FileName = "2", Title = "重名地图" }) };
        check(Server.Helpers.CommandInput.FindMap(maps, "比奇省", 1, out _) == maps[0] && Server.Helpers.CommandInput.FindMap(maps, "0", 1, out _) == maps[0], "传送没有同时支持地图名称和代码");
        check(Server.Helpers.CommandInput.FindMap(maps, "重名地图", 1, out var ambiguity) == null && ambiguity.Contains("1、2"), "重名地图没有明确要求代码");
        check(Server.Helpers.CommandInput.FindMap(maps, "0", 2, out _) == null && Server.Helpers.CommandInput.Split("mm \"Bichon Wall\" 100 200").SequenceEqual(new[] { "mm", "Bichon Wall", "100", "200" }), "实例越界或带空格地图名称解析错误");
        check(Server.Helpers.CommandInput.FindMap(maps, "不存在", 1, out var missing) == null && missing.Contains("地图不存在") &&
            Server.Helpers.CommandInput.FindMap(maps, "0", 2, out var closed) == null && closed.Contains("实例 2 未开启"), "地图不存在与实例未开启未区分");
        var books = new[] { new ItemInfo { Type = ItemType.Book, Shape = (short)Spell.FireBall, RequiredClass = RequiredClass.Wizard },
            new ItemInfo { Type = ItemType.Book, Shape = (short)Spell.Thrusting, RequiredClass = RequiredClass.Warrior },
            new ItemInfo { Type = ItemType.Book, Shape = (short)Spell.Thrusting, RequiredClass = RequiredClass.Warrior },
            new ItemInfo { Type = ItemType.Book, Shape = 9999, RequiredClass = RequiredClass.Warrior } };
        var magics = new[] { new MagicInfo { Spell = Spell.FireBall }, new MagicInfo { Spell = Spell.Thrusting } };
        check(PlayerObject.ClassBooks(books, magics, MirClass.Warrior).Select(book => (Spell)book.Shape).SequenceEqual(new[] { Spell.Thrusting }) &&
            PlayerObject.ClassBooks(books, magics, MirClass.Wizard).Select(book => (Spell)book.Shape).SequenceEqual(new[] { Spell.FireBall }), "全部技能书没有按当前版本/职业去重或接受了未知技能");
        var testServer = Server.Settings.TestServer;
        try
        {
            Server.Settings.TestServer = false;
            var account = new AccountInfo();
            var player = new CommandPlayer { Info = new CharacterInfo { Name = "CommandTest", AccountInfo = account }, Account = account,
                CurrentMap = new Map(new MapInfo { Index = 1, Title = "CommandTest" }) };
            void Run(string text) { player.ChatTime = 0; player.Info.ChatBanned = false; player.Messages.Clear(); player.Chat(text); }
            Run("@不存在的命令"); check(player.Messages.Any(text => text.Contains("未知命令")), "Unknown command remained silent");
            Run("@mm"); check(player.Messages.Any(text => text.Contains("参数不足")), "Missing map command arguments remained silent");
            Run("@mm 0"); check(player.Messages.Any(text => text.Contains("无权限")), "Short GM command bypassed permissions or failed silently");
            Run("@books Wizard"); check(player.Info.Magics.Count == 0 && player.Messages.Any(text => text.Contains("无权限")), "普通玩家可使用全部技能书GM命令");
            Run("@mv 1 2"); check(player.Messages.Any(text => text.Contains("无权限")), "无传送装备移动命令未说明权限");
            Run("@fd 不存在"); check(player.Messages.Any(text => text.Contains("无权限")), "无探测装备查找命令未说明权限");
            Run("@! 内容"); check(player.Messages.Any(text => text.Contains("无权限")), "原公告简写绕过权限提示");
            player.IsGM = true;
            Run("@切换标记 1"); check(player.Info.Flags[1] && player.Messages.Any(text => text.Contains("命令已处理")), "Chinese GM command failed to execute the original handler");
            Run("@SF 1"); check(!player.Info.Flags[1], "Uppercase short GM command did not reach the same handler");
            Run("@SETFLAG 1"); check(player.Info.Flags[1], "Original English GM command stopped working");
            Run("@sf -1"); check(player.Info.Flags[1] && player.Messages.Any(text => text.Contains("标记编号")), "Negative flag index was accepted or threw");
            Run("@mm 0 abc 20"); check(player.Messages.Any(text => text.Contains("传送参数错误") && text.Contains("例子：") && text.Contains("@mm 0")), "Invalid authorized teleport omitted its example");
            Run("@等级 abc"); check(player.Messages.Any(text => text.Contains("等级必须")), "Invalid level argument remained silent");
            Run("@gs FireBall abc"); check(player.Info.Magics.Count == 0 && player.Messages.Any(text => text.Contains("等级须为") && text.Contains("例子：")), "无效技能等级被静默转换为0级");
            Run("@获取职业技能书 未知职业"); check(player.Info.Magics.Count == 0 && player.Messages.Any(text => text.Contains("职业错误") && text.Contains("例子：")), "错误职业没有反馈或改变技能");
            Run("@mm 不存在的地图"); check(player.Messages.Count == 1 && player.Messages[0].Contains("地图不存在") && player.Messages[0].Contains("例子："), "地图不存在未明确反馈或重复输出了笼统结果");
            Run("@mk 不存在的物品"); check(player.Messages.Count == 1 && player.Messages[0].Contains("物品不存在") && player.Messages[0].Contains("例子："), "物品不存在未明确反馈或缺少例子");
            player.IsGM = false;
            Run("@sf 1"); check(player.Info.Flags[1] && player.Messages.Any(text => text.Contains("无权限")), "Chinese alias routing weakened original GM permissions");
        }
        finally { Server.Settings.TestServer = testServer; }
    }

    private sealed class CommandPlayer : PlayerObject
    {
        public readonly List<string> Messages = new();
        public override void Enqueue(Packet packet) { }
        public override void ReceiveChat(string text, ChatType type) { Messages.Add(text); base.ReceiveChat(text, type); }
    }
}
