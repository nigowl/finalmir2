using Server.MirDatabase;
using S = ServerPackets;

namespace Server.MirObjects;

public partial class PlayerObject
{
    public void RestoreHeroInventory(HeroInfo hero, bool notify)
    {
        var items = Helpers.HeroInventory.Restore(Info, hero);
        if (items.Length == 0) return;
        RefreshBagWeight();
        if (!notify) return;
        foreach (var item in items) CheckItem(item);
        Enqueue(new S.UserSlotsRefresh { Inventory = (UserItem[])Info.Inventory.Clone() });
        ReceiveChat("英雄物品已放入共用背包。", ChatType.Hint);
    }

    private bool HasHeroItemSession => HasHero && HeroSpawned && !Dead && TradePartner == null && ItemRentalPartner == null && !Fishing;
    private bool CanManageHeroItems => HasHeroItemSession && !Hero.Dead;
}
