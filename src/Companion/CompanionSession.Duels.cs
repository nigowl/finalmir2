using System.Text.Json;
using Crystal.Native;
using Mir2.Companions;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private string duelContext = "", duelChoice = "";
    private uint challengerId;
    private long duelDeadline, nextDuelSuggestion;
    private bool duelAnswered;
    private bool CanDuel(Actor challenger) => Profile.PkMode == "duel" && Knowledge is { AllowPk:true, InSafeZone:false, NoFight:false } &&
        state.Me is { Dead:false } && state.User is { } user && !Paused && !retiring && pendingAsset == null && !state.Trading && !state.StorageActive &&
        user.HP * 100L >= Math.Max(1, state.CalculateStats()[Stat.HP]) * 80L && challenger is { Kind:ObjectType.Player, Dead:false, Hidden:false } &&
        challenger.Id != user.ObjectID && !state.GroupMembers.Contains(challenger.Name) && Distance(user.Location, challenger.Location) <= 7;

    private async Task ConsiderDuel(Actor challenger, long now)
    {
        if (!CanDuel(challenger)) return;
        if (duelContext.Length > 0) return;
        if (Profile.LlmMode != 2 || now < nextDuelSuggestion)
        {
            if (Profile.LlmMode == 2 && challengerId == challenger.Id && duelChoice == "decline-duel") return;
            if (Profile.Traits.Aggression >= 60 || Profile.LlmMode == 2 && challengerId == challenger.Id && duelChoice == "accept-duel") await AcceptDuel(challenger, now);
            return;
        }
        challengerId = challenger.Id; duelContext = Guid.NewGuid().ToString("N"); duelDeadline = now + 6500;
        duelChoice = ""; duelAnswered = false; nextDuelSuggestion = now + 300000;
        var observation = $"附近玩家{challenger.Name}向本人明确提出三十秒切磋。本人{state.User!.Class}，等级{state.User.Level}，生命{state.User.HP}/{state.CalculateStats()[Stat.HP]}；对方显示生命{challenger.Health}%。只选择接受或拒绝本次切磋，不扩大攻击范围。";
        await report(new("llm-duel", Profile.Id, State:challenger.Name, Reason:duelContext,
            Text:JsonSerializer.Serialize(new GoalSuggestion(["accept-duel", "decline-duel"], observation),
                new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
    }
    public void DuelResult(string context, string choice)
    {
        if (context != start.Run + "/" + duelContext || Environment.TickCount64 >= duelDeadline) return;
        duelChoice = choice is "accept-duel" or "decline-duel" ? choice : ""; duelAnswered = true;
    }
    private async Task PendingDuel(long now)
    {
        if (duelContext.Length == 0 || !duelAnswered && now < duelDeadline) return;
        var choice = Profile.LlmMode == 2 ? duelChoice : ""; duelContext = "";
        var challenger = state.Actors.GetValueOrDefault(challengerId);
        if (challenger == null || !CanDuel(challenger))
        { await report(new("llm-outcome", Profile.Id, "未采用", "切磋决策", "切磋对象、生命或权限已改变。")); return; }
        if (choice == "accept-duel" || choice.Length == 0 && Profile.Traits.Aggression >= 60) await AcceptDuel(challenger, now);
        else await Say("这次先不切磋，我先准备装备和补给。");
        if (choice.Length > 0) await report(new("llm-outcome", Profile.Id, "已采用", "切磋决策", choice == "accept-duel" ? "接受本次限时切磋，战斗仍走原权限。" : "拒绝本次切磋，继续正常发展。"));
    }
    private async Task AcceptDuel(Actor challenger, long now)
    { duel = challenger.Id; duelUntil = now + 30000; await Say("接受切磋，三十秒内见招拆招，我会量力而行。"); }
}
