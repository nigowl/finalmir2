using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public static class BasicDropBalance
{
    public static int Tier(int level, int hp)
    {
        int[] levels = [10, 20, 30, 40, 50, 65];
        int[] health = [50, 150, 500, 2000, 10000, 50000];
        return Enumerable.Range(0, levels.Length).FirstOrDefault(i => level <= levels[i] && hp <= health[i], 6);
    }
    public static bool GeneralEquipment(ItemInfo item) => item.IsEquipment &&
        (int)item.RequiredClass != 31 && (int)item.RequiredClass > 0 && ((int)item.RequiredClass & ((int)item.RequiredClass - 1)) != 0;

    internal static (Action Undo, bool Changed) PrepareItems()
    {
        var world = Envir.Main;
        var changed = world.ItemInfoList.Where(GeneralEquipment).Select(item => (Item: item, Before: item.RequiredClass)).ToArray();
        var added = new List<ItemInfo>(); var counter = world.ItemIndex;
        foreach (var pair in changed) pair.Item.RequiredClass = (RequiredClass)31;
        try
        {
            AddMedicine("疗伤药", 300, 670); AddMedicine("万年雪霜", 500, 671);
        }
        catch { Undo(); throw; }
        return (Undo, changed.Length + added.Count > 0);
        void AddMedicine(string name, int restore, int sourceId)
        {
            if (world.ItemInfoList.Any(item => item.Name == name)) return;
            var source = world.ItemInfoList.Single(item => item.Index == sourceId && item.Type == ItemType.Potion && item.Shape == 1);
            using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) source.Save(writer);
            memory.Position = 0; using var reader = new BinaryReader(memory);
            var item = new ItemInfo(reader, Envir.Version, Envir.CustomVersion)
            { Index = Math.Max(world.ItemIndex, world.ItemInfoList.Max(item => item.Index)) + 1, Name = name, StartItem = false };
            item.Stats[Stat.HP] = item.Stats[Stat.MP] = restore;
            world.ItemIndex = item.Index; world.ItemInfoList.Add(item); added.Add(item);
        }
        void Undo()
        {
            foreach (var pair in changed) pair.Item.RequiredClass = pair.Before;
            foreach (var item in added) world.ItemInfoList.Remove(item);
            world.ItemIndex = counter;
        }
    }

    internal static DropInfo[] Rebuild(MonsterInfo monster, IEnumerable<DropInfo> previous)
    {
        var tier = Tier(monster.Level, monster.Stats[Stat.HP]);
        uint[] gold = [100, 200, 500, 1000, 2000, 5000, 10000];
        int[][] medicines = [[658, 659], [660, 661], [662, 663], [664, 665], [666, 667]];
        var items = tier < medicines.Length ? medicines[tier].Select(id => Envir.Main.GetItemInfo(id)).ToArray() :
            [Envir.Main.GetItemInfo(tier == 5 ? "疗伤药" : "万年雪霜")];
        if (items.Any(item => item == null || item.Type != ItemType.Potion || item.Shape > 1 || item.Stats[Stat.HP] + item.Stats[Stat.MP] <= 0))
            throw new InvalidDataException("所选版本缺少分档补给药品，未重写掉落。");
        return Strip(previous).Concat([new DropInfo { Probability = .5m, Gold = gold[tier] }])
            .Concat(items.Select(item => new DropInfo { Probability = .5m, Item = item })).ToArray();
    }
    private static IEnumerable<DropInfo> Strip(IEnumerable<DropInfo> previous)
    {
        foreach (var drop in previous)
        {
            if (drop.QuestRequired) { yield return drop; continue; }
            if (drop.GroupedDrop != null)
            {
                var children = Strip(drop.GroupedDrop).ToArray();
                if (children.Length == 0) continue;
                var group = new GroupDropInfo { Random = drop.GroupedDrop.Random, First = drop.GroupedDrop.First }; group.AddRange(children);
                yield return new DropInfo { Chance = drop.Chance, Probability = drop.Probability, GroupedDrop = group, Type = drop.Type };
            }
            else if (drop.Gold == 0 && !(drop.Item?.Type == ItemType.Potion && drop.Item.Shape <= 1 && (drop.Item.Stats[Stat.HP] > 0 || drop.Item.Stats[Stat.MP] > 0))) yield return drop;
        }
    }
}
