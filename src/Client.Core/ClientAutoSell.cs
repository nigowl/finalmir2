namespace Crystal.Native;

public static class ClientAutoSell
{
    public static UserItem? Next(GameState state, ClientPreferences preferences, ISet<ulong> excluded)
    {
        if (!preferences.AutoSell || state.User == null || state.Me?.Dead != false || state.Trading || state.StorageActive) return null;
        return state.User.Inventory.Concat(state.User.QuestInventory).FirstOrDefault(item => item?.Info != null && item.Count > 0 && !excluded.Contains(item.UniqueID) && Allowed(state, preferences, item));
    }

    public static bool Allowed(GameState state, ClientPreferences preferences, UserItem item)
    {
        var info = item.Info;
        var explicitRule = info != null && preferences.ItemRules.TryGetValue(info.FriendlyName, out var rule) && rule.HasFlag(ItemActions.Recycle);
        if (info == null || state.User == null || ItemRecycling.Rejection(item, allowUnidentified: explicitRule).Length > 0 ||
            !preferences.ForItem(info.FriendlyName).HasFlag(ItemActions.Recycle)) return false;
        if (!preferences.ItemRules.ContainsKey(info.FriendlyName) && !preferences.SellWhitelist &&
            info.Type is not (ItemType.Meat or ItemType.Food or ItemType.CraftingMaterial or ItemType.Ore)) return false;
        // 客户端只有任务显示信息；存在任务物品引用的普通材料也保守保留。
        if (state.Quests.Values.Any(quest => quest.TaskList.Any(line => line.Contains(info.Name, StringComparison.Ordinal)))) return false;
        return true;
    }
}
