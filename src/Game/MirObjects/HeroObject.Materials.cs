using Server.MirEnvir;

namespace Server.MirObjects;

public partial class HeroObject
{
    private UserItem SharedMaterial(int count, Func<short, bool> shape)
    {
        if (Dead || Owner == null || Owner.Dead || Owner.Hero != this || CurrentMap == null || CurrentMap != Owner.CurrentMap ||
            Owner.TradePartner != null || Owner.ItemRentalPartner != null || Owner.Fishing) return null;
        return Owner.Info.Inventory.FirstOrDefault(item => item?.Info is { Type: ItemType.Amulet } info && item.Count >= count && shape(info.Shape) &&
            !info.Bind.HasFlag(BindMode.NoHero) && (item.SoulBoundId < 0 || item.SoulBoundId == Owner.Info.Index) && item.SealedInfo == null &&
            (item.ExpireInfo == null || item.ExpireInfo.ExpiryDate > Envir.Now) &&
            (item.RentalInformation == null || item.RentalInformation.BindingFlags == BindMode.None && item.RentalInformation.ExpiryDate > Envir.Now) &&
            CanEquipItem(item, (int)EquipmentSlot.Amulet, checkWeight: false));
    }

    protected override UserItem GetAmulet(int count, int shape = 0) =>
        base.GetAmulet(count, shape) ?? SharedMaterial(count, value => value == shape);

    protected override UserItem GetPoison(int count, byte shape = 0)
    {
        if (shape != 0) return base.GetPoison(count, shape) ?? SharedMaterial(count, value => value == shape);
        if (Target != null)
            foreach (byte colour in new byte[] { 2, 1 })
                if (!Target.PoisonList.Any(poison => poison.PType.HasFlag(colour == 2 ? PoisonType.Red : PoisonType.Green)) &&
                    GetPoison(count, colour) is { } missing) return missing;
        return base.GetPoison(count) ?? SharedMaterial(count, value => value is 1 or 2);
    }

    protected override void ConsumeItem(UserItem item, byte cost)
    {
        var index = Array.FindIndex(Owner.Info.Inventory, value => ReferenceEquals(value, item));
        base.ConsumeItem(item, cost);
        if (index < 0) return;
        // 原消耗只清自身背包；共用耗材归主人，末次必须清原格并刷新主人负重。
        if (item.Count == 0) Owner.Info.Inventory[index] = null;
        Owner.RefreshBagWeight();
    }
}
