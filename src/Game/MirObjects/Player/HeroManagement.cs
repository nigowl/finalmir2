using S = ServerPackets;

namespace Server.MirObjects;

public partial class PlayerObject
{
    public void EnsureHeroCapacity()
    {
        if (Info.Heroes == null || Info.Heroes.Length < Settings.MaximumHeroCount)
            Array.Resize(ref Info.Heroes, Settings.MaximumHeroCount);
        // 旧存档可能已有更多英雄，只停止新增，不截断原有资产。
        Info.MaximumHeroCount = Info.Heroes.Length;
    }

    public void DeleteManagedHero(ClientPackets.DeleteHeroRequest request)
    {
        void Reply(bool success, string message) => Enqueue(new S.DeleteHeroResult
            { HeroIndex = request.HeroIndex, Success = success, Message = message });
        var npc = CurrentMap?.NPCs.FirstOrDefault(candidate => candidate.ObjectID == NPCObjectID);
        if (!IsAtNPC(NPCScript.HeroManageKey) || npc == null || !npc.Visible ||
            !npc.VisibleLog.TryGetValue(Info.Index, out var visible) || !visible || TradePartner != null ||
            ItemRentalPartner != null || Fishing || Hero?.LogTime > Envir.Time)
        { Reply(false, "无法删除英雄：请靠近英雄使者，并等待英雄脱离战斗。" ); return; }
        var slot = Array.FindIndex(Info.Heroes, hero => hero != null && hero.Index == request.HeroIndex && !hero.Deleted);
        if (slot < 0) { Reply(false, "该英雄不存在或不属于当前角色，请重新打开管理菜单。" ); return; }
        var selected = Info.Heroes[slot];
        if (selected.Equipment.Any(item => item != null) || selected.Inventory.Any(item => item != null))
        { Reply(false, "请先将该英雄设为当前英雄，卸下全部装备并取回物品后再删除。" ); return; }
        if (selected == CurrentHero)
        {
            if (Hero != null) DespawnHero();
            Info.HeroSpawned = false;
            Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.None });
            CurrentHero = null;
        }
        Info.Heroes[slot] = null;
        selected.Deleted = true; selected.DeleteDate = Envir.Now;
        if (Connection != null) Connection.HeroStorageSent = false;
        Reply(true, $"已删除英雄“{selected.Name}”。" );
        if (Connection != null) ManageHeroes();
    }
}
