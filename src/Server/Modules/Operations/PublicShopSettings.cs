using System.Text.Json;
using Mir2.Management;
using Server.Helpers;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed class PublicShopSettings(SqliteDatabaseStorage storage, AdminChanges changes)
{
    public CatalogRow[] Items() => Envir.Main.ItemInfoList.Where(item => PublicShopCatalogRules.CanList(item) &&
        PublicShopCatalogRules.SkillBookAllowed(item, Envir.Main))
        .OrderBy(item => item.Index).Select(item => new CatalogRow(item.Index, item.FriendlyName,
            $"{item.Type.ToLocalizedString()} · 基础买价 {ItemPriceRules.Get(item).Buy:N0} · {PublicShopRules.UnitText(item)}")).ToArray();

    public PublicShopDocument Read()
    {
        var catalog = PublicShopCatalogRules.Current(Envir.Main);
        var rate = (decimal)Settings.PublicShopPriceRate;
        return new(catalog, AdminChanges.Version(new { catalog, DefaultPriceRate = rate }), rate);
    }

    public PublicShopDocument Save(PublicShopEdit edit)
    {
        if (edit == null) throw new InvalidDataException("缺少商城配置。");
        AdminChanges.Match(Read().Version, edit.Version);
        PublicShopCatalogRules.Validate(edit.Catalog, Envir.Main);
        var previous = Envir.Main.PublicShopCatalog;
        changes.Commit(() =>
        {
            storage.Write(ItemPriceRules.Snapshot, JsonSerializer.SerializeToUtf8Bytes(ItemPriceRules.Current(Envir.Main)));
            storage.Write(PublicShopCatalogRules.Snapshot, JsonSerializer.SerializeToUtf8Bytes(edit.Catalog));
            Envir.Main.PublicShopCatalog = edit.Catalog;
        }, () => Envir.Main.PublicShopCatalog = previous);
        foreach (var player in Envir.Main.Players) player.InvalidatePublicShop();
        return Read();
    }
}

public static class PublicShopSettingsModule
{
    public static void MapPublicShopSettings(this RouteGroupBuilder group)
    {
        group.MapGet("/shop/prices", (GameRuntime runtime, ItemPriceSettings settings, CancellationToken token) => runtime.Invoke(settings.Read, token));
        group.MapPut("/shop/prices", (GameRuntime runtime, ItemPriceSettings settings, ItemPriceEdit edit, CancellationToken token) => runtime.Invoke(() => settings.Save(edit), token));
        group.MapGet("/shop", (GameRuntime runtime, PublicShopSettings settings, CancellationToken token) => runtime.Invoke(settings.Read, token));
        group.MapGet("/shop/items", (GameRuntime runtime, PublicShopSettings settings, CancellationToken token) => runtime.Invoke(settings.Items, token));
        group.MapPut("/shop", (GameRuntime runtime, PublicShopSettings settings, PublicShopEdit edit, CancellationToken token) => runtime.Invoke(() => settings.Save(edit), token));
    }
}
