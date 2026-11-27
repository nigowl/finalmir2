using System.Text.Json;
using System.Text.Json.Serialization;
using Server.MirEnvir;

namespace Server.Helpers;

public static class ItemPriceRules
{
    public const string Snapshot = "settings/item-prices.json";

    public static ItemPrice Get(ItemInfo info) => info.TradingPrices ?? new ItemPrice {
        ItemIndex = info.Index, Buy = info.Price, Sell = info.Price / 2, Recycle = info.Price / 2 };

    public static void Load(Envir world)
    {
        if (!DatabaseStorage.Exists(Snapshot)) return;
        using var stream = DatabaseStorage.OpenRead(Snapshot);
        var catalog = JsonSerializer.Deserialize<ItemPriceCatalog>(stream, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
        if (catalog?.Schema != 1 || catalog.Items == null) throw new InvalidDataException("不支持的物品价格格式，原数据已保留。");
        var ids = new HashSet<int>();
        foreach (var price in catalog.Items)
            if (price == null || !ids.Add(price.ItemIndex) || world.GetItemInfo(price.ItemIndex) == null)
                throw new InvalidDataException("物品价格含有重复或不存在的物品，原数据已保留。");
        foreach (var price in catalog.Items) world.GetItemInfo(price.ItemIndex).TradingPrices = price;
    }

    public static ItemPriceCatalog Current(Envir world) => new() {
        Items = world.ItemInfoList.Where(item => item.TradingPrices != null).Select(item => item.TradingPrices).OrderBy(item => item.ItemIndex).ToArray() };
}
