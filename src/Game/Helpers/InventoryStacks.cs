namespace Server.Helpers;

public static class InventoryStacks
{
    public static bool CanAdd(UserItem[] inventory, IEnumerable<UserItem> items) => AfterAdding(inventory, items) != null;

    public static UserItem[] AfterAdding(UserItem[] inventory, IEnumerable<UserItem> items)
    {
        var slots = inventory.Select(item => item?.Clone()).ToArray();
        foreach (var item in items.Where(item => item != null))
        {
            var remaining = item.Clone();
            foreach (var target in slots)
            {
                if (!remaining.CanStackWith(target) || target.Count >= target.Info.StackSize) continue;
                var count = Math.Min(remaining.Count, target.Info.StackSize - target.Count);
                target.Count += (ushort)count; remaining.Count -= (ushort)count;
                if (remaining.Count == 0) break;
            }
            if (remaining.Count == 0) continue;
            var index = Array.IndexOf(slots, null);
            if (index < 0) return null;
            slots[index] = remaining;
        }
        return slots;
    }

    public static void Compact(UserItem[] inventory)
    {
        for (var to = 0; to < inventory.Length; to++)
        {
            var target = inventory[to];
            if (target?.Info == null || target.Info.StackSize <= 1) continue;
            for (var from = to + 1; from < inventory.Length && target.Count < target.Info.StackSize; from++)
            {
                var source = inventory[from];
                if (source?.Info == null || !target.CanStackWith(source)) continue;
                var count = Math.Min(source.Count, target.Info.StackSize - target.Count);
                target.Count += (ushort)count; source.Count -= (ushort)count;
                if (source.Count == 0) inventory[from] = null;
            }
        }
    }
}
