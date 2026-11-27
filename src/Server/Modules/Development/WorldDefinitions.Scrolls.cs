using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Server.Helpers;
using Server.MirDatabase;

namespace Server.ConsoleHost;

public sealed record AppraisalScrollSetup(int Added, int Renamed, int Files, int ItemIndex, string Backup = "");

public sealed partial class WorldDefinitions
{
    public AppraisalScrollSetup PrepareAppraisalScroll(SqliteDatabaseStorage storage)
    {
        var existing = World.ItemInfoList.Where(item => EquipmentAppraisal.IsScroll(item) || item.Name == "装备鉴定卷轴").ToArray();
        if (existing.Length > 1 || existing.Any(item => !EquipmentAppraisal.IsScroll(item) || item.Name != "装备鉴定卷轴"))
            throw new InvalidDataException("鉴定卷轴定义冲突，未覆盖。");
        var renamed = World.ItemInfoList.Where(item => item.Type == ItemType.Scroll && ScrollNames.Normalize(item.Name) != item.Name)
            .ToDictionary(item => item, item => item.Name);
        var replacements = renamed.Values.Distinct().ToDictionary(name => name, ScrollNames.Normalize);
        var duplicates = World.ItemInfoList.GroupBy(item => item.Type == ItemType.Scroll ? ScrollNames.Normalize(item.Name) : item.Name)
            .Any(group => group.Select(item => item.Name).Distinct().Count() > 1);
        if (duplicates) throw new InvalidDataException("卷轴新名称与现有定义冲突，未修改。");
        var scroll = existing.SingleOrDefault();
        if (scroll == null)
        {
            var model = World.ItemInfoList.First(item => item.Type == ItemType.Scroll && item.Shape == 1);
            scroll = new ItemInfo { Index = Next(World.ItemIndex, World.ItemInfoList.Select(item => item.Index)), Name = "装备鉴定卷轴", Type = ItemType.Scroll,
                Shape = EquipmentAppraisal.ScrollShape, Image = model.Image, Weight = model.Weight, StackSize = 999, Price = EquipmentAppraisal.ScrollPrice,
                RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female,
                ToolTip = "双击后选择未鉴定装备，确认后消耗1张卷轴。" };
        }
        var catalogBefore = World.PublicShopCatalog;
        var catalog = PublicShopCatalogRules.Current(World);
        var listed = catalog.Tabs.SelectMany(tab => tab.Products).Any(product => product.ItemIndex == scroll.Index);
        var tabId = (catalog.Tabs.FirstOrDefault(tab => tab.Name.Contains('卷')) ?? catalog.Tabs.First()).Id;
        var updated = listed ? catalog : catalog with { Tabs = catalog.Tabs.Select(tab => tab.Id != tabId ? tab : tab with {
            Products = tab.Products.Append(new PublicShopProduct { ItemIndex = scroll.Index, PriceRate = 1m }).ToArray() }).ToArray() };
        var files = new Dictionary<string, (byte[] Before, byte[] After)>();
        if (replacements.Count > 0)
        {
            var pattern = string.Join("|", replacements.Keys.OrderByDescending(name => name.Length).Select(name => Regex.Escape(name) + (name.EndsWith('卷') ? "(?!轴)" : "")));
            foreach (var path in ChineseWorldTranslation.WorldFiles(paths.World).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".ini"))
            {
                var bytes = File.ReadAllBytes(path); var text = new UTF8Encoding(false, true).GetString(bytes);
                var value = Regex.Replace(text, pattern, match => replacements[match.Value]);
                if (value != text) files[path] = (bytes, Encoding.UTF8.GetBytes(value));
            }
        }
        if (existing.Length == 1 && renamed.Count == 0 && listed) return new(0, 0, 0, scroll.Index);
        World.SaveDB(); World.SaveAccounts();
        var backup = ChineseWorldTranslation.Backup(paths, storage, "scroll-quality");
        var counter = World.ItemIndex;
        try
        {
            storage.Transaction(() =>
            {
                foreach (var file in files) ChineseWorldTranslation.Replace(file.Key, file.Value.After);
                foreach (var item in renamed.Keys) item.Name = ScrollNames.Normalize(item.Name);
                if (existing.Length == 0) { World.ItemInfoList.Add(scroll); World.ItemIndex = scroll.Index; }
                World.PublicShopCatalog = updated;
                PublicShopCatalogRules.Validate(updated, World);
                storage.Write(PublicShopCatalogRules.Snapshot, JsonSerializer.SerializeToUtf8Bytes(updated)); World.SaveDB();
            });
        }
        catch
        {
            foreach (var file in files) ChineseWorldTranslation.Replace(file.Key, file.Value.Before);
            foreach (var item in renamed) item.Key.Name = item.Value;
            if (existing.Length == 0) World.ItemInfoList.Remove(scroll);
            World.ItemIndex = counter; World.PublicShopCatalog = catalogBefore; throw;
        }
        foreach (var player in World.Players) player.InvalidatePublicShop();
        return new(existing.Length == 0 ? 1 : 0, renamed.Count, files.Count, scroll.Index, backup);
    }
}
