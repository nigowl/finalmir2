using Mir2.Companions;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionWorldChecks
{
    public static CompanionWorldReport Read()
    {
        var world = Envir.Main;
        var spawns = world.MapList.Where(map => !map.Info.NoFight).SelectMany(map => map.Info.Respawns)
            .Where(spawn => spawn.Count > 0).Select(spawn => spawn.MonsterIndex).ToHashSet();
        var quests = world.QuestInfoList.ToDictionary(q => q.Index);
        string Npc(uint id) => world.NPCs.FirstOrDefault(npc => npc.ObjectID == id)?.Name ?? "";
        var checks = world.QuestInfoList.OrderBy(q => q.Index).Select(quest =>
        {
            var warnings = new List<string>(); var targets = new List<string>();
            var accept = Npc(quest.NpcIndex); var finish = Npc(quest.FinishNpcIndex);
            if (accept.Length == 0) warnings.Add("没有已加载的接取 NPC；需核实是否由条件脚本提供");
            if (finish.Length == 0) warnings.Add("没有已加载的交付 NPC");
            warnings.AddRange(PrerequisiteWarnings(quest, quests));
            if (quest.RequiredMinLevel > quest.RequiredMaxLevel) warnings.Add("最低等级大于最高等级");
            if (quest.FlagTasks.Count > 0) warnings.Add("包含脚本标记目标，陪玩尚不能自动完成");
            foreach (var task in quest.KillTasks)
            {
                var sources = CompanionPlanning.KillSources(task.Monster).Where(spawns.Contains).ToArray();
                targets.Add($"击杀 {task.Monster.GameName} ×{task.Count}；已布怪来源 {string.Join(',', sources)}");
                if (sources.Length == 0) warnings.Add($"击杀目标“{task.Monster.GameName}”没有正常布怪来源，需核实脚本召唤");
            }
            foreach (var task in quest.ItemTasks)
            {
                var items = new HashSet<int> { task.Item.Index };
                var sources = world.MonsterInfoList.Where(m => spawns.Contains(m.Index) && CompanionPlanning.CanDrop(m.Drops, items))
                    .Select(m => $"{m.GameName}#{m.Index}").ToArray();
                targets.Add($"收集 {task.Item.FriendlyName}#{task.Item.Index} ×{task.Count}；掉落 {string.Join(',', sources)}");
                if (sources.Length == 0) warnings.Add($"物品“{task.Item.FriendlyName}”无正概率且已布怪的掉落来源，需核实商店或脚本");
            }
            foreach (var task in quest.CarryItems) targets.Add($"接取携带 {task.Item.FriendlyName}#{task.Item.Index} ×{task.Count}");
            return new CompanionQuestCheck(quest.Index, quest.Name, quest.RequiredMinLevel, quest.RequiredMaxLevel, quest.RequiredQuest,
                quest.RequiredClass.ToString(), accept, finish, targets.ToArray(), warnings.ToArray());
        }).ToArray();
        return new(world.MapInfoList.Count, world.MonsterInfoList.Count, checks.Length, checks);
    }

    public static string[] PrerequisiteWarnings(QuestInfo quest, IReadOnlyDictionary<int, QuestInfo> quests)
    {
        var seen = new HashSet<int> { quest.Index }; var current = quest;
        while (current.RequiredQuest > 0)
        {
            if (!seen.Add(current.RequiredQuest)) return ["前置任务形成循环"];
            if (!quests.TryGetValue(current.RequiredQuest, out var previous)) return [$"前置任务 #{current.RequiredQuest} 不存在"];
            if (previous.RequiredMinLevel > current.RequiredMaxLevel) return ["前置最低等级超过后续接取上限"];
            if ((previous.RequiredClass & current.RequiredClass) == 0) return ["前置与后续职业条件没有交集"];
            current = previous;
        }
        return [];
    }
}
