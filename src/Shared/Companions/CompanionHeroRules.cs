#nullable enable
namespace Mir2.Companions;

public static class CompanionHeroRules
{
    public static bool Usable(UserItem item, MirClass role, MirGender gender, int level) =>
        !item.Info.Bind.HasFlag(BindMode.NoHero) && item.RentalInformation?.BindingFlags is null or BindMode.None &&
        CompanionSupplyRules.Usable(item.Info, role, gender, level);

    public static IEnumerable<int> ProtectedItems(IEnumerable<UserItem?> inventory, MirClass role, MirGender gender, int level,
        IReadOnlyList<UserItem?>? equipment, ISet<Spell>? learned)
    {
        foreach (var item in inventory)
        {
            if (item?.Info is not { } info || info.Bind.HasFlag(BindMode.NoHero) ||
                (info.RequiredClass & (RequiredClass)(1 << (int)role)) == 0 || (info.RequiredGender & (RequiredGender)(1 << (int)gender)) == 0) continue;
            if (info.Type == ItemType.Book && learned?.Contains((Spell)info.Shape) != true ||
                info.IsEquipment && (equipment == null || info.RequiredType == RequiredType.Level && info.RequiredAmount > level ||
                    CompanionInventoryRules.UpgradeSlot(item, equipment, role, learned != null && CompanionInventoryRules.Caster(learned)) >= 0)) yield return item.ItemIndex;
        }
    }
}
