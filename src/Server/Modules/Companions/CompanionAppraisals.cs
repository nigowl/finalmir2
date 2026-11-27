using Mir2.Companions;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionAppraisals
{
    public static KnownGoal? Choose(PlayerObject player, CompanionProfile profile, NPCObject[] npcs,
        Dictionary<int, KnownExit?> routes, ISet<string>? blocked)
    {
        if (!player.Info.Inventory.Any(item => item == null) || player.HP*100L<=player.Stats[Stat.HP]*65L ||
            CompanionSupplyRules.NeedsRefill(player.Info.Inventory,Stat.HP,player.Class,profile.Traits.Frugality,player.Level)) return null;
        var scroll = player.Info.Inventory.Any(scroll => scroll != null && EquipmentAppraisal.IsScroll(scroll.Info) &&
            CompanionSupplyRules.Usable(scroll.Info,player.Class,player.Gender,player.Level) && scroll.RentalInformation == null && scroll.SealedInfo == null);
        var hero = player.HeroSpawned && !player.Hero.Dead ? player.Hero : null;
        bool Worth(UserItem item, HumanObject subject) => (subject == player || !item.Info.Bind.HasFlag(BindMode.NoHero)) &&
            CompanionInventoryRules.WorthAppraising(item,subject.Info.Equipment,subject.Class,subject.Gender,subject.Level,
                CompanionInventoryRules.Caster(subject.Info.Magics.Select(magic => magic.Spell)));
        var item = player.Info.Equipment.Concat(hero?.Info.Equipment ?? []).Concat(player.Info.Inventory)
            .FirstOrDefault(item => item != null && (item.SoulBoundId < 0 || item.SoulBoundId == player.Info.Index) &&
                (item.ExpireInfo == null || item.ExpireInfo.ExpiryDate > Server.MirEnvir.Envir.Main.Now) &&
                (Worth(item,player) || hero != null && Worth(item,hero)) &&
                (scroll || EquipmentAppraisal.Price(item) <= player.Account.Gold / 2) && blocked?.Contains($"appraise/{item.UniqueID}") != true);
        if (item == null) return null;
        if (scroll) return new("service",player.CurrentMap.Info.Index,player.CurrentLocation.X,player.CurrentLocation.Y,
            "使用已有卷轴鉴定有提升潜力的装备",Key:$"appraise/{item.UniqueID}",AppraisalItemId:item.UniqueID);
        var distances = CompanionSupplies.RouteDistances(player,routes);
        var npc = npcs.Where(npc => player.Level>=10 || npc.CurrentMap==player.CurrentMap &&
            Functions.MaxDistance(player.CurrentLocation,npc.CurrentLocation)<=64).Where(npc =>
        {
            var script = NPCScript.Get(npc.ScriptID);
            return script.NPCPages.Any(page => page.Key == EquipmentAppraisal.Page) &&
                script.NPCPages.Where(page => page.Key == NPCScript.MainKey || page.Key == EquipmentAppraisal.Page)
                    .All(page => page.SegmentList.All(segment => segment.ActList.Count == 0 && segment.ElseActList.Count == 0));
        }).OrderBy(npc => distances[npc.CurrentMap.Info.Index].Hops)
            .ThenBy(npc => Functions.MaxDistance(distances[npc.CurrentMap.Info.Index].Entry,npc.CurrentLocation)).FirstOrDefault();
        return npc == null ? null : new("service",npc.CurrentMap.Info.Index,npc.CurrentLocation.X,npc.CurrentLocation.Y,
            $"鉴定有提升潜力的装备：{npc.Name}",NpcId:npc.ObjectID,NextExit:routes[npc.CurrentMap.Info.Index],
            Key:$"appraise/{item.UniqueID}",AppraisalItemId:item.UniqueID);
    }
}
