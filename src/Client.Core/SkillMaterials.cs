using C = ClientPackets;

namespace Crystal.Native;

public readonly record struct SkillMaterial(short Shape, int Count)
{
    public string Name => Shape switch { 0 => "护身符", 1 => "绿毒粉", 2 => "红毒粉", 3 => "复活护符", _ => "毒粉" };
    public bool Matches(UserItem? item) => item?.Info is { Type: ItemType.Amulet } info && item.Count >= Count &&
        (Shape < 0 ? info.Shape is 1 or 2 : info.Shape == Shape);
}

public static class SkillMaterials
{
    public static SkillMaterial[] Required(GameState state, Spell spell, Actor? target) => spell switch
    {
        Spell.Poisoning => [new(PoisonShape(state, target), 1)],
        Spell.PoisonSword => [new(-1, 1)],
        Spell.SummonShinsu => [new(0, 5)],
        Spell.SummonHolyDeva => [new(0, 2)],
        Spell.PoisonCloud => [new(0, 5), new(1, 5)],
        Spell.Reincarnation => [new(3, 1)],
        Spell.SoulFireBall or Spell.SummonSkeleton or Spell.Hiding or Spell.MassHiding or Spell.SoulShield or Spell.BlessedArmour
            or Spell.TrapHexagon or Spell.UltimateEnhancer or Spell.Plague or Spell.Curse or Spell.Hallucination => [new(0, 1)],
        _ => []
    };

    private static short PoisonShape(GameState state, Actor? target)
    {
        var inventory = (state.User?.Equipment ?? []).Concat(state.User?.Inventory ?? []);
        if (target != null && !target.Poison.HasFlag(PoisonType.Red) && inventory.Any(new SkillMaterial(2, 1).Matches)) return 2;
        if (target != null && !target.Poison.HasFlag(PoisonType.Green) && inventory.Any(new SkillMaterial(1, 1).Matches)) return 1;
        if (target != null && !target.Poison.HasFlag(PoisonType.Red)) return 2;
        if (target != null && !target.Poison.HasFlag(PoisonType.Green)) return 1;
        return -1;
    }

    public static bool Equipped(GameState state, SkillMaterial material)
    {
        var equipment = state.User?.Equipment ?? [];
        if (material.Shape is 1 or 2)
        {
            var first = equipment.FirstOrDefault(item => new SkillMaterial(-1, material.Count).Matches(item));
            return material.Matches(first);
        }
        return equipment.Any(material.Matches);
    }

    public static string Missing(GameState state, SkillMaterial[] materials) => materials.FirstOrDefault(material =>
        !(state.User?.Equipment ?? []).Concat(state.User?.Inventory ?? []).Any(material.Matches)) is { Count: > 0 } missing
        ? $"需要至少 {missing.Count} 份{missing.Name}（同一叠）。" : "";

    public static Packet? NextChange(GameState state, SkillMaterial[] materials)
    {
        if (state.User == null) return null;
        var user = state.User;
        foreach (var material in materials)
        {
            if (Equipped(state, material)) continue;
            var item = user.Inventory.FirstOrDefault(material.Matches);
            var poisonSlot = material.Shape is 1 or 2 ? Array.FindIndex(user.Equipment, new SkillMaterial(-1, material.Count).Matches) : -1;
            if (item == null && poisonSlot >= 0)
            {
                var empty = Array.IndexOf(user.Inventory, null);
                if (empty >= 0) return new C.RemoveItem { Grid = MirGridType.Inventory, UniqueID = user.Equipment[poisonSlot].UniqueID, To = empty };
                return null;
            }
            if (item == null) return null;
            var slot = poisonSlot >= 0 ? poisonSlot : (int)EquipmentSlot.Amulet;
            if (materials.Any(other => other != material && other.Matches(user.Equipment.ElementAtOrDefault(slot)))) slot = (int)EquipmentSlot.BraceletR;
            return new C.EquipItem { Grid = MirGridType.Inventory, UniqueID = item.UniqueID, To = slot };
        }
        return null;
    }
}
