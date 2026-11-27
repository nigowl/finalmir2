using System.Text.Json;
using Mir2.Management;
using Server.Helpers;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed class ItemPriceSettings(SqliteDatabaseStorage storage, AdminChanges changes)
{
    public ItemPriceRow[] Read() => Envir.Main.ItemInfoList.OrderBy(item => item.Index).Select(Row).ToArray();

    private static ItemPriceRow Row(ItemInfo item)
    {
        var prices = ItemPriceRules.Get(item);
        return new(prices, item.FriendlyName, item.Type, item.Image, PublicShopRules.UnitText(item), AdminChanges.Version(prices));
    }

    public ItemPriceRow Save(ItemPriceEdit edit)
    {
        if (edit?.Prices == null) throw new InvalidDataException("缺少物品价格。");
        var world = Envir.Main;
        var info = world.GetItemInfo(edit.Prices.ItemIndex) ?? throw new InvalidDataException("物品不存在。");
        AdminChanges.Match(Row(info).Version, edit.Version);
        var catalog = world.PublicShopCatalog ?? PublicShopCatalogRules.Current(world);
        if (edit.Prices.Buy == 0 && catalog.Tabs.SelectMany(tab => tab.Products).Any(product => product.ItemIndex == info.Index))
            throw new InvalidDataException("商城中仍有此商品，请先移除商品后再将买价设为0。");
        var previous = info.TradingPrices; var previousCatalog = world.PublicShopCatalog;
        changes.Commit(() => {
            info.TradingPrices = edit.Prices with { };
            PublicShopCatalogRules.Validate(catalog, world, loading: true);
            // 与旧商城倍率转换在同一事务保存，改买价后重启不能重新按旧绝对价换算。
            storage.Write(ItemPriceRules.Snapshot, JsonSerializer.SerializeToUtf8Bytes(ItemPriceRules.Current(world)));
            storage.Write(PublicShopCatalogRules.Snapshot, JsonSerializer.SerializeToUtf8Bytes(catalog));
            world.PublicShopCatalog = catalog;
        }, () => { info.TradingPrices = previous; world.PublicShopCatalog = previousCatalog; });
        foreach (var player in world.Players) {
            player.InvalidatePublicShop();
            player.Connection.SendItemPrice(info);
        }
        return Row(info);
    }
}
