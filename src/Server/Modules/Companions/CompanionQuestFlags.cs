using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionQuestFlags
{
    public static (int QuestId, int Flag)? Source(NPCObject npc)
    {
        if (!Server.MirEnvir.Envir.Main.Scripts.TryGetValue(npc.ScriptID, out var script) || script.NPCPages.Count != 1) return null;
        var page = script.NPCPages[0];
        if (!page.Key.Equals(NPCScript.MainKey, StringComparison.OrdinalIgnoreCase) || page.SegmentList.Count != 1 || page.Buttons.Count != 0) return null;
        var segment = page.SegmentList[0];
        // shortcut: 只识别主对话的单条件单标记；复杂条件、跳转或资产动作须有真实剧情回归后再支持。
        if (segment.CheckList.Count != 1 || segment.ElseActList.Count != 0 || segment.Buttons.Count != 0 || segment.ElseButtons.Count != 0 ||
            segment.GotoButtons.Count != 0 || segment.ActList.Any(action => action.Type is not (ActionType.Set or ActionType.LocalMessage or ActionType.Break))) return null;
        var condition = segment.CheckList[0];
        if (condition.Type != CheckType.CheckQuest || condition.Params.Count != 2 ||
            !int.TryParse(condition.Params[0], out var questId) || questId <= 0 || !condition.Params[1].Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)) return null;
        var sets = segment.ActList.Where(action => action.Type == ActionType.Set).ToArray();
        if (sets.Length != 1 || sets[0].Params.Count != 2 || sets[0].Params[1] != "1" ||
            !int.TryParse(sets[0].Params[0], out var flag) || flag < 0 || flag >= Globals.FlagIndexCount) return null;
        return (questId, flag);
    }
}
