using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionInventory
{
    public static KnownGoal? Choose(PlayerObject player, NPCObject[] npcs, Dictionary<int, KnownExit?> routes, ISet<string>? blocked, bool recycleOnly = false)
    {
        if (!recycleOnly && !CompanionInventoryRules.NeedsSpace(player.CurrentBagWeight, player.Stats[Stat.BagWeight], player.Info.Inventory.Count(item => item == null))) return null;
        var protectedItems = Envir.Main.QuestInfoList.SelectMany(quest => quest.ItemTasks.Concat(quest.CarryItems)).Select(task => task.Item.Index)
            .Concat(Envir.Main.RecipeInfoList.SelectMany(recipe => (recipe.Ingredients ?? []).Concat(recipe.Tools ?? [])).Select(item => item.ItemIndex)).ToHashSet();
        CompanionHeroes.ProtectInventory(player, protectedItems);
        var caster = player.Info.Magics.Any(magic => magic.Info.Spell is Spell.FireBall or Spell.GreatFireBall or Spell.ThunderBolt or Spell.SoulFireBall);
        var spare = CompanionInventoryRules.SurplusArmour(player.Info.Inventory,player.Info.Equipment[(int)EquipmentSlot.Armour],protectedItems,player.Level)
            .Concat(CompanionInventoryRules.SurplusWeapons(player.Info.Inventory, player.Info.Equipment[(int)EquipmentSlot.Weapon],
                player.Class, player.Gender, player.Level, caster, protectedItems)).Select(item => new KnownSale(item.UniqueID, item.Count))
            .Concat(CompanionInventoryRules.SurplusMaterials(player.Info.Inventory, protectedItems, player.Level))
            .Concat(player.Info.Inventory.Where(item=>CompanionInventoryRules.PlainMeat(item,protectedItems,player.Level))
                .Select(item=>new KnownSale(item.UniqueID,item.Count))).ToArray();
        if (spare.Length == 0) return null;
        var inventory = player.Info.Inventory.Where(item => item != null).ToDictionary(item => item.UniqueID);
        if (recycleOnly)
        {
            var allowed = spare.Where(sale => ItemRecycling.Rejection(inventory[sale.UniqueId]).Length == 0).ToArray();
            return allowed.Length == 0 || blocked?.Contains("public-recycle") == true ? null :
                new("service", player.CurrentMap.Info.Index, player.CurrentLocation.X, player.CurrentLocation.Y,
                    "原地回收普通余货，保留任务、配方、备装及基础储备", Key: "public-recycle", SellItems: allowed);
        }
        var vendor = npcs.Where(npc => blocked?.Contains($"unload/{npc.ObjectID}") != true)
            .Select(npc =>
            {
                var script = NPCScript.Get(npc.ScriptID);
                var sales = script.NPCPages.Any(page => page.Key.Equals(NPCScript.SellKey, StringComparison.OrdinalIgnoreCase) ||
                    page.Key.Equals(NPCScript.BuySellKey, StringComparison.OrdinalIgnoreCase))
                    ? spare.Where(sale => script.Types.Count == 0 || script.Types.Contains(inventory[sale.UniqueId].Info.Type)).ToArray() : [];
                return (npc, sales);
            }).Where(pair => pair.sales.Length > 0)
            .OrderBy(pair => pair.npc.CurrentMap == player.CurrentMap ? Functions.MaxDistance(player.CurrentLocation, pair.npc.CurrentLocation) : 10000).FirstOrDefault();
        return vendor.npc == null ? null : new("service", vendor.npc.CurrentMap.Info.Index, vendor.npc.CurrentLocation.X, vendor.npc.CurrentLocation.Y,
            $"背包负重 {player.CurrentBagWeight:0.##}/{player.Stats[Stat.BagWeight]}，出售普通余货：{vendor.npc.Name}", NpcId: vendor.npc.ObjectID,
            NextExit: routes[vendor.npc.CurrentMap.Info.Index], Key: $"unload/{vendor.npc.ObjectID}", SellItems: vendor.sales);
    }
}
