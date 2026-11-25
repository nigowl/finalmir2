using System.Text.Json;
using Mir2.Management;
using Server.Helpers;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed class PublicShopSettings(SqliteDatabaseStorage storage, AdminChanges changes)
{
    public CatalogRow[] Items() => Envir.Main.ItemInfoList.Where(item => PublicShopCatalogRules.CanList(item) &&
        (item.Type != ItemType.Book || Envir.Main.MagicInfoList.Any(m => (int)m.Spell == item.Shape)))
        .OrderBy(item => item.Index).Select(item => new CatalogRow(item.Index, item.FriendlyName,
            $"{item.Type.ToLocalizedString()} · 定义价格 {item.Price:N0} · {PublicShopRules.UnitText(item)}")).ToArray();

    public PublicShopDocument Read()
    {
        var catalog = PublicShopCatalogRules.Current(Envir.Main);
        return new(catalog, AdminChanges.Version(catalog));
    }

    public PublicShopDocument Save(PublicShopEdit edit)
    {
        if (edit == null) throw new InvalidDataException("缺少商城配置。");
        AdminChanges.Match(Read().Version, edit.Version);
        PublicShopCatalogRules.Validate(edit.Catalog, Envir.Main);
        var previous = Envir.Main.PublicShopCatalog;
        changes.Commit(() =>
        {
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
        group.MapGet("/shop", (GameRuntime runtime, PublicShopSettings settings, CancellationToken token) => runtime.Invoke(settings.Read, token));
        group.MapGet("/shop/items", (GameRuntime runtime, PublicShopSettings settings, CancellationToken token) => runtime.Invoke(settings.Items, token));
        group.MapPut("/shop", (GameRuntime runtime, PublicShopSettings settings, PublicShopEdit edit, CancellationToken token) => runtime.Invoke(() => settings.Save(edit), token));
    }
}
