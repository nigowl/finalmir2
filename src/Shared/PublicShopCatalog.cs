public sealed record PublicShopCatalog
{
    public int Schema { get; set; } = 1;
    public PublicShopTab[] Tabs { get; set; } = [];
}

public sealed record PublicShopTab
{
    public byte Id { get; set; }
    public string Name { get; set; } = "";
    public PublicShopProduct[] Products { get; set; } = [];
}

public sealed record PublicShopProduct
{
    public int ItemIndex { get; set; }
    public uint Price { get; set; }
}
