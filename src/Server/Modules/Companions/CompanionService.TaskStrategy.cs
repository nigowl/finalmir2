using System.Text.Encodings.Web;
using System.Text.Json;
using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private void SuggestTask(CompanionRecord record, LiveCompanion active, CompanionKnowledge knowledge, List<KnownGoal> tasks, DateTimeOffset now)
    {
        // 候选复用本次正常任务规划结果，不再为模型额外扫描全世界或放宽战力/路线条件。
        active.TaskChoices = tasks.DistinctBy(goal => goal.QuestId).Take(3).ToArray();
        var context = AdminChanges.Version(active.TaskChoices.Select(goal => new { goal.QuestId, goal.Key }));
        if (active.TaskChoiceContext != context) { active.PreferredQuest = 0; active.TaskPreferenceUntil = default; }
        active.TaskChoiceContext = context;
        if (record.Profile.LlmMode != 2 || record.Desired != "run" || active.TaskChoices.Length < 2 ||
            knowledge.Goal is not { Kind:"hunt", QuestId:>0 } || now < active.NextTaskRequest ||
            context == active.LastTaskRequest && now < active.NextSameTaskRequest) return;
        var player = Envir.Main.CharacterList.FirstOrDefault(c => c.Index == record.CharacterId)?.Player;
        if (player == null || player.Dead || player.HP * 100L < player.Stats[Stat.HP] * 80L) return;
        active.LastTaskRequest = context; active.NextTaskRequest = now.AddMinutes(5); active.NextSameTaskRequest = now.AddMinutes(15);
        var descriptions = active.TaskChoices.Select(goal => $"task:{goal.QuestId}={Envir.Main.GetQuestInfo(goal.QuestId)?.Name}，{Envir.Main.GetMapInfo(goal.MapId)?.Title}{(goal.MapId==player.CurrentMap.Info.Index?"（同图）":"（跨图）")}" +
            $"，剩余{player.CurrentQuests.First(q => q.Index == goal.QuestId).KillTaskCount.Sum(t => Math.Max(0L, (long)t.Info.Count-t.Count)) + player.CurrentQuests.First(q => q.Index == goal.QuestId).ItemTaskCount.Sum(t => Math.Max(0L, (long)t.Info.Count-t.Count))}项");
        var observation = $"等级{player.Level}，生命{player.HP}/{player.Stats[Stat.HP]}。选择先推进的已接任务；不覆盖保命、补给、交付与日常优先规则。" + string.Join("；", descriptions);
        if (observation.Length > 300) observation = observation[..300];
        RequestLlm(new("llm-task", record.Profile.Id, Reason:context, Text:JsonSerializer.Serialize(new GoalSuggestion(active.TaskChoices.Select(goal => "task:"+goal.QuestId).ToArray(), observation),
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Session:active.Session), record, active);
    }
    private void TaskSuggestion(CompanionRecord record, LiveCompanion active, string context, string choice)
    {
        var player = Envir.Main.CharacterList.FirstOrDefault(c => c.Index == record.CharacterId)?.Player;
        var selected = active.TaskChoices.FirstOrDefault(goal => "task:"+goal.QuestId == choice);
        if (record.Profile.LlmMode != 2 || record.Desired != "run" || player == null || player.Dead || player.HP * 100L < player.Stats[Stat.HP] * 80L || context != active.TaskChoiceContext ||
            selected == null || !player.CurrentQuests.Any(q => q.Index == selected.QuestId && !q.Completed))
        { AddEvent(active, new(DateTimeOffset.UtcNow, "llm-log", "未采用", "任务先后决策", "候选任务已完成或场景改变，继续规则行为。")); return; }
        active.PreferredQuest = selected.QuestId; active.TaskPreferenceUntil = DateTimeOffset.UtcNow.AddMinutes(10); active.NextKnowledge = DateTimeOffset.MinValue;
        AddEvent(active, new(DateTimeOffset.UtcNow, "llm-log", "待执行", "任务先后决策", "已选择任务：" + Envir.Main.GetQuestInfo(selected.QuestId)?.Name + "；下次规划重新校验战力、路线及优先服务。"));
    }
}
