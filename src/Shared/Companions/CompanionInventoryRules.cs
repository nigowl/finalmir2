#nullable enable
namespace Mir2.Companions;

public static class CompanionInventoryRules
{
    public static int UpgradeSlot(UserItem item, IReadOnlyList<UserItem?> equipment, MirClass role, bool caster)
    {
        if (MagicBloodStone.IsStone(item.Info))
            return equipment.Count > 3 && MagicBloodStone.Remaining(item) > MagicBloodStone.Remaining(equipment[3]) ? 3 : -1;
        int[] slots = item.Info.Type switch
        {
            ItemType.Weapon => [0], ItemType.Armour => [1], ItemType.Helmet => [2], ItemType.Torch => [3],
            ItemType.Necklace => [4], ItemType.Bracelet => [5, 6], ItemType.Ring => [7, 8],
            ItemType.Amulet => [9], ItemType.Belt => [10], ItemType.Boots => [11], ItemType.Stone => [12], _ => []
        };
        return slots.Where(slot => slot < equipment.Count && (equipment[slot] == null || !MagicBloodStone.IsStone(equipment[slot]!.Info) &&
                EquipmentValue(item, role, caster) > EquipmentValue(equipment[slot]!, role, caster)))
            .OrderBy(slot => equipment[slot] == null ? int.MinValue : EquipmentValue(equipment[slot]!, role, caster))
            .DefaultIfEmpty(-1).First();
    }

    public static bool Caster(IEnumerable<Spell> spells) => spells.Any(spell => spell is Spell.FireBall or Spell.GreatFireBall or
        Spell.ThunderBolt or Spell.FrostCrunch or Spell.Vampirism or Spell.FlameDisruptor or Spell.SoulFireBall or
        Spell.StraightShot or Spell.DoubleShot or Spell.NapalmShot or Spell.VampireShot);

    public static int DevelopmentCount(UserItem item, IEnumerable<UserItem?> inventory, IReadOnlyList<UserItem?> equipment,
        MirClass role, MirGender gender, int level, ISet<Spell> learned)
    {
        if (!CompanionSupplyRules.Usable(item.Info, role, gender, level) || item.Count == 0) return 0;
        var carried = inventory.Concat(equipment).Where(i => i != null).Select(i => i!);
        if (item.Info.Type == ItemType.Book)
            return item.Info.Shape > 0 && !learned.Contains((Spell)item.Info.Shape) && !carried.Any(i => i.ItemIndex == item.ItemIndex) ? 1 : 0;
        if (item.Info.Type == ItemType.Amulet)
            return item.Info.Shape == 0 && learned.Contains(Spell.SoulFireBall)
                ? Math.Max(0, 50 - carried.Where(i => i.Info.Type == ItemType.Amulet && i.Info.Shape == 0).Sum(i => (int)i.Count)) : 0;
        return item.CurrentDura > 0 && !carried.Any(i => i.ItemIndex == item.ItemIndex) &&
            UpgradeSlot(item, equipment, role, Caster(learned)) >= 0 ? 1 : 0;
    }

    public static int EquipmentValue(UserItem item, MirClass role, bool caster)
    {
        if (MagicBloodStone.IsStone(item.Info)) return MagicBloodStone.Remaining(item);
        var attack = caster && role is MirClass.Wizard or MirClass.Archer ? Stat.MaxMC : caster && role == MirClass.Taoist ? Stat.MaxSC : Stat.MaxDC;
        return item.Info.Stats[attack] * 5 + item.AddedStats[attack] * 5 + item.Info.Stats[Stat.MaxAC] * 2 + item.Info.Stats[Stat.MaxMAC] * 2 + item.Info.Stats[Stat.HP] / 5;
    }

