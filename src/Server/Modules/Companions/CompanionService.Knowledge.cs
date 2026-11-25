using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private WorkerCommand? PlanKnowledge(DateTimeOffset now)
    {
        // 每100ms只规划最久未刷新的一个角色，不能把数十次全图搜索集中塞进同一游戏帧。
        var next = live.Where(pair => pair.Value.StoppingAt == null && pair.Value.Entered && pair.Value.NextKnowledge <= now && Eligibility(store.Get(pair.Key), now).Allowed)
            .OrderBy(pair => pair.Value.NextKnowledge).FirstOrDefault();
        if (next.Value == null || runtime.Stopping) return null;
        var (id, active) = next;
        active.NextKnowledge = now.AddSeconds(3);
        foreach (var key in active.BlockedGoals.Keys.Where(key => active.BlockedGoals[key] <= now).ToArray()) active.BlockedGoals.Remove(key);
        var knowledge = CompanionKnowledgeReader.Read(store.Get(id), active.BlockedGoals.Keys.ToHashSet(), store.Records.Values.ToArray(), store.Settings.AllowPk, active.Goal);
        active.Goal = knowledge?.Goal; active.Guild = knowledge?.Guild;
        if (knowledge == null) return null;
        knowledge = knowledge with { AllowPk = store.Settings.AllowPk, Revision = knowledge.Revision + "/" + store.Settings.AllowPk + "/" + knowledge.InSafeZone };
        if (knowledge.Revision == active.Knowledge) return null;
        active.Knowledge = knowledge.Revision;
        return new("knowledge", Id: id, Knowledge: knowledge);
    }
}
