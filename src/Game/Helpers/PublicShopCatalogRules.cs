using System.Text.Json;
using System.Text.Json.Serialization;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Helpers;

public static class PublicShopCatalogRules
{
    public const string Snapshot = "settings/public-shop.json";

    public static bool CanList(ItemInfo item) => item != null && item.StackSize > 0 && Enum.IsDefined(item.Type) && item.Type is not
        (ItemType.Nothing or ItemType.Quest or ItemType.Script or ItemType.MonsterSpawn or ItemType.SealedHero);

    public static void Validate(PublicShopCatalog catalog, Envir world)
    {
        if (catalog?.Schema != 1) throw new InvalidDataException("不支持的商城配置格式，原数据已保留。");
        if (catalog.Tabs == null || catalog.Tabs.Length is < 1 or > 8) throw new InvalidDataException("商城须保留1–8个选项卡。");
        var tabs = new HashSet<byte>(); var names = new HashSet<string>(StringComparer.Ordinal); var items = new HashSet<int>();
        foreach (var tab in catalog.Tabs)
        {
            if (tab == null || tab.Id > 31 || !tabs.Add(tab.Id) || string.IsNullOrWhiteSpace(tab.Name) || tab.Name.Length > 16 ||
                tab.Name != tab.Name.Trim() || tab.Name.Any(char.IsControl) || !names.Add(tab.Name)) throw new InvalidDataException("选项卡编号/名称重复或无效，名称最多16字。");
            if (tab.Products == null) throw new InvalidDataException("商品列表不能为空值。");
            foreach (var product in tab.Products)
            {
                if (product == null || !items.Add(product.ItemIndex) || !CanList(world.GetItemInfo(product.ItemIndex)))
                    throw new InvalidDataException("商品不存在、不适合商城出售或重复；同一商品只能放在一个选项卡。");
                if (world.GetItemInfo(product.ItemIndex).Type == ItemType.Book && !world.MagicInfoList.Any(m => (int)m.Spell == world.GetItemInfo(product.ItemIndex).Shape))
                    throw new InvalidDataException("技能书没有对应的有效技能。");
                if (product.Price == 0 && world.GetItemInfo(product.ItemIndex).Price == 0) throw new InvalidDataException("此商品没有默认价格，请设置大于0的单价。");
            }
        }
        if (items.Count > 512) throw new InvalidDataException("商城最多512种商品。");
    }

    public static PublicShopCatalog Load(Envir world)
    {
        if (!DatabaseStorage.Exists(Snapshot)) return null;
        using var stream = DatabaseStorage.OpenRead(Snapshot);
        var catalog = JsonSerializer.Deserialize<PublicShopCatalog>(stream, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
        Validate(catalog, world); return catalog;
    }

    public static PublicShopCatalog Current(Envir world)
    {
        if (world.PublicShopCatalog != null) return world.PublicShopCatalog;
        var books = PlayerObject.PublicShopGoods().Select(pair => pair.Goods.Info.Index).ToHashSet();
        return new PublicShopCatalog { Tabs = PublicShopRules.DefaultTabNames.Select((name, category) => new PublicShopTab { Id = (byte)category, Name = name,
            Products = world.ItemInfoList.Where(item => PublicShopRules.Category(item) == category && item.Price > 0 && item.StackSize > 0 &&
                (item.Type != ItemType.Book || books.Contains(item.Index) && world.MagicInfoList.Any(m => (int)m.Spell == item.Shape)))
                .OrderBy(item => item.Index).Select(item => new PublicShopProduct { ItemIndex = item.Index }).ToArray() }).ToArray() };
    }
}
