namespace Server.Helpers;

public static class InventoryStacks
{
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
