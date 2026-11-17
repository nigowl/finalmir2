using Mir2.Companions;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionRepairs
{
    public static KnownGoal? Choose(PlayerObject player, NPCObject[] npcs, Dictionary<int, KnownExit?> routes, ISet<string>? blocked)
    {
        if (!player.Info.Inventory.Any(item => item == null)) return null;
        var damaged = player.Info.Equipment.Where(item => CompanionSupplyRules.NeedsRepair(item, player.Account.Gold)).ToArray();
        if (damaged.Length == 0) return null;
        var vendor = npcs.Where(npc => blocked?.Contains($"repair/{npc.ObjectID}") != true)
            .Where(npc =>
            {
                var script = NPCScript.Get(npc.ScriptID);
                var rate = NPCScript.PriceRate(player, npc);
                return script.NPCPages.Any(page => page.Key.Equals(NPCScript.RepairKey, StringComparison.OrdinalIgnoreCase)) &&
                    damaged.Any(item => (script.Types.Count == 0 || script.Types.Contains(item.Info.Type)) &&
                        item.RepairPrice() * (double)rate >= 0 && item.RepairPrice() * (double)rate < player.Account.Gold / 2d);
            })
            // shortcut: 商人沿用同图优先的直线排序，跨图往返明显绕远时再按实际路径成本评估。
            .OrderBy(npc => npc.CurrentMap == player.CurrentMap ? Functions.MaxDistance(player.CurrentLocation, npc.CurrentLocation) : 10000)
            .FirstOrDefault();
        return vendor == null ? null : new("service", vendor.CurrentMap.Info.Index, vendor.CurrentLocation.X, vendor.CurrentLocation.Y,
            $"已穿装备耐久不足，前往修理：{vendor.Name}", NpcId: vendor.ObjectID, NextExit: routes[vendor.CurrentMap.Info.Index], Key: $"repair/{vendor.ObjectID}");
    }
}
