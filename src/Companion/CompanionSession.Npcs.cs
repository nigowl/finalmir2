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
    private bool repairPage,sellPage,unloadOnly,repairOnly;
    private float repairRate=1;
    private ulong repairItem;
    private int repairSlot=-1;
    private readonly Dictionary<uint,(long Until,uint Gold)> visited=[];
    private readonly HashSet<int> acceptedQuests=[],finishedQuests=[];
    private long npcSent;
    private async Task Call(uint id,string key)
    {
        if(key.Equals("[@MAIN]",StringComparison.OrdinalIgnoreCase))
        {unloadOnly=Knowledge?.Goal?.SellItems is {Length:>0};repairOnly=Knowledge?.Goal?.Key.StartsWith("repair/",StringComparison.Ordinal)==true;
            storageOnly=Knowledge?.Goal?.StorageItems is {Length:>0};}
        storagePage=false;
        npcId=pendingNpc=id; npcSent=Environment.TickCount64; page=[]; goods=null; repairPage=sellPage=false;
        await connection.Send(new C.CallNPC{ObjectID=id,Key=key}); nextAction=npcSent+500;
    }
    private int PotionCount(bool hp)=>CompanionSupplyRules.Count(state.User!.Inventory,hp?Stat.HP:Stat.MP);
    private int PotionTarget(bool hp)=>CompanionSupplyRules.Target(hp?Stat.HP:Stat.MP,state.User!.Class,Profile.Traits.Frugality);
    private int TownScrollCount()=>CompanionSupplyRules.TownCount(state.User!.Inventory,state.User.Class,state.User.Gender,state.User.Level);
    private bool NeedsRepair(UserItem? item)=>CompanionSupplyRules.NeedsRepair(item,state.User!.Gold) && !refused.Contains(item!.UniqueID);
    private bool Trades(ItemInfo item) =>
        Knowledge?.Npcs.FirstOrDefault(n=>n.ObjectId==npcId)?.TradeTypes is { } types &&
        (types.Length==0||types.Contains((int)item.Type));
    private bool RepairHere(UserItem? item)
    {
        if(!NeedsRepair(item)||!Trades(item!.Info))return false;
        var cost=item.RepairPrice()*(double)repairRate;
        return cost>=0 && cost<state.User!.Gold/2d;
    }
    private int SupplyPriority(UserItem item)
    {
        if(CompanionSupplyRules.IsPotion(item,Stat.HP) && PotionCount(true)==0)return -1;
        if(item.Info.Type!=ItemType.Potion)return CompanionSupplyRules.IsTownScroll(item)?2:3;
        var stat=item.Info.Stats[Stat.HP]>0?Stat.HP:Stat.MP;
        return CompanionSupplyRules.IsPotion(item,stat) && CompanionSupplyRules.NeedsRefill(state.User!.Inventory,stat,state.User.Class,Profile.Traits.Frugality)?0:1;
    }
    private async Task<bool> BuySupplies()
    {
        var shop=goods!;var user=state.User!;
        // 最后容量须先补低于储备线的药品，不能被便宜药的补满占掉。
        foreach(var item in shop.List.Where(i=>i.Info!=null && Usable(i.Info)).OrderBy(SupplyPriority).ThenBy(i=>i.Price()))
        {
            if(Funding && !CompanionSupplyRules.IsPotion(item,Stat.HP))continue;
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
                missing=CompanionInventoryRules.DevelopmentCount(item,user.Inventory,user.Equipment,user.Class,user.Gender,
                    user.Level,user.Magics.Select(m=>m.Spell).ToHashSet());
                if(missing==0)continue;
            }
            var unit=item.Clone();unit.Count=1;
            var price=unit.Price()*(double)shop.Rate;
            if(missing<=0||!user.Inventory.Any(x=>x==null))continue;
            var limit=CompanionSupplyRules.PurchaseLimit(item,price,user.Gold,Profile.Traits.Frugality,PotionCount(true));
            var count=(ushort)Math.Min(Math.Min(missing,item.Info.StackSize),Math.Min(item.Count,limit));
            if(item.Info.UnitWeight>0)count=(ushort)Math.Min(count,Math.Max(0,state.CalculateStats()[Stat.BagWeight]-state.InventoryWeight)/item.Info.UnitWeight);
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
        { if(now-npcSent<3000)return true; visited[pendingNpc]=(now+60000,user.Gold); pendingNpc=0; }
        if(await CreateGuild(now))return true;
        if(await UseStorage(now))return true;
        if(sellPage && await SellSurplus())return true;
        if(goods!=null)
        {
            // 减负会话不顺便购装，避免刚腾出空间又被买回的装备占满。
            if(!unloadOnly && !repairOnly && await BuySupplies())return true;
            goods=null;
        }
        if(repairPage)
        {
            // 主动修理只处理已穿装备及刚卸下的同一件，不能先修整包备用品耗尽药钱。
            var worn=user.Inventory.FirstOrDefault(i=>RepairHere(i) && (repairOnly?i!.UniqueID==repairItem:repairItem==0||i!.UniqueID==repairItem));
            if(worn!=null){repairItem=worn.UniqueID;await Asset(new C.RepairItem{UniqueID=worn.UniqueID},"repair",worn.UniqueID);return true;}
            var equipped=user.Equipment.FirstOrDefault(RepairHere);
            var empty=Array.IndexOf(user.Inventory,null);
            if(equipped!=null && empty>=0)
            {repairItem=equipped.UniqueID;repairSlot=Array.IndexOf(user.Equipment,equipped);await Asset(new C.RemoveItem{Grid=MirGridType.Inventory,UniqueID=equipped.UniqueID,To=empty},"remove",equipped.UniqueID);return true;}
            repairPage=false;repairItem=0;repairSlot=-1;
            if(npcId!=0 && !repairOnly){await Call(npcId,"[@MAIN]");return true;}
        }
        // 筹资后金币已增加，原缺钱访问的冷却不能挡住明确的新补药计划。
        if(Knowledge?.Goal is {Kind:"service"} serviceGoal && (visited.GetValueOrDefault(serviceGoal.NpcId).Until<now ||
            serviceGoal.Key.StartsWith("supply/",StringComparison.Ordinal)&&user.Gold>visited.GetValueOrDefault(serviceGoal.NpcId).Gold) &&
            state.Actors.GetValueOrDefault(serviceGoal.NpcId) is {Hidden:false} serviceNpc && Distance(user.Location,serviceNpc.Location)<=4)
        {
            visited[serviceNpc.Id]=(now+60000,user.Gold);await Status(serviceGoal.Key.StartsWith("quest-flag/",StringComparison.Ordinal)?"交互中":"补给中",serviceGoal.Reason);await Call(serviceNpc.Id,"[@MAIN]");return true;
        }
        foreach(var progress in state.Quests.Values.Where(q=>q.Completed && q.QuestInfo!=null && !finishedQuests.Contains(q.Id)))
        {
            var q=progress.QuestInfo;
            // 满包时服务器可能已安排减负，不能因附近有交付NPC就抢先发送会被拒绝的任务操作。
            if(Knowledge?.Goal is not {Kind:"turn-in"} turnIn || turnIn.QuestId!=q.Index)continue;
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
            if(storageOnly)
            {
                var storageLink=links.FirstOrDefault(link=>link.Equals("STORAGE",StringComparison.OrdinalIgnoreCase));
                if(storageLink!=null){await Call(npcId,"[@"+storageLink+"]");return true;}
                await report(new("goal-blocked",Profile.Id,Reason:"当前NPC没有实际可用的仓库链接",Text:$"warehouse/{npcId}"));
                CloseStorage();return true;
            }
            // 只识别原引擎内置服务，未知剧情链接保持未执行。
            var service=links.FirstOrDefault(l=>!repairOnly && (l.Equals("SELL",StringComparison.OrdinalIgnoreCase)||l.Equals("BUYSELL",StringComparison.OrdinalIgnoreCase)) && SaleItem(true)!=null) ??
                links.FirstOrDefault(l=>l.Equals("REPAIR",StringComparison.OrdinalIgnoreCase) && user.Inventory.Concat(user.Equipment).Any(RepairHere)) ??
                links.FirstOrDefault(l=>!repairOnly && (l.Equals("BUY",StringComparison.OrdinalIgnoreCase)||l.Equals("BUYSELL",StringComparison.OrdinalIgnoreCase)) && (Knowledge?.Goal?.Key.StartsWith("develop/",StringComparison.Ordinal)==true||user.Equipment[0]==null||PotionCount(true)<PotionTarget(true)||PotionCount(false)<PotionTarget(false)||TownScrollCount()==0));
            if(service!=null){await Call(npcId,"[@"+service+"]");return true;}
        }
        var needsService=SaleItem()!=null || user.Gold>0 && (user.Equipment[0]==null||PotionCount(true)<PotionTarget(true)||PotionCount(false)<PotionTarget(false)||user.Inventory.Concat(user.Equipment).Any(NeedsRepair));
        if(!needsService || Funding || Knowledge?.Goal?.Kind is "accept" or "turn-in" or "service" ||
            Knowledge?.Goal is {Kind:"hunt"} journey && journey.MapId!=Knowledge.MapId)return false;
        var merchant=state.Actors.Values.Where(a=>a.Kind==ObjectType.Merchant && !a.Hidden && visited.GetValueOrDefault(a.Id).Until<now)
            .OrderBy(a=>Distance(user.Location,a.Location)).FirstOrDefault();
        if(merchant!=null)
        {
            if(Distance(user.Location,merchant.Location)<=4)
            {visited[merchant.Id]=(now+300000,user.Gold);await Status("交互中","按补给需要查找 NPC 服务："+merchant.Name);await Call(merchant.Id,"[@MAIN]");return true;}
            if(Distance(user.Location,merchant.Location)<15 && await MoveTo(merchant.Location,3))return true;
        }
        return false;
    }
}
