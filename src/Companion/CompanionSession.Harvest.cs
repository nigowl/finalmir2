using C = ClientPackets;
using Mir2.Companions;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private readonly Dictionary<uint,int> harvestAttempts=[];

    private async Task<bool> CollectHarvest(long now)
    {
        var user=state.User;var knowledge=Knowledge;if(user==null||knowledge==null)return false;
        var empty=user.Inventory.Count(item=>item==null);if(empty==0)return false;
        var limit=state.CalculateStats()[Stat.BagWeight];
        var corpse=state.Actors.Values.Where(actor=>actor.Kind==ObjectType.Monster&&actor.Dead&&!actor.Harvested&&!actor.Hidden&&
            harvestAttempts.GetValueOrDefault(actor.Id)<8&&knowledge.Enemies?.Any(enemy=>enemy.ObjectId==actor.Id&&enemy.Harvestable)==true&&
            CompanionInventoryRules.CanHarvest(state.InventoryWeight,limit,empty,knowledge.Quests.Any(quest=>quest.ItemIds.Length>0&&
                quest.MonsterIds.Contains(MonsterId(actor))&&state.Quests.TryGetValue(quest.Id,out var progress)&&!progress.Completed)))
            .OrderBy(actor=>Distance(user.Location,actor.Location)).FirstOrDefault();
        if(corpse==null)return false;
        if(Distance(user.Location,corpse.Location)>1)return await MoveTo(corpse.Location,1);
        await Status("采集中","按原规则割取尸体掉落："+corpse.Name);
        harvestAttempts[corpse.Id]=harvestAttempts.GetValueOrDefault(corpse.Id)+1;
        foreach(var id in harvestAttempts.Keys.Where(id=>!state.Actors.ContainsKey(id)).ToArray())harvestAttempts.Remove(id);
        await connection.Send(new C.Harvest{Direction=Direction(user.Location,corpse.Location)});
        nextAction=now+800;return true;
    }
}
