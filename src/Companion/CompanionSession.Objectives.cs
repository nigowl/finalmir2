using System.Drawing;
using Mir2.Companions;
using C = ClientPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private string progressGoal = "";
    private int bestGoalDistance = int.MaxValue;
    private string reportedBlockedGoal="";
    private long arrivedAt;

    private int MonsterId(Crystal.Native.Actor actor) => Knowledge?.Enemies?.FirstOrDefault(enemy => enemy.ObjectId == actor.Id)?.MonsterId ??
        Knowledge?.Spawns.FirstOrDefault(spawn => spawn.Name == actor.Name)?.MonsterId ?? 0;

    private async Task<bool> FollowObjective(long now, bool travelOnly = false)
    {
        if (Knowledge?.Goal is not { } goal) { reportedBlockedGoal=""; return false; }
        var remote = goal.MapId != Knowledge.MapId;
        if (remote && goal.NextExit == null)
        { if(travelOnly)return false; await Status("待目标", "目标地图没有可用的公开步行入口"); nextAction = now + 3000; return true; }
        var point = remote ? new Point(goal.NextExit!.X, goal.NextExit.Y) : new Point(goal.X, goal.Y);
        var near = remote ? 0 : goal.Kind is "accept" or "turn-in" or "service" ? 3 : 1;
        var key = $"{Knowledge.MapId}/{goal.Kind}/{goal.MapId}/{goal.QuestId}/{goal.NpcId}/{string.Join(',', goal.MonsterIds ?? [])}";
        var distance = Distance(state.User!.Location, point);
        if (key != progressGoal) { progressGoal = key; bestGoalDistance = distance; arrivedAt=0; }
        else if (distance < bestGoalDistance) { bestGoalDistance = distance; lastProgress = now; }
        await Status(travelOnly ? "撤离中" : remote ? "跨图中" : goal.Kind == "hunt" ? "寻怪中" : "任务中",
            travelOnly ? "补给途中遭受攻击，优先沿可通行路线撤离" : goal.Reason);
        var atExit=remote && distance==0;
        if (distance > near && await MoveTo(point, near)) {reportedBlockedGoal="";arrivedAt=0;return true;}
        if (travelOnly && !atExit) return false;
        if(distance<=near&&arrivedAt==0)arrivedAt=now;
        if((distance>near||arrivedAt>0&&now-arrivedAt>15000)&&goal.Key.Length>0&&reportedBlockedGoal!=goal.Key)
        {
            reportedBlockedGoal=goal.Key;
            await report(new("goal-blocked",Profile.Id,Reason:"当前目标暂不可达或到达后没有进度，改选其他目标",Text:goal.Key));
        }
        // 原Turn也会执行CheckMovement，出口周围被占用时不必先挤开再踩入。
        if(atExit)
        {
            await connection.Send(new C.Turn{Direction=state.User.Direction});
            nextAction=now+650;return true;
        }
        await Status("等待目标", distance <= near ? goal.Kind == "hunt" ? "已到目标区域，等待怪物刷新或任务掉落" : "已到任务 NPC 附近，等待接交回执" : "目标暂不可达，等待重新规划");
        nextAction = now + 1000;
        return true;
    }
}
