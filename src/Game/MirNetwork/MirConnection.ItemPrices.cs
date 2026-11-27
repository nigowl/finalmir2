using Server.Helpers;

namespace Server.MirNetwork;

public partial class MirConnection
{
    private bool itemPricesEnabled;

    private void EnableItemPrices()
    {
        if (itemPricesEnabled) return;
        itemPricesEnabled = true;
        foreach (var info in SentItemInfo) SendItemPrice(info);
    }

    public void SendItemPrice(ItemInfo info)
    {
        if (itemPricesEnabled) Enqueue(new ServerPackets.ItemPriceChanged { Prices = ItemPriceRules.Get(info), Configured = info.TradingPrices != null });
    }
}
