namespace Mir2.Management;

public sealed record PublicShopDocument(PublicShopCatalog Catalog, string Version, decimal DefaultPriceRate);
public sealed record PublicShopEdit(PublicShopCatalog Catalog, string Version);
