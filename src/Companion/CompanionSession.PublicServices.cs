using Crystal.Native;
using Mir2.Companions;
using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private readonly ClientPublicShop publicPurchases = new();
    private long publicCatalogAt, publicCatalogRequested;
    private int recycleSequence;
    private C.RecycleItem? recycleRequest;

    private async Task<bool> PublicServices(long now)
    {
        var goal = Knowledge?.Goal;
        if (goal?.Key is not ("public-supply" or "public-develop" or "public-hero-develop" or "public-recycle" or "public-guild-material")) return false;
        if (state.Trading || state.StorageActive || repairItem != 0) return false;
        var user = state.User!;
        if (goal.Key == "public-recycle")
        {
            // 执行端再次核对允许编号/数量和备装；绝不回退到任意肉类或玩家勾选清单。
            var item = goal.SellItems?.Length > 0 ? SaleItem() : null;
            if (item != null && goal.SellItems!.Any(sale => sale.UniqueId == item.UniqueID && sale.Count >= item.Count) &&
                ItemRecycling.Rejection(item).Length == 0)
            {
                recycleRequest = new() { RequestId = ++recycleSequence, UniqueID = item.UniqueID, Count = item.Count };
                await Status("回收中", $"原地回收{item.Info.FriendlyName} ×{item.Count}");
                await Asset(recycleRequest, "recycle", item.UniqueID);
            }
            else await report(new("goal-blocked", Profile.Id, Reason: "当前余货已变化或受到保护，重新规划", Text: goal.Key));
            return true;
        }
        if (state.PublicShop == null || now - publicCatalogAt > 30000)
        {
            if (publicCatalogRequested == 0)
            { publicCatalogRequested = now; await connection.Send(new C.PublicShopRequest()); return true; }
            else if (now - publicCatalogRequested >= 5000)
            { publicCatalogRequested = 0; await report(new("goal-blocked", Profile.Id, Reason: "公共商店目录读取超时", Text: goal.Key)); }
            return false;
        }
        var hero = goal.Key == "public-hero-develop";
        if (hero && (state.HeroState != HeroSpawnState.Summoned || state.Hero is not { HP: > 0 })) return false;
        var subject = hero ? state.Hero! : user;
        var choice = goal.Key == "public-guild-material" && !state.InGuild && Profile.Guilds.Mode is "create" or "auto"
            ? CompanionPublicShopRules.GuildMaterial(state.PublicShop.Items,id=>state.Items.GetValueOrDefault(id),goal.PublicItemId,goal.PublicCount,
                goal.GoldReserve,user.Gold,state.CalculateStats()[Stat.BagWeight]-state.InventoryWeight,
                user.Inventory.Concat(user.QuestInventory).Where(i=>i?.ItemIndex==goal.PublicItemId).Sum(i=>(int)i!.Count))
            : CompanionPublicShopRules.Choose(state.PublicShop.Items.Where(offer => offer.ItemIndex == goal.PublicItemId &&
                (Knowledge?.Guild?.Action != "prepare" || state.Items.GetValueOrDefault(offer.ItemIndex) is { } saving && !MagicBloodStone.IsStone(saving)) &&
                (!hero || state.Items.GetValueOrDefault(offer.ItemIndex) is { } info && !info.Bind.HasFlag(BindMode.NoHero))), id => state.Items.GetValueOrDefault(id),
            user.Inventory, subject.Equipment, subject.Class, subject.Gender, subject.Level, subject.Magics.Select(m => m.Spell).ToHashSet(),
            user.Gold, state.CalculateStats()[Stat.BagWeight] - state.InventoryWeight, Profile.Traits.Frugality,
            Knowledge?.NoTownTeleport == false, goal.Key != "public-supply");
        if (choice is { } selected && publicPurchases.Buy(state, selected.Offer, selected.Count, now) is { } request)
        {
            await Status("补给中", $"公共商店购买{state.Items[request.ItemIndex].FriendlyName} ×{request.Count}");
            await Asset(request, "public-buy", (ulong)request.RequestId);
        }
        else if (goal.Key == "public-guild-material")
            await report(new("goal-blocked",Profile.Id,Reason:"建会材料报价、预算、容量或身份已改变，重新核对",Text:goal.Key));
        return true;
    }

    private async Task<bool> PublicServiceResult(Packet packet)
    {
        bool success; string message;
        if (packet is S.PublicShopResult bought && pendingAsset == "public-buy" && (ulong)bought.RequestId == assetId)
        { success = bought.Success; message = bought.Message; if (!success) publicCatalogAt = 0; }
        else if (packet is S.RecycleItemResult recycled && pendingAsset == "recycle" && recycleRequest is { } request &&
            recycled.RequestId == request.RequestId && recycled.UniqueID == request.UniqueID && recycled.Count == request.Count)
        { success = recycled.Success; message = recycled.Message; if (!success) refused.Add(assetId); recycleRequest = null; }
        else return false;
        await report(new("asset", Profile.Id, Text: pendingAsset + ":" + (success ? "成功" : "拒绝"), Reason: message));
        if (!success) await report(new("goal-blocked", Profile.Id, Reason: message, Text: Knowledge?.Goal?.Key ?? ""));
        pendingAsset = null;
        return true;
    }
}
