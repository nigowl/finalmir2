using System.Drawing;
using System.Text.RegularExpressions;
using Mir2.Companions;
using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private uint npcId,pendingNpc;
    private List<string> page=[];
    private S.NPCGoods? goods;
    private bool repairPage,sellPage,unloadOnly;
    private float repairRate=1;
    private ulong repairItem;
    private readonly Dictionary<uint,long> visited=[];
    private readonly HashSet<int> acceptedQuests=[],finishedQuests=[];
    private long npcSent;
    private async Task Call(uint id,string key)
    {
        if(key.Equals("[@MAIN]",StringComparison.OrdinalIgnoreCase))unloadOnly=Knowledge?.Goal?.SellItems is {Length:>0};
        npcId=pendingNpc=id; npcSent=Environment.TickCount64; page=[]; goods=null; repairPage=sellPage=false;
        await connection.Send(new C.CallNPC{ObjectID=id,Key=key}); nextAction=npcSent+500;
    }
    private int PotionCount(bool hp)=>CompanionSupplyRules.Count(state.User!.Inventory,hp?Stat.HP:Stat.MP);
    private int PotionTarget(bool hp)=>CompanionSupplyRules.Target(hp?Stat.HP:Stat.MP,state.User!.Class,Profile.Traits.Frugality);
    private int TownScrollCount()=>CompanionSupplyRules.TownCount(state.User!.Inventory,state.User.Class,state.User.Gender,state.User.Level);
    private bool NeedsRepair(UserItem? item)=>item?.Info!=null && item.MaxDura>0 && item.CurrentDura<item.MaxDura/2 &&
        !item.Info.Bind.HasFlag(BindMode.DontRepair) && !refused.Contains(item.UniqueID) && item.RepairPrice()<state.User!.Gold/2;
    private bool Trades(ItemInfo item) =>
        Knowledge?.Npcs.FirstOrDefault(n=>n.ObjectId==npcId)?.TradeTypes is { } types &&
        (types.Length==0||types.Contains((int)item.Type));
    private bool RepairHere(UserItem? item) => NeedsRepair(item) && Trades(item!.Info) && item.RepairPrice()*repairRate<=state.User!.Gold;
    private async Task<bool> BuySupplies()
    {
        var shop=goods!;var user=state.User!;
        foreach(var item in shop.List.Where(i=>i.Info!=null && Usable(i.Info)).OrderBy(i=>i.Info.Type==ItemType.Potion?0:CompanionSupplyRules.IsTownScroll(i)?1:2).ThenBy(i=>i.Price()))
        {
            var missing=1;
            if(item.Info.Type==ItemType.Potion)
            {
                var hp=item.Info.Stats[Stat.HP]>0;
                if(!CompanionSupplyRules.IsPotion(item,hp?Stat.HP:Stat.MP))continue;
                missing=PotionTarget(hp)-PotionCount(hp);
            }
            else if(CompanionSupplyRules.IsTownScroll(item))
            {
                if(Knowledge?.NoTownTeleport!=false)continue;
                missing=1-TownScrollCount();
            }
            else
            {
                var slot=Slot(item.Info.Type);
                var equipment=slot>=0&&slot<user.Equipment.Length&&(user.Equipment[slot]==null||EquipmentValue(item)>EquipmentValue(user.Equipment[slot]));
                var book=item.Info.Type==ItemType.Book&&!user.Magics.Any(m=>(int)m.Spell==item.Info.Shape);
                if((!equipment&&!book)||user.Inventory.Any(i=>i?.ItemIndex==item.ItemIndex))continue;
            }
            var unit=item.Clone();unit.Count=1;
            var price=unit.Price()*shop.Rate;
            if(missing<=0||price<=0||price>user.Gold/CompanionSupplyRules.BudgetDivisor(Profile.Traits.Frugality)||!user.Inventory.Any(x=>x==null))continue;
            var count=(ushort)Math.Min(Math.Min(missing,item.Info.StackSize),Math.Min(item.Count,Math.Floor(user.Gold/(price*2))));
            if(item.Info.Weight>0)count=(ushort)Math.Min(count,Math.Max(0,state.CalculateStats()[Stat.BagWeight]-state.InventoryWeight)/item.Info.Weight);
            if(count==0)continue;
            boughtItem=item.ItemIndex;
            await Asset(new C.BuyItem{ItemIndex=item.UniqueID,Count=count,Type=shop.Type},"buy",item.UniqueID);return true;
        }
        goods=null;return false;
    }
    private async Task<bool> TasksAndSupplies(long now)
    {
        var user=state.User!;
        if(pendingNpc!=0)
        { if(now-npcSent<3000)return true; visited[pendingNpc]=now+60000; pendingNpc=0; }
        if(sellPage && await SellSurplus())return true;
        if(goods!=null)
        {
            // 减负会话不顺便购装，避免刚腾出空间又被买回的装备占满。
            if(!unloadOnly && await BuySupplies())return true;
            goods=null;
        }
        if(repairPage)
        {
            var worn=user.Inventory.FirstOrDefault(i=>RepairHere(i) && (repairItem==0||i.UniqueID==repairItem));
            if(worn!=null){repairItem=worn.UniqueID;await Asset(new C.RepairItem{UniqueID=worn.UniqueID},"repair",worn.UniqueID);return true;}
            var equipped=user.Equipment.FirstOrDefault(RepairHere);
            var empty=Array.IndexOf(user.Inventory,null);
            if(equipped!=null && empty>=0)
            {repairItem=equipped.UniqueID;await Asset(new C.RemoveItem{Grid=MirGridType.Inventory,UniqueID=equipped.UniqueID,To=empty},"remove",equipped.UniqueID);return true;}
            repairPage=false;repairItem=0;
            if(npcId!=0){await Call(npcId,"[@MAIN]");return true;}
        }
        if(Knowledge?.Goal is {Kind:"service"} serviceGoal && visited.GetValueOrDefault(serviceGoal.NpcId)<now &&
            state.Actors.GetValueOrDefault(serviceGoal.NpcId) is {Hidden:false} serviceNpc && Distance(user.Location,serviceNpc.Location)<=4)
        {
            visited[serviceNpc.Id]=now+60000;await Status("补给中",serviceGoal.Reason);await Call(serviceNpc.Id,"[@MAIN]");return true;
        }
        foreach(var progress in state.Quests.Values.Where(q=>q.Completed && q.QuestInfo!=null && !finishedQuests.Contains(q.Id)))
        {
            var q=progress.QuestInfo;
            var npc=state.Actors.GetValueOrDefault(q.FinishNPCIndex);
            if(npc==null || npc.Hidden)continue;
            if(Distance(user.Location,npc.Location)>4)return await MoveTo(npc.Location,3);
            if(npcId!=npc.Id){await Call(npc.Id,"[@MAIN]");return true;}
            finishedQuests.Add(q.Index);
            var reward=q.RewardsSelectItem.Select((item,index)=>(item,index)).OrderByDescending(pair=>MatchesIdentity(pair.item.Item))
                .ThenByDescending(pair=>Usable(pair.item.Item))
                .ThenByDescending(pair=>EquipmentValue(new UserItem(pair.item.Item))).FirstOrDefault();
            await Asset(new C.FinishQuest{QuestIndex=q.Index,SelectedItemIndex=q.RewardsSelectItem.Count>0?reward.index:-1},"quest",(ulong)q.Index);return true;
        }
        foreach(var q in state.QuestDefinitions.Values.Where(q=>state.CanAcceptQuest(q) && !acceptedQuests.Contains(q.Index)))
        {
            if(Knowledge?.Goal is not {Kind:"accept"} goal || goal.QuestId!=q.Index)continue;
            var task=Knowledge?.Quests.FirstOrDefault(t=>t.Id==q.Index);
            if(task==null)continue;
            var npc=state.Actors.GetValueOrDefault(q.NPCIndex);
            if(npc==null || npc.Hidden || Distance(user.Location,npc.Location)>5)continue;
            if(npcId!=npc.Id){await Call(npc.Id,"[@MAIN]");return true;}
            acceptedQuests.Add(q.Index);
            await Asset(new C.AcceptQuest{NPCIndex=q.NPCIndex,QuestIndex=q.Index},"quest",(ulong)q.Index);return true;
        }
        if(page.Count>0 && npcId!=0)
        {
            var links=page.SelectMany(line=>Regex.Matches(line,@"<[^<>]+/@([^<>]+)>").Select(m=>m.Groups[1].Value)).ToArray();page=[];
            // 只识别原引擎内置服务，未知剧情链接保持未执行。
            var service=links.FirstOrDefault(l=>(l.Equals("SELL",StringComparison.OrdinalIgnoreCase)||l.Equals("BUYSELL",StringComparison.OrdinalIgnoreCase)) && SaleItem(true)!=null) ??
                links.FirstOrDefault(l=>l.Equals("REPAIR",StringComparison.OrdinalIgnoreCase) && user.Inventory.Concat(user.Equipment).Any(RepairHere)) ??
                links.FirstOrDefault(l=>(l.Equals("BUY",StringComparison.OrdinalIgnoreCase)||l.Equals("BUYSELL",StringComparison.OrdinalIgnoreCase)) && (user.Equipment[0]==null||PotionCount(true)<PotionTarget(true)||PotionCount(false)<PotionTarget(false)||TownScrollCount()==0));
            if(service!=null){await Call(npcId,"[@"+service+"]");return true;}
        }
        var needsService=SaleItem()!=null || user.Gold>0 && (user.Equipment[0]==null||PotionCount(true)<PotionTarget(true)||PotionCount(false)<PotionTarget(false)||user.Inventory.Concat(user.Equipment).Any(NeedsRepair));
        if(!needsService || Knowledge?.Goal?.Kind is "accept" or "turn-in" or "service" ||
            Knowledge?.Goal is {Kind:"hunt"} journey && journey.MapId!=Knowledge.MapId)return false;
        var merchant=state.Actors.Values.Where(a=>a.Kind==ObjectType.Merchant && !a.Hidden && visited.GetValueOrDefault(a.Id)<now)
            .OrderBy(a=>Distance(user.Location,a.Location)).FirstOrDefault();
        if(merchant!=null)
        {
            if(Distance(user.Location,merchant.Location)<=4)
            {visited[merchant.Id]=now+300000;await Status("交互中","按补给需要查找 NPC 服务："+merchant.Name);await Call(merchant.Id,"[@MAIN]");return true;}
            if(Distance(user.Location,merchant.Location)<15 && await MoveTo(merchant.Location,3))return true;
        }
        return false;
    }
}
