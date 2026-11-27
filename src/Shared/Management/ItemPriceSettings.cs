namespace Mir2.Management;

public sealed record ItemPriceRow(ItemPrice Prices, string Name, ItemType Type, ushort Image, string Unit, string Version);
public sealed record ItemPriceEdit(ItemPrice Prices, string Version);
