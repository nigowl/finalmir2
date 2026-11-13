using System.Text.Json;
using Mir2.Companions;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private string goalContext="",goalHint="";
    private string[] goalCandidates=[];
    private long nextGoalRequest,goalDeadline;
    public void GoalResult(string context,string text)
    {
        if(context!=start.Run+"/"+goalContext||Environment.TickCount64>=goalDeadline||!goalCandidates.Contains(text))return;
        goalHint=text;goalContext="";
    }
    private async Task SuggestGoal(long now)
    {
        if(Profile.LlmMode!=2||now<nextGoalRequest)return;
        nextGoalRequest=now+60000;goalContext=Guid.NewGuid().ToString("N");goalDeadline=now+7000;
        var candidates=new List<string>{"explore"};
        if(state.Actors.Values.Any(a=>a.Kind==ObjectType.Monster&&!a.Hidden&&!a.Dead)&&Knowledge?.NoFight!=true)candidates.Add("hunt");
        if(state.Quests.Count>0)candidates.Add("quest");
        if(Profile.AcceptGroup&&state.GroupMembers.Count>1)candidates.Add("help");
        if(!state.Actors.Values.Any(a=>a.Kind==ObjectType.Monster&&!a.Dead&&Distance(a.Location,state.User!.Location)<=6))candidates.Add("rest");
        goalCandidates=candidates.ToArray();
        await report(new("llm-goal",Profile.Id,Reason:goalContext,Text:JsonSerializer.Serialize(new GoalSuggestion(goalCandidates,$"等级{state.User!.Level}，生命{state.User.HP}，当前任务{state.Quests.Count}"))));
    }
    private void InvalidateSuggestions()
    {goalContext="";goalHint="";llmContext="";llmReply="";}
}
