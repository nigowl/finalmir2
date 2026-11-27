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

    public static bool SkillBookAllowed(ItemInfo item, Envir world) => item != null && (item.Type != ItemType.Book ||
        item.Shape is > 0 and <= byte.MaxValue && (item.RequiredType != RequiredType.Level || item.RequiredAmount < 35) &&
        world.MagicInfoList.Any(magic => (int)magic.Spell == item.Shape && magic.Level1 < 35));

    public static void Validate(PublicShopCatalog catalog, Envir world, bool loading = false, decimal? defaultPriceRate = null)
    {
        if (catalog?.Schema != 3) throw new InvalidDataException("不支持的商城配置格式，原数据已保留。");
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
                if (!loading && !SkillBookAllowed(world.GetItemInfo(product.ItemIndex), world))
                    throw new InvalidDataException("商城技能书仅允许学习等级低于35级的书籍。");
                if (product.PriceRate <= 0 || product.PriceRate > uint.MaxValue) throw new InvalidDataException("商品倍率须大于0，且不超过4294967295。 ");
                if (ItemPriceRules.Get(world.GetItemInfo(product.ItemIndex)).Buy == 0) throw new InvalidDataException("此商品买价为0，请先在物品价格中设置买价。");
                if (Math.Ceiling(UnitPrice(world.GetItemInfo(product.ItemIndex)) * (product.PriceRate ?? defaultPriceRate ?? (decimal)Settings.PublicShopPriceRate)) > uint.MaxValue)
                    throw new InvalidDataException("商品买价乘倍率超出金币范围，请调低价格或倍率。");
            }
        }
        if (items.Count > 512) throw new InvalidDataException("商城最多512种商品。");
    }

    public static PublicShopCatalog Load(Envir world)
    {
        if (!DatabaseStorage.Exists(Snapshot)) return null;
        using var stream = DatabaseStorage.OpenRead(Snapshot);
        using var json = JsonDocument.Parse(stream);
        var schema = json.RootElement.TryGetProperty("Schema", out var version) ? version.GetInt32() : 1;
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        PublicShopCatalog catalog;
        if (schema == 1)
        {
            var legacy = json.Deserialize<LegacyCatalog>(options);
            if (legacy?.Tabs == null || legacy.Tabs.Any(tab => tab == null || tab.Products == null || tab.Products.Any(product => product == null))) throw new InvalidDataException("商城配置无效，原数据已保留。");
            catalog = new() { Tabs = legacy.Tabs.Select(tab => new PublicShopTab { Id = tab.Id, Name = tab.Name,
                Products = tab.Products?.Select(product => {
                    var info = world.GetItemInfo(product.ItemIndex) ?? throw new InvalidDataException("商城物品不存在，原数据已保留。");
                    // 旧绝对价商品可能没有定义买价，使用1作为转换基数，卖价和回收价保持0。
                    if (product.Price > 0 && ItemPriceRules.Get(info).Buy == 0)
                        info.TradingPrices = ItemPriceRules.Get(info) with { Buy = 1 };
                    return new PublicShopProduct { ItemIndex = product.ItemIndex, PriceRate = product.Price > 0 ?
                        Ratio(product.Price, UnitPrice(info)) : DefaultRate(info) };
                }).ToArray() }).ToArray() };
        }
        else if (schema == 2)
        {
            var legacy = json.Deserialize<RateCatalog>(options);
            if (legacy?.Tabs == null || legacy.Tabs.Any(tab => tab == null || tab.Products == null || tab.Products.Any(product => product == null)))
                throw new InvalidDataException("商城配置无效，原数据已保留。");
            catalog = new() { Tabs = legacy.Tabs.Select(tab => new PublicShopTab { Id = tab.Id, Name = tab.Name,
                Products = tab.Products.Select(product => new PublicShopProduct { ItemIndex = product.ItemIndex, PriceRate = product.PriceRate }).ToArray() }).ToArray() };
        }
        else if (schema == 3) catalog = json.Deserialize<PublicShopCatalog>(options);
        else throw new InvalidDataException("不支持的商城配置格式，原数据已保留。");
        // 旧货架可读取，但不再展示或出售受限书；不能因旧商品使整服无法启动。
        Validate(catalog, world, loading: true); return catalog;
    }

    public static PublicShopCatalog Current(Envir world)
    {
        if (world.PublicShopCatalog is { } saved) return saved with { Tabs = saved.Tabs.Select(tab => tab with {
            Products = tab.Products.Where(product => SkillBookAllowed(world.GetItemInfo(product.ItemIndex), world)).ToArray() }).ToArray() };
        var books = PlayerObject.PublicShopGoods().Select(pair => pair.Goods.Info.Index).ToHashSet();
        return new PublicShopCatalog { Tabs = PublicShopRules.DefaultTabNames.Select((name, category) => new PublicShopTab { Id = (byte)category, Name = name,
            Products = world.ItemInfoList.Where(item => PublicShopRules.Category(item) == category && ItemPriceRules.Get(item).Buy > 0 && item.StackSize > 0 &&
                SkillBookAllowed(item, world) && (item.Type != ItemType.Book || books.Contains(item.Index)))
                .OrderBy(item => item.Index).Select(item => new PublicShopProduct { ItemIndex = item.Index, PriceRate = DefaultRate(item) }).ToArray() }).ToArray() };
    }
    public static uint UnitPrice(ItemInfo info) => new UserItem(info) { Count = PublicShopRules.UnitCount(info),
        CurrentDura = info.Durability, MaxDura = info.Durability }.BuyPrice();

    // 向下保留12位，避免旧整数报价除法向上舍入后多收1金币。
    private static decimal Ratio(decimal price, uint basis) => basis == 0 ? 1m : Math.Floor(price / basis * 1_000_000_000_000m) / 1_000_000_000_000m;

    private static decimal? DefaultRate(ItemInfo info) => PublicShopRules.IsGuildHorn(info)
        ? Ratio(1_000_000m, UnitPrice(info)) : null;

    private sealed record RateCatalog
    {
        public int Schema { get; set; } = 2;
        public RateTab[] Tabs { get; set; } = [];
    }
    private sealed record RateTab
    {
        public byte Id { get; set; }
        public string Name { get; set; } = "";
        public RateProduct[] Products { get; set; } = [];
    }
    private sealed record RateProduct
    {
        public int ItemIndex { get; set; }
        public decimal PriceRate { get; set; } = 1m;
    }

    private sealed record LegacyCatalog
    {
        public int Schema { get; set; } = 1;
        public LegacyTab[] Tabs { get; set; } = [];
    }
    private sealed record LegacyTab
    {
        public byte Id { get; set; }
        public string Name { get; set; } = "";
        public LegacyProduct[] Products { get; set; } = [];
    }
    private sealed record LegacyProduct
    {
        public int ItemIndex { get; set; }
        public uint Price { get; set; }
    }

}
