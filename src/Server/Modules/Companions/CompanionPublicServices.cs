using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionPublicServices
{
    public static KnownGoal? Choose(PlayerObject player, CompanionProfile profile, ISet<string>? blocked, bool development = false, bool hero = false, bool savingGuild = false)
    {
        var key = hero ? "public-hero-develop" : development ? "public-develop" : "public-supply";
        if (blocked?.Contains(key) == true) return null;
        if (hero && (!player.HeroSpawned || player.Hero.Dead)) return null;
        var subject = hero ? player.Hero : (HumanObject)player;
        var choice = CompanionPublicShopRules.Choose(player.PublicShopOffers().Where(offer => Envir.Main.GetItemInfo(offer.ItemIndex) is { } info &&
                (!hero || !info.Bind.HasFlag(BindMode.NoHero)) && (!savingGuild || !MagicBloodStone.IsStone(info)) &&
                (info.Type != ItemType.Book || Envir.Main.MagicInfoList.Any(m => (int)m.Spell == info.Shape && m.Level1 <= subject.Level))), Envir.Main.GetItemInfo,
            player.Info.Inventory, subject.Info.Equipment, subject.Class, subject.Gender, subject.Level,
            subject.Info.Magics.Select(m => m.Spell).ToHashSet(), player.Account.Gold,
            player.Stats[Stat.BagWeight] - player.CurrentBagWeight, profile.Traits.Frugality, !player.CurrentMap.Info.NoTownTeleport, development);
        return choice == null ? null : new("service", player.CurrentMap.Info.Index, player.CurrentLocation.X, player.CurrentLocation.Y,
            development ? "公共商店原地购买职业书籍或护符" : "公共商店原地补充药品及回城卷", Key: key, PublicItemId: choice.Value.Offer.ItemIndex);
    }
}
