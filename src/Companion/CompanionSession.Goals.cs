using System.Text.Json;
using Mir2.Companions;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private string goalContext="",goalHint="",goalDecision="",lastGoalDecision="";
    private long nextGoalRequest,nextSameGoalRequest,goalDeadline,goalHintUntil;
    private string GoalDecision(long now)
    {
        if(Knowledge==null||state.User==null||Paused||retiring||Funding||pendingAsset!=null||state.Trading||state.StorageActive||
            state.Me?.Dead!=false||state.Quests.Count>0||preparedSkill!=null||pendingSkill!=Spell.None||
            state.User.HP*100L<Math.Max(1,state.CalculateStats()[Stat.HP])*80L||now-monsterAttackedAt<5000||now<aggressorUntil||
            Profile.Goal is "探索优先" or "协助队友")return "";
        if(now-lastProgress<20000||combatTarget!=0&&state.Actors.GetValueOrDefault(combatTarget) is {Dead:false})return "";
        if(Knowledge.Goal is { } goal && (goal.Kind!="hunt"||goal.QuestId>0||goal.MapId!=Knowledge.MapId))return "";
        var exit=ExploreDestination(now);var targets=CombatCandidates().Select(MonsterId).Distinct().Order().ToArray();
        if(exit==null||targets.Length==0||Knowledge.NoFight)return "";
        return $"{Knowledge.MapId}/{Profile.Goal}/{string.Join(',',targets)}/{exit.DestinationMapId}";
    }
    public async Task GoalResult(string context,string text)
    {
        var now=Environment.TickCount64;
        if(context!=start.Run+"/"+goalContext||now>=goalDeadline||text is not ("hunt" or "explore")||goalDecision!=GoalDecision(now))
        {
            if(context==start.Run+"/"+goalContext)goalContext="";
            await report(new("llm-outcome",Profile.Id,"未采用","目标建议","建议无效、已过期或当前可执行选项已改变。"));return;
        }
        goalHint=text;goalContext="";goalHintUntil=now+120000;
        await report(new("llm-outcome",Profile.Id,"待执行","目标建议","已通过当前候选校验："+text));
    }
    private async Task SuggestGoal(long now)
    {
        if(Profile.LlmMode!=2)return;
        var decision=GoalDecision(now);
        if(goalHint.Length>0&&(now>=goalHintUntil||decision!=goalDecision))
        {goalHint="";await report(new("llm-outcome",Profile.Id,"未采用","目标建议","场景改变或建议到期，继续规则行为。"));}
        if(decision.Length==0)return;
        if(now<nextGoalRequest)return;
        if(decision==lastGoalDecision&&now<nextSameGoalRequest)return;
        nextGoalRequest=now+300000;nextSameGoalRequest=now+900000;lastGoalDecision=goalDecision=decision;
        goalContext=Guid.NewGuid().ToString("N");goalDeadline=now+7000;
        var observation=$"当前无个人任务或必要服务，至少20秒没有发展进度且未在战斗；等级{state.User!.Level}，生命{state.User.HP}；hunt=继续接近当前适合的可见怪物，explore=沿公开出口探索。仅选择闲时偏好，不改变保命与任务规则。";
        await report(new("llm-goal",Profile.Id,Reason:goalContext,Text:JsonSerializer.Serialize(new GoalSuggestion(["hunt","explore"],observation),
            new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)})));
    }
    private async Task UsedGoal(string hint)
    {
        if(goalHint!=hint)return;
        goalHint="";
        await report(new("llm-outcome",Profile.Id,"已采用","目标建议",hint=="explore"?"实际发出沿公开出口探索的移动请求。":"实际发出当前合法目标的战斗/接近请求。"));
    }
    private void InvalidateSuggestions()
    {goalContext="";goalHint="";llmContext="";llmReply="";duelContext="";}
}