    public static UserItem[] StorageUnused(UserItem?[] inventory, UserItem?[] equipment, ISet<int> questItems,
        MirClass role, MirGender gender, int level, ISet<Spell> learned, bool caster)
    {
        var all = inventory.Where(item => item?.Info != null && item.Count > 0).Select(item => item!).ToArray();
        bool Identity(UserItem item) => item.Info.RequiredClass.HasFlag((RequiredClass)(1 << (int)role)) && item.Info.RequiredGender.HasFlag((RequiredGender)(1 << (int)gender));
        bool Usable(UserItem item) => CompanionSupplyRules.Usable(item.Info, role, gender, level) && item.CurrentDura > 0;
        int Value(UserItem item) => EquipmentValue(item, role, caster);
        var reserves = all.Where(item => item.Info.IsEquipment && Usable(item)).GroupBy(item => item.Info.Type)
            .SelectMany(group => group.OrderByDescending(Value).ThenByDescending(item => item.CurrentDura)
                .Take(group.Key is ItemType.Ring or ItemType.Bracelet ? 2 : 1)).Select(item => item.UniqueID).ToHashSet();
        var medicine = new HashSet<ulong>();
        foreach (var stat in new[] { Stat.HP, Stat.MP })
        {
            var left = stat == Stat.HP ? 20 : 10;
            foreach (var item in all.Where(item => CompanionSupplyRules.IsPotion(item, stat) && CompanionSupplyRules.Usable(item.Info, role, gender, level))
                .OrderByDescending(item => item.Info.Stats[stat]))
            { if (left <= 0) break; medicine.Add(item.UniqueID); left -= item.Count; }
        }
        return all.Where(item => !questItems.Contains(item.ItemIndex) && !item.Info.Bind.HasFlag(BindMode.DontStore) &&
            item.RentalInformation == null && item.ExpireInfo == null && item.SealedInfo == null && item.Count <= item.Info.StackSize &&
            (item.Info.Type is ItemType.Ore or ItemType.Meat ||
             (CompanionSupplyRules.IsPotion(item, Stat.HP) || CompanionSupplyRules.IsPotion(item, Stat.MP)) && !medicine.Contains(item.UniqueID) ||
             item.Info.Type == ItemType.Book && (learned.Contains((Spell)item.Info.Shape) || !Identity(item)) ||
             item.Info.IsEquipment && item.Info.Type is not (ItemType.Amulet or ItemType.Torch) &&
                item.AddedStats.Count == 0 && item.MaxDura == item.Info.Durability && !item.GMMade && !item.Cursed &&
                item.SoulBoundId < 0 && item.WeddingRing < 0 && item.GemCount == 0 && item.RefinedValue == RefinedValue.None &&
                item.RefineAdded == 0 && item.Awake.GetAwakeLevel() == 0 && item.Slots.Length == 0 &&
                (!Identity(item) || item.Info.RequiredType == RequiredType.Level && item.Info.RequiredAmount <= level &&
                 !reserves.Contains(item.UniqueID) && equipment.Where(worn => worn?.Info.Type == item.Info.Type).ToArray() is { Length: > 0 } worn &&
                 worn.Length >= (item.Info.Type is ItemType.Ring or ItemType.Bracelet ? 2 : 1) && worn.All(w => Value(item) <= Value(w!)))))
            .OrderByDescending(item => item.Weight).ToArray();
    }

    public static bool NeedsSpace(decimal weight, int limit, int emptySlots) => emptySlots <= 2 || limit > 0 && weight * 5 >= limit * 4L;

    public static bool CanCollect(decimal weight, int limit, int emptySlots, decimal addedWeight, bool needed, bool gold) =>
        gold || weight >= 0 && addedWeight >= 0 && limit > 0 && weight + addedWeight <= limit &&
        (needed || emptySlots > 2 && (weight + addedWeight) * 5 <= limit * 4L);

    public static bool CanHarvest(decimal weight, int limit, int emptySlots, bool needed) =>
        weight >= 0 && limit > 0 && emptySlots > 0 && (needed || !NeedsSpace(weight, limit, emptySlots));

    public static bool PlainWeapon(UserItem? item, ISet<int> protectedItems, int level) =>
        item?.Info?.Type == ItemType.Weapon && item.Count == 1 && PlainItem(item, protectedItems, level);

    public static bool PlainMeat(UserItem? item, ISet<int> protectedItems, int level) =>
        item?.Info?.Type == ItemType.Meat && item.Count <= item.Info.StackSize && PlainItem(item, protectedItems, level);

    public static bool StorageMaterial(UserItem? item, ISet<int> questItems, int level) =>
        item?.Info?.Type is ItemType.CraftingMaterial or ItemType.Food && item.Count <= item.Info.StackSize &&
        item.Info.Weight > 0 && PlainItem(item, questItems, level);

    public static UserItem[] StorageSurplus(IEnumerable<UserItem?> inventory,ISet<int> questItems,int level,int freeStorage)
    {
        if(freeStorage<=0)return [];
        var all=inventory.ToArray();
        var items=all.Where(item=>StorageMaterial(item,questItems,level)).Select(item=>item!).ToArray();
        var totals=items.GroupBy(item=>item.ItemIndex).ToDictionary(group=>group.Key,group=>group.Sum(item=>(int)item.Count));
        // 整叠暂存后还需拆回5份时，必须同时预留一个原仓库拆分格。
        return items.Where(item=>totals[item.ItemIndex]-item.Count>=5||item.Count>5&&freeStorage>=2)
            .Concat(StorageEquipment(all,questItems,level)).OrderByDescending(item=>item.Weight).ToArray();
    }

