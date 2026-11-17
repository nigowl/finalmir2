using System.Security.Cryptography;
using System.Text;
using Mir2.Companions;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private bool storageOnly,storagePage,storageSnapshot,splitAcknowledged;
    private int storageFrom,storageTo,storageOriginalCount,storageDefinition;
    private ushort storageCount;
    private long storageActivity;
    private string storageRevision="";
    // 独立于登录密码且不依赖目录/昵称；完整复制版本后仍可解锁，不新增浏览器或存档密钥。
    private string StoragePassword()=>Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(start.Password),
        Encoding.UTF8.GetBytes("Mir2.Companion.Storage/v1:"+start.Account)))[..14];
    private bool AtStorage()=>state.StorageActive&&state.User!=null&&state.Me?.Dead==false&&!state.Trading&&
        state.Actors.GetValueOrDefault(npcId) is {Kind:ObjectType.Merchant,Hidden:false} npc&&Distance(state.User.Location,npc.Location)<=Globals.DataRange;
    private void CloseStorage(bool completed=false)
    {if(completed)visited.Remove(npcId);storagePage=storageOnly=false;state.StorageActive=state.StorageUnlocked=false;}
    private void StoragePacket(Packet packet)
    {
        if(packet is S.UserStorage)storageSnapshot=true;
        if(packet is S.NPCStorage)
        {storagePage=storageOnly;pendingNpc=0;storageSnapshot=state.Storage.Length>0;StorageActivity();}
    }
    private void StorageActivity()
    {storageActivity=Environment.TickCount64;storageRevision=Knowledge?.Revision??"";lastProgress=storageActivity;}
    private async Task<bool> UseStorage(long now)
    {
        if(!storagePage)return false;
        if(!AtStorage()){CloseStorage();return false;}
        var user=state.User!;
        if(user.RequireStoragePassword&&!user.HasStoragePassword)
        {await Asset(new C.SetStoragePassword{NewPassword=StoragePassword()},"storage-password",0);return true;}
        if(!state.StorageUnlocked)
        {await Asset(new C.UnlockStorage{Password=StoragePassword()},"storage-unlock",0);return true;}
        if(!storageSnapshot)
        {if(now-storageActivity>5000)throw new TimeoutException("仓库快照未确认，已停止且不会执行存取。");return true;}
        foreach(var move in Knowledge?.Goal?.NpcId==npcId?Knowledge.Goal.StorageItems??[]:[])
            if(await StorageMove(move))return true;
        // 等待宿主下一次知识快照决定取回基础储备，不能在存入回执后按旧目标反复操作。
        if(Knowledge?.Revision==storageRevision&&now-storageActivity<5000)return true;
        CloseStorage(true);return false;
    }
    private async Task<bool> StorageMove(KnownStorage move)
    {
        var user=state.User!;var source=move.Take?state.Storage:user.Inventory;
        var item=source.FirstOrDefault(item=>item?.UniqueID==move.UniqueId);
        var quests=Knowledge?.Quests.SelectMany(q=>q.ItemIds).ToHashSet()??[];
        if(item==null||refused.Contains(move.UniqueId))return false;
        var full=CompanionInventoryRules.NeedsSpace(state.InventoryWeight,state.CalculateStats()[Stat.BagWeight],user.Inventory.Count(i=>i==null));
        if(!move.Take)
        {
            var caster=user.Magics.Any(m=>ClientAutomation.IsSingleSkill(m.Spell)&&m.Spell!=Spell.None&&!ClientAutomation.IsAttackSkill(m.Spell));
            if(!full||move.Count!=item.Count||!CompanionInventoryRules.StorageUnused(user.Inventory,user.Equipment,quests,user.Class,user.Gender,user.Level,
                user.Magics.Select(m=>m.Spell).ToHashSet(),caster).Concat(CompanionInventoryRules.StorageSurplus(user.Inventory,quests,user.Level,
                Enumerable.Range(0,state.StorageCapacity).Count(i=>state.Storage[i]==null))).Any(i=>i.UniqueID==item.UniqueID))return false;
            return await StorageTransfer(item,false);
        }
        if(!CompanionInventoryRules.StorageMaterial(item,quests,user.Level))return false;
        var count=user.Inventory.Where(i=>i?.ItemIndex==item.ItemIndex).Sum(i=>(int)i.Count);
        var amount=(ushort)Math.Min(Math.Min(move.Count,item.Count),Math.Max(0,5-count));
        var room=Math.Max(0,state.CalculateStats()[Stat.BagWeight]*.8m-state.InventoryWeight);
        amount=(ushort)Math.Min(amount,room/item.Info.UnitWeight);
        if(full||amount==0||user.Inventory.Count(i=>i==null)<=2)return false;
        if(amount==item.Count)return await StorageTransfer(item,true);
        if(!Enumerable.Range(0,state.StorageCapacity).Any(i=>state.Storage[i]==null))return false;
        storageOriginalCount=item.Count;storageCount=amount;storageDefinition=item.ItemIndex;splitAcknowledged=false;
        await Asset(new C.SplitItem{Grid=MirGridType.Storage,UniqueID=item.UniqueID,Count=amount},"storage-split",item.UniqueID);return true;
    }
    private async Task<bool> StorageTransfer(UserItem item,bool take)
    {
        if(!AtStorage()||!state.StorageUnlocked)return false;
        if(take&&!CompanionInventoryRules.CanCollect(state.InventoryWeight,state.CalculateStats()[Stat.BagWeight],
            state.User!.Inventory.Count(i=>i==null),item.Weight,false,false))return false;
        storageFrom=Array.FindIndex(take?state.Storage:state.User!.Inventory,i=>i?.UniqueID==item.UniqueID);
        storageTo=take?Array.IndexOf(state.User!.Inventory,null):Enumerable.Range(0,state.StorageCapacity).FirstOrDefault(i=>state.Storage[i]==null,-1);
        if(storageFrom<0||storageTo<0||take&&storageFrom>=state.StorageCapacity)return false;
        storageCount=item.Count;
        await Status(take?"取回中":"存仓中",take?"按原仓库回执取回基础材料储备":"自动暂存闲置物品，保留任务物、升级装备和战斗补给");
        await Asset(take?new C.TakeBackItem{From=storageFrom,To=storageTo}:new C.StoreItem{From=storageFrom,To=storageTo},
            take?"storage-take":"storage-store",item.UniqueID);return true;
    }
}
