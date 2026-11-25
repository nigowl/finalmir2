using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionHeroes
{
    public static KnownGoal? Choose(PlayerObject player, NPCObject[] npcs, Dictionary<int, KnownExit?> routes, ISet<string>? blocked)
    {
        if (!Server.Settings.AllowNewHero || player.Level < Server.Settings.Hero_RequiredLevel ||
            !Server.Settings.Hero_CanCreateClass.Any(enabled => enabled) || player.Info.MaximumHeroCount == 0 ||
            player.Info.Heroes.Any(hero => hero != null) || player.Info.Inventory.Any(item => item?.Info.Type == ItemType.SealedHero)) return null;
        var seal = Envir.Main.GetItemInfo(Server.Settings.HeroSealItemName);
        if (seal != null && (!player.CanGainItem(new UserItem(seal)) || player.CurrentBagWeight + seal.UnitWeight > player.Stats[Stat.BagWeight])) return null;
        var npc = npcs.Where(npc => blocked?.Contains($"hero-create/{npc.ObjectID}") != true &&
                OffersCreation(NPCScript.Get(npc.ScriptID)))
            .OrderBy(npc => npc.CurrentMap == player.CurrentMap ? Functions.MaxDistance(player.CurrentLocation, npc.CurrentLocation) : 10000).FirstOrDefault();
        return npc == null ? null : new("service", npc.CurrentMap.Info.Index, npc.CurrentLocation.X, npc.CurrentLocation.Y,
            $"前往英雄使者创建首名英雄：{npc.Name}", NpcId: npc.ObjectID, NextExit: routes[npc.CurrentMap.Info.Index], Key: $"hero-create/{npc.ObjectID}");
    }

    private static bool OffersCreation(NPCScript script) =>
        script.NPCPages.Any(page => page.Key.Equals(NPCScript.HeroCreateKey, StringComparison.OrdinalIgnoreCase)) &&
        // 自定义扣款/传送剧情尚未接入请求锁，不能在读取创建许可时自动执行。
        script.NPCPages.Where(page => page.Key.Equals(NPCScript.MainKey, StringComparison.OrdinalIgnoreCase) ||
                page.Key.Equals(NPCScript.HeroCreateKey, StringComparison.OrdinalIgnoreCase))
            .All(page => page.SegmentList.All(segment => segment.ActList.Count == 0 && segment.ElseActList.Count == 0));

    public static void ProtectInventory(PlayerObject player, ISet<int> protectedItems)
    {
        if (player.CurrentHero is not { } hero) return;
        protectedItems.UnionWith(CompanionHeroRules.ProtectedItems(player.Info.Inventory, hero.Class, hero.Gender, hero.Level,
            hero.Equipment, hero.Magics.Select(magic => magic.Info.Spell).ToHashSet()));
    }
}