    private static IEnumerable<UserItem> StorageEquipment(IEnumerable<UserItem?> inventory,ISet<int> questItems,int level) =>
        inventory.Where(item=>item?.Info?.Type is ItemType.Armour or ItemType.Helmet or ItemType.Torch or ItemType.Necklace or
                ItemType.Bracelet or ItemType.Ring or ItemType.Belt or ItemType.Boots or ItemType.Stone && item.Count==1 &&
                item.MaxDura==item.Info.Durability && item.CurrentDura<=item.MaxDura && PlainItem(item,questItems,level))
            .Select(item=>item!).GroupBy(item=>item.ItemIndex)
            // 同定义普通品属性相同，只暂存多出的备装；双槽位仍留两件，特殊品不参与抵扣储备。
            .SelectMany(group=>group.OrderByDescending(item=>item.CurrentDura).Skip(group.First().Info.Type is ItemType.Ring or ItemType.Bracelet?2:1));

    private static bool PlainItem(UserItem? item, ISet<int> protectedItems, int level) =>
        item?.Info is { } info && info.RequiredType == RequiredType.Level && info.RequiredAmount <= level &&
        info.Grade <= ItemGrade.Common && info.Set == ItemSet.None && info.Unique == SpecialItemMode.None &&
        info.Bind == BindMode.None && !info.CanMine && !info.IsFishingRod && !info.NeedIdentify && info.Slots == 0 &&
        item.Count > 0 && item.Price() > 0 && !protectedItems.Contains(item.ItemIndex) &&
        item.SoulBoundId < 0 && item.WeddingRing < 0 && !item.GMMade && !item.Cursed &&
        item.RentalInformation == null && item.ExpireInfo == null && item.SealedInfo == null &&
        item.AddedStats.Count == 0 && item.GemCount == 0 && item.RefinedValue == RefinedValue.None && item.RefineAdded == 0 && item.RefineSuccessChance == 0 &&
        item.Awake.Type == AwakeType.None && item.Awake.GetAwakeLevel() == 0 && item.Slots.Length == 0;

    public static KnownSale[] SurplusMaterials(IEnumerable<UserItem?> inventory, ISet<int> protectedItems, int level)
    {
        var result = new List<KnownSale>();
        foreach (var group in inventory.Where(item => item?.Info?.Type is ItemType.CraftingMaterial or ItemType.Food)
            .Select(item => item!).Where(item => item.Weight > 0 && PlainItem(item, protectedItems, level)).GroupBy(item => item.ItemIndex))
        {
            var reserve = 5;
            foreach (var item in group)
            {
                var keep = Math.Min(reserve, item.Count); reserve -= keep;
                if (item.Count > keep) result.Add(new(item.UniqueID, (ushort)(item.Count - keep)));
            }
        }
        return result.ToArray();
    }

    public static UserItem[] SurplusArmour(IEnumerable<UserItem?> inventory, UserItem? equipped, ISet<int> protectedItems, int level)
    {
        if(equipped?.Info?.Type!=ItemType.Armour)return [];
        // 只出售同一定义的普通重复衣服，每种仍留耐久最好的一件，不推断不同属性之间的优劣。
        return inventory.Where(item=>item?.Info?.Type==ItemType.Armour&&item.UniqueID!=equipped.UniqueID&&item.Count==1&&
                item.MaxDura==item.Info.Durability&&PlainItem(item,protectedItems,level))
            .Select(item=>item!).GroupBy(item=>item.ItemIndex)
            .SelectMany(group=>group.OrderByDescending(item=>item.CurrentDura).Skip(1))
            .OrderByDescending(item=>item.Weight).ToArray();
    }

    public static UserItem[] SurplusWeapons(IEnumerable<UserItem?> inventory, UserItem? equipped,
        MirClass role, MirGender gender, int level, bool caster, ISet<int> protectedItems)
    {
        if (equipped?.Info?.Type != ItemType.Weapon) return [];
        var items = inventory.Where(item => item?.Info?.Type == ItemType.Weapon).Select(item => item!).ToArray();
        bool Usable(UserItem item) => item.CurrentDura > 0 && CompanionSupplyRules.Usable(item.Info, role, gender, level);
        var attack = caster && role is MirClass.Wizard or MirClass.Archer ? Stat.MaxMC : caster && role == MirClass.Taoist ? Stat.MaxSC : Stat.MaxDC;
        int Value(UserItem item) => item.Info.Stats[attack] + item.AddedStats[attack];
        // 备装也可能是任务/强化物；先在全部可用武器中保留，再筛选能出售的普通余货。
        var backup = items.Where(Usable).OrderByDescending(Value).ThenByDescending(item => item.CurrentDura).FirstOrDefault();
        return items.Where(item => item.UniqueID != equipped.UniqueID && item != backup &&
            PlainWeapon(item, protectedItems, level) && (!Usable(item) || Value(item) <= Value(equipped)))
            .OrderByDescending(item => item.Weight).ToArray();
    }
}
