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
            Run("@mm 0"); check(player.Messages.Any(text => text.Contains("命令未完成")), "Short GM command bypassed permissions or failed silently");
            player.IsGM = true;
            Run("@切换标记 1"); check(player.Info.Flags[1] && player.Messages.Any(text => text.Contains("命令已处理")), "Chinese GM command failed to execute the original handler");
            Run("@SF 1"); check(!player.Info.Flags[1], "Uppercase short GM command did not reach the same handler");
            Run("@SETFLAG 1"); check(player.Info.Flags[1], "Original English GM command stopped working");
            Run("@sf -1"); check(player.Info.Flags[1] && player.Messages.Any(text => text.Contains("标记编号")), "Negative flag index was accepted or threw");
            Run("@mm 0 abc 20"); check(player.Messages.Any(text => text.Contains("传送参数错误") && text.Contains("例子：") && text.Contains("@mm 0")), "Invalid authorized teleport omitted its example");
            Run("@等级 abc"); check(player.Messages.Any(text => text.Contains("等级必须")), "Invalid level argument remained silent");
            player.IsGM = false;
            Run("@sf 1"); check(player.Info.Flags[1] && player.Messages.Any(text => text.Contains("命令未完成")), "Chinese alias routing weakened original GM permissions");
        }
        finally { Server.Settings.TestServer = testServer; }
    }

    private sealed class CommandPlayer : PlayerObject
    {
        public readonly List<string> Messages = new();
        public override void Enqueue(Packet packet) { }
        public override void ReceiveChat(string text, ChatType type) => Messages.Add(text);
    }
}
