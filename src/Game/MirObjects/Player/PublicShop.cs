using Server.MirEnvir;
using S = ServerPackets;

namespace Server.MirObjects;

public partial class PlayerObject
{
    private readonly Dictionary<int, PublicShopEntry> publicShopOffers = new();
    private S.PublicShopResult lastPublicPurchase;
    private long publicShopReadTime;

    internal static IEnumerable<(NPCObject Npc, UserItem Goods)> PublicShopGoods() => Envir.NPCs.Where(npc => npc.Visible && npc.CurrentMap != null)
            .Select(npc => (npc, script: NPCScript.Get(npc.ScriptID)))
            .Where(pair => pair.script.NPCPages.SelectMany(page => page.SegmentList).Any(segment =>
                segment.Buttons.Concat(segment.ElseButtons).Any(link => link.Equals(NPCScript.BuyKey, StringComparison.OrdinalIgnoreCase) ||
                    link.Equals(NPCScript.BuySellKey, StringComparison.OrdinalIgnoreCase) || link.Equals(NPCScript.BuyNewKey, StringComparison.OrdinalIgnoreCase) ||
                    link.Equals(NPCScript.BuySellNewKey, StringComparison.OrdinalIgnoreCase))))
            .SelectMany(pair => pair.script.Goods.Where(goods => goods.Info != null).Select(goods => (pair.npc, goods)));

    public IEnumerable<PublicShopEntry> PublicShopOffers()
    {
        return Helpers.PublicShopCatalogRules.Current(Envir).Tabs
            .SelectMany(tab => tab.Products.Select(product => (Info: Envir.GetItemInfo(product.ItemIndex), Category: tab.Id, product.PriceRate)))
            .Where(product => Helpers.PublicShopCatalogRules.CanList(product.Info) && Helpers.PublicShopCatalogRules.SkillBookAllowed(product.Info, Envir))
            .Select(product => {
                var price = Math.Ceiling(Helpers.PublicShopCatalogRules.UnitPrice(product.Info) * (product.PriceRate ?? (decimal)Settings.PublicShopPriceRate));
                return new PublicShopEntry { ItemIndex = product.Info.Index, Category = product.Category,
                    Price = price is > 0 and <= uint.MaxValue ? (uint)price : 0, MaxCount = 999 };
            }).Where(offer => offer.Price > 0).OrderBy(offer => offer.Category).ThenBy(offer => offer.ItemIndex).Take(512);
    }

    public void ReadPublicShop(bool layout = false)
    {
        if (layout) Enqueue(new S.PublicShopTabs { Tabs = Helpers.PublicShopCatalogRules.Current(Envir).Tabs.Select(tab => new PublicShopTab { Id = tab.Id, Name = tab.Name }).ToArray() });
        if (Envir.Time < publicShopReadTime) { Enqueue(new S.PublicShop { Items = publicShopOffers.Values.ToList() }); return; }
        publicShopReadTime = Envir.Time + 500;
        publicShopOffers.Clear();
        foreach (var offer in PublicShopOffers()) { publicShopOffers.Add(offer.ItemIndex, offer); Connection.CheckItemInfo(Envir.GetItemInfo(offer.ItemIndex)); }
        Enqueue(new S.PublicShop { Items = publicShopOffers.Values.ToList() });
    }

    public void InvalidatePublicShop() { publicShopReadTime = 0; publicShopOffers.Clear(); }

    public void BuyPublicShop(ClientPackets.PublicShopBuy request)
    {
        if (lastPublicPurchase != null && request.RequestId == lastPublicPurchase.RequestId) { Enqueue(lastPublicPurchase); return; }
        void Result(string message, bool success = false)
        {
            var result = new S.PublicShopResult { RequestId = request.RequestId, Success = success, Message = message };
            if (request.RequestId > 0 && (lastPublicPurchase == null || request.RequestId > lastPublicPurchase.RequestId)) lastPublicPurchase = result;
            Enqueue(result);
        }
        if (request.RequestId <= 0 || lastPublicPurchase != null && request.RequestId < lastPublicPurchase.RequestId)
        { Result("购买失败：请求已过期，请重新打开商城。"); return; }
        if (Dead || TradePartner != null)
        { Result("购买失败：阵亡或交易期间不能购买。"); return; }
        if (!publicShopOffers.TryGetValue(request.ItemIndex, out var offered) || request.Count == 0 || request.Count > offered.MaxCount)
        { Result("购买失败：商品或数量无效，请刷新商城。"); return; }
        var current = PublicShopOffers().FirstOrDefault(offer => offer.ItemIndex == request.ItemIndex);
        if (current == null || current.Price != offered.Price || current.Price != request.UnitPrice)
        { Result("购买失败：商品价格已变更，请重新打开商城确认。"); return; }
        var cost = (ulong)current.Price * request.Count;
        if (cost > uint.MaxValue || cost > Account.Gold) { Result("购买失败：金币不足或总价超出范围。"); return; }
        var info = Envir.GetItemInfo(request.ItemIndex);
        var items = new List<UserItem>(); var remaining = (int)request.Count * PublicShopRules.UnitCount(info);
        while (remaining > 0)
        {
            var item = new UserItem(info) { Count = (ushort)Math.Min(remaining, info.StackSize), CurrentDura = info.Durability, MaxDura = info.Durability };
            Envir.UpdateItemExpiry(item); items.Add(item); remaining -= item.Count;
            if (items.Count > Info.Inventory.Length + Info.QuestInventory.Length) break;
        }
        var material = Helpers.MaterialInventoryRules.Contains(info, Envir);
        var inventory = remaining == 0 ? Helpers.InventoryStacks.AfterAdding(material ? Info.QuestInventory : Info.Inventory, items) : null;
        if (inventory == null) { Result("购买失败：背包空间不足，且无法合堆。"); return; }
        // 毒符按整堆计重；合堆后未新增物品堆时不增加负重。
        var weight = material ? CurrentBagWeight : inventory.Sum(item => item?.Weight ?? 0);
        if (!material && weight > Stats[Stat.BagWeight])
        { Result($"购买失败：负重不足（购买后 {weight:0.##}/{Stats[Stat.BagWeight]}）。"); return; }
        Account.Gold -= (uint)cost; Enqueue(new S.LoseGold { Gold = (uint)cost });
        foreach (var item in items) { item.UniqueID = Envir.CreateFreshItem(info).UniqueID; GainItem(item); }
        Result($"购买成功：{info.FriendlyName} ×{request.Count}{(info.Type == ItemType.Amulet ? "包" : "")}，花费 {cost:N0} 金币。", true);
    }
}
