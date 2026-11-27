using System.Text.Encodings.Web;
using System.Text.Json;
using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private Dictionary<Guid,string> GuildPreferences() => live.Where(pair => store.Get(pair.Key).Profile.LlmMode == 2 && DateTimeOffset.UtcNow < pair.Value.GuildPreferenceUntil)
        .ToDictionary(pair => pair.Key, pair => pair.Value.GuildPreference);
    private bool DuelAllowed(CompanionRecord record, string name)
    {
        var player = Envir.Main.CharacterList.FirstOrDefault(c => c.Index == record.CharacterId)?.Player;
        var opponent = player?.CurrentMap?.Players.FirstOrDefault(p => p.Name == name);
        return store.Settings.AllowPk && record.Profile.PkMode == "duel" && record.Desired == "run" && player is { Dead:false, InSafeZone:false } &&
            !player.CurrentMap.Info.NoFight && player.HP * 100L >= player.Stats[Stat.HP] * 80L && opponent is { Dead:false, InSafeZone:false } &&
            opponent != player && player.GroupMembers?.Contains(opponent) != true && Functions.InRange(player.CurrentLocation, opponent.CurrentLocation, 7);
    }

    private void SuggestGuild(CompanionRecord record, LiveCompanion active, CompanionKnowledge knowledge, DateTimeOffset now)
    {
        var player = Envir.Main.CharacterList.FirstOrDefault(c => c.Index == record.CharacterId)?.Player;
        if (player == null) return;
        active.GuildChoices = CompanionGuildDevelopment.Choices(player, record, store.Records.Values.ToArray(), active.BlockedGoals.Keys.ToHashSet(), store.Settings.AllowPk, GuildPreferences());
        var context = AdminChanges.Version(new { record.Profile.Version, plans = active.GuildChoices.Select(p => new { p.Action, p.Name, Key = p.Goal?.Key }) });
        if (active.GuildChoiceContext != context) { active.GuildPreference = ""; active.GuildPreferenceUntil = default; }
        active.GuildChoiceContext = context;
        if (record.Profile.LlmMode != 2 || !CompanionGuildDevelopment.Active(record) || player.HP * 100L < player.Stats[Stat.HP] * 80L ||
            active.GuildChoices.Length < 2 || now < active.NextGuildRequest || context == active.LastGuildRequest && now < active.NextSameGuildRequest) return;
        if (knowledge.Goal is { } goal && (goal.Kind is "service" or "accept" or "turn-in" || goal.QuestId > 0 || goal.Key.StartsWith("fund/", StringComparison.Ordinal))) return;
        active.LastGuildRequest = context; active.NextGuildRequest = now.AddMinutes(5); active.NextSameGuildRequest = now.AddMinutes(15);
        var descriptions = active.GuildChoices.Select(plan => CompanionGuildDevelopment.ChoiceKey(plan) + "=" + (plan.Action == "join"
            ? $"申请{plan.Name}（{Envir.Main.GetGuild(plan.Name)?.Info.Membercount}人）" : plan.Action == "create"
            ? $"{plan.Reason}，费用{Server.Settings.Guild_CreationCostList.Where(c=>c.Item==null).Sum(c=>(long)c.Amount)}金币，材料已满足" : plan.Reason));
        var observation = $"等级{player.Level}，生命{player.HP}/{player.Stats[Stat.HP]}，魔法{player.MP}/{player.Stats[Stat.MP]}，金币{player.Account.Gold}，武器{player.Info.Equipment[0]?.FriendlyName??"无"}。" + string.Join("；", descriptions);
        if (observation.Length > 300) observation = observation[..300];
        var message = JsonSerializer.Serialize(new GoalSuggestion(active.GuildChoices.Select(CompanionGuildDevelopment.ChoiceKey).ToArray(), observation),
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (RequestLlm(new("llm-guild", record.Profile.Id, Reason:context, Text:message, Session:active.Session), record, active)) active.GuildPendingUntil = now.AddSeconds(6);
    }

    private void GuildSuggestion(CompanionRecord record, LiveCompanion active, string context, string choice)
    {
        var now = DateTimeOffset.UtcNow;
        active.GuildPendingUntil = default;
        var player = Envir.Main.CharacterList.FirstOrDefault(c => c.Index == record.CharacterId)?.Player;
        var current = player == null ? [] : CompanionGuildDevelopment.Choices(player, record, store.Records.Values.ToArray(), active.BlockedGoals.Keys.ToHashSet(), store.Settings.AllowPk, GuildPreferences());
        var actualContext = AdminChanges.Version(new { record.Profile.Version, plans = current.Select(p => new { p.Action, p.Name, Key = p.Goal?.Key }) });
        if (choice.Length == 0 || record.Profile.LlmMode != 2 || !CompanionGuildDevelopment.Active(record) || player == null ||
            player.HP * 100L < player.Stats[Stat.HP] * 80L || context != actualContext || !current.Any(p => CompanionGuildDevelopment.ChoiceKey(p) == choice))
        { AddEvent(active, new(now, "llm-log", "未采用", "行会与攻城决策", "建议为空或资格/场景已改变，继续规则行为。")); return; }
        active.GuildPreference = choice; active.GuildPreferenceUntil = now.AddMinutes(10); active.NextKnowledge = DateTimeOffset.MinValue;
        AddEvent(active, new(now, "llm-log", "待执行", "行会与攻城决策", "已重验合法候选：" + choice + "；实际建会、入会和战果仍以原服回执为准。"));
    }
}
