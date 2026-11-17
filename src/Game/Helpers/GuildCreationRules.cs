#nullable enable
using Server.MirObjects;

namespace Server.Helpers;

public static class GuildCreationRules
{
    // 接待NPC、实际建会和陪玩预检共用原费用条件，预检不能扣款或创建行会。
    public static string? Failure(PlayerObject player, string name)
    {
        if (player.Info.Level < Settings.Guild_RequiredLevel)
            return GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.LevelNotEnoughCreateGuild, Settings.Guild_RequiredLevel);
        if (player.Info.AccountInfo.AdminAccount) return null;
        if (string.Equals(name, Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase))
            return GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.CannotCreateNewbieGuild);
        foreach (var required in Settings.Guild_CreationCostList)
        {
            if (required.Item == null)
            {
                if (player.Info.AccountInfo.Gold < required.Amount)
                    return GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.InsufficientGoldCreateGuild, required.Amount);
                continue;
            }
            var count = (ushort)Math.Min(required.Amount, ushort.MaxValue);
            foreach (var item in player.Info.Inventory.Where(item => item != null && item.Info == required.Item))
            {
                if (required.Item.Type == ItemType.Ore && item.CurrentDura / 1000 > required.Amount) { count = 0; break; }
                count = item.Count > count ? (ushort)0 : (ushort)(count - item.Count);
                if (count == 0) break;
            }
            if (count == 0) continue;
            return required.Amount == 1
                ? GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.RequiredToCreateGuild, required.Item.FriendlyName)
                : required.Item.Type == ItemType.Ore
                    ? GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.ItemPurityRequiredForGuildCreation, required.Item.FriendlyName, required.Amount / 1000)
                    : GameLanguage.ServerTextMap.GetLocalization(ServerTextKeys.InsufficientNeedCreateGuild, required.Item.FriendlyName, required.Amount);
        }
        return null;
    }
}
