using System.Text.RegularExpressions;
using C = ClientPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private async Task<bool> AppraiseEquipment()
    {
        var now=Environment.TickCount64;
        if (Knowledge?.Goal is not { AppraisalItemId: > 0 } goal || state.Trading || state.StorageActive || repairItem!=0 ||
            now-monsterAttackedAt<5000 || Crowded(now) ||
            CompanionSafety.LowHealth(state.User!.HP,state.CalculateStats()[Stat.HP],Profile.Traits.Risk)) return false;
        var user = state.User!;
        var item = user.Inventory.Concat(user.Equipment).Concat(state.Hero?.Equipment ?? [])
            .FirstOrDefault(item => item?.UniqueID == goal.AppraisalItemId);
        async Task<bool> Block(string reason)
        {
            reportedBlockedGoal=goal.Key;
            await report(new("goal-blocked",Profile.Id,Reason:reason,Text:goal.Key));return true;
        }
        if (item == null || !EquipmentAppraisal.Pending(item) || refused.Contains(item.UniqueID) || heroRefused.Contains(item.UniqueID))
            return await Block("待鉴定装备已变化，重新选择成长目标");
        if (goal.NpcId != 0 && (state.Actors.GetValueOrDefault(goal.NpcId) is not { Hidden:false } npc ||
            Distance(user.Location,npc.Location)>4)) return false;
        var scroll = user.Inventory.FirstOrDefault(scroll => scroll != null && EquipmentAppraisal.IsScroll(scroll.Info) &&
            !refused.Contains(scroll.UniqueID) && Usable(scroll.Info) && scroll.RentalInformation == null && scroll.SealedInfo == null);
        if (goal.NpcId == 0 && scroll == null) return await Block("已有鉴定卷轴不可用，重新规划鉴定服务");
        if (goal.NpcId != 0 && EquipmentAppraisal.Price(item)>user.Gold/2)
            return await Block("保留基础补给金币，暂缓鉴定");
        if (goal.NpcId != 0 && npcId != goal.NpcId)
        { await Call(goal.NpcId,"[@MAIN]");return true; }
        var links = page.SelectMany(line => Regex.Matches(line,@"<[^<>]+/@([^<>]+)>")
            .Select(match => match.Groups[1].Value.Split('/')[0])).ToArray();
        var entry = links.FirstOrDefault(link => link.Equals("APPRAISE",StringComparison.OrdinalIgnoreCase));
        var worn = user.Equipment.Any(worn => worn?.UniqueID==item.UniqueID);
        var heroWorn = state.Hero?.Equipment.Any(worn => worn?.UniqueID==item.UniqueID)==true;
        if (worn || heroWorn)
        {
            if (goal.NpcId != 0 && entry == null) return await Block("当前NPC未提供实际鉴定入口，保留已穿装备");
            var empty=Array.IndexOf(user.Inventory,null);
            if(empty<0)return await Block("没有安全卸装空格，暂缓鉴定");
            await Asset(new C.RemoveItem{Grid=heroWorn?MirGridType.HeroInventory:MirGridType.Inventory,UniqueID=item.UniqueID,To=empty},
                heroWorn?"hero-remove":"remove",item.UniqueID);return true;
        }
        await Status("鉴定装备中","鉴定可提升角色或英雄战力的装备，保留补给预算");
        if (goal.NpcId == 0)
        {
            await Asset(new C.AppraiseEquipment{ScrollID=scroll!.UniqueID,ItemID=item.UniqueID},"appraise",item.UniqueID);return true;
        }
        var confirm=links.FirstOrDefault(link=>link.Equals($"APPRAISECONFIRM_{item.UniqueID}",StringComparison.OrdinalIgnoreCase));
        if(confirm!=null)
        { await Asset(new C.CallNPC{ObjectID=npcId,Key="[@"+confirm+"]"},"appraise",item.UniqueID);return true; }
        var select=links.FirstOrDefault(link=>link.Equals($"APPRAISESELECT_{item.UniqueID}",StringComparison.OrdinalIgnoreCase));
        if(select!=null){await Call(npcId,"[@"+select+"]");return true;}
        if(entry!=null){await Call(npcId,"[@"+entry+"]");return true;}
        return await Block("当前NPC没有该装备的实际鉴定选项，未扣款");
    }
}
