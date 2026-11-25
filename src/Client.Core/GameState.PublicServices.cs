using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    public S.PublicShop? PublicShop { get; private set; }
    public S.PublicShopTabs? PublicShopTabs { get; private set; }
    public readonly Dictionary<int, S.QuestNavigation> QuestNavigation = new();
    public int PublicShopRevision { get; private set; }

    private bool ApplyPublicServices(Packet packet)
    {
        switch (packet)
        {
            case S.PublicShop shop: PublicShop = shop; PublicShopRevision++; return true;
            case S.PublicShopTabs tabs: PublicShopTabs = tabs; PublicShopRevision++; return true;
            case S.QuestNavigation navigation: QuestNavigation[navigation.QuestIndex] = navigation; return true;
            case S.PublicShopResult result:
                if (result.Success) NotifyItemMessage(result.Message, ChatType.System2);
                else NotifyMessage(result.Message, ChatType.Hint);
                return true;
            case S.RecycleItemResult result:
                if (result.Success) NotifyItemMessage(result.Message, ChatType.System2);
                else NotifyMessage(result.Message, ChatType.Hint);
                return true;
            case S.UserInformation or S.LogOutSuccess or S.ReturnToLogin or S.Disconnect:
                PublicShop = null; PublicShopTabs = null; PublicShopRevision++; QuestNavigation.Clear(); break;
            case S.MapChanged: QuestNavigation.Clear(); break;
        }
        return false;
    }
}
