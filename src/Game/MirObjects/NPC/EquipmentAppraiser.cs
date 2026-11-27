using S = ServerPackets;

namespace Server.MirObjects;

public static class EquipmentAppraiser
{
    private const string SelectPrefix = "[@APPRAISESELECT_", ConfirmPrefix = "[@APPRAISECONFIRM_", QuoteKey = "EquipmentAppraisalQuote";
    private sealed record Quote(ulong Item, uint Cost, long Expires);

    public static bool TryCall(PlayerObject player, uint npc, string key)
    {
        var selecting = key.StartsWith(SelectPrefix, StringComparison.Ordinal);
        if (!selecting && !key.StartsWith(ConfirmPrefix, StringComparison.Ordinal)) return false;
        var prefix = selecting ? SelectPrefix : ConfirmPrefix;
        if (!key.EndsWith(']') || !ulong.TryParse(key[prefix.Length..^1], out var id)) return true;
        void Reply(bool success, string message) { if (!selecting) player.Enqueue(new S.EquipmentAppraisalResult { UniqueID = id, Success = success, Message = message }); }
        if (player.NPCObjectID != npc || player.NPCPage?.Key != EquipmentAppraisal.Page || player.Dead ||
            player.TradePartner != null || player.ItemRentalPartner != null || player.Fishing)
        { Reply(false, "当前状态不能鉴定，请重新与鉴定大师交谈。"); return true; }
        var item = player.Info.Inventory.FirstOrDefault(candidate => candidate?.UniqueID == id);
        if (!Allowed(player, item)) { Reply(false, "装备已变动或不能鉴定。"); Show(player, "装备已变动或不能鉴定，请重新选择。"); return true; }
        if (selecting)
        {
            var cost = EquipmentAppraisal.Price(item);
            player.NPCData[QuoteKey] = new Quote(id, cost, Server.MirEnvir.Envir.Main.Time + 30000);
            Page(player, [$"确定鉴定{item.Info.FriendlyName}？", $"费用：{cost:N0}金币。鉴定必定成功，结果固定且仅可鉴定一次。",
                "未鉴定时可正常穿戴，品质加成暂不生效。", $"<确认鉴定/@APPRAISECONFIRM_{id}>", "<取消/@APPRAISE>"]);
            return true;
        }
        if (!player.NPCData.Remove(QuoteKey, out var value) || value is not Quote quote || quote.Item != id ||
            quote.Expires < Server.MirEnvir.Envir.Main.Time || quote.Cost != EquipmentAppraisal.Price(item))
        { Reply(false, "鉴定确认已失效。"); Show(player, "鉴定确认已失效，请重新选择。"); return true; }
        if (player.Account.Gold < quote.Cost) { Reply(false, "金币不足，本次未鉴定。"); Show(player, "金币不足，本次未鉴定。"); return true; }
        if (!Identify(player, item, out var error))
        { Reply(false, error); Show(player, error); return true; }
        player.Account.Gold -= quote.Cost;
        player.Enqueue(new S.LoseGold { Gold = quote.Cost });
        player.Enqueue(new S.RefreshItem { Item = item });
        Reply(true, $"鉴定成功：{item.Info.FriendlyName} · {EquipmentAppraisal.Name(item)}。");
        player.ReceiveChat($"鉴定成功：{item.Info.FriendlyName} · {EquipmentAppraisal.Name(item)}，花费{quote.Cost:N0}金币。", ChatType.System);
        Show(player, $"鉴定成功：{item.Info.FriendlyName} · {EquipmentAppraisal.Name(item)}。");
        return true;
    }

    private static bool Allowed(PlayerObject player, UserItem item) => EquipmentAppraisal.Pending(item) && item.Count == 1 &&
        (item.SoulBoundId < 0 || item.SoulBoundId == player.Info.Index) && item.RentalInformation == null && item.SealedInfo == null &&
        (item.ExpireInfo == null || item.ExpireInfo.ExpiryDate > Server.MirEnvir.Envir.Main.Now);

    public static bool Identify(PlayerObject player, UserItem item, out string error)
    {
        error = "装备已变动或不能鉴定。";
        if (!Allowed(player, item)) return false;
        var bonuses = EquipmentAppraisal.Bonuses(item);
        if (bonuses.Values.Any(pair => (long)item.AddedStats[pair.Key] + pair.Value > int.MaxValue ||
            (long)item.Info.Stats[pair.Key] + item.AddedStats[pair.Key] + pair.Value > int.MaxValue))
        { error = "装备属性异常，未消耗物品或金币。"; return false; }
        item.AddedStats.Add(bonuses); item.Identified = true; error = "";
        return true;
    }

    public static void Show(PlayerObject player, string notice = "")
    {
        player.NPCData.Remove(QuoteKey);
        var lines = new List<string> { notice, "老夫玄鉴，专辨兵甲灵性。请从背包选择待鉴定装备。", "普通、良品、上品、精品、极品；已穿戴的装备请先卸下。" };
        lines.AddRange(player.Info.Inventory.Where(item => Allowed(player, item))
            .Select(item => $"<{item.Info.FriendlyName} · {EquipmentAppraisal.Price(item):N0}金币/@APPRAISESELECT_{item.UniqueID}>"));
        if (lines.Count == 3) lines.Add("背包中没有可鉴定的装备。");
        lines.Add("<返回/@MAIN>");
        Page(player, lines);
    }

    private static void Page(PlayerObject player, List<string> lines)
    {
        var page = new NPCPage(EquipmentAppraisal.Page);
        var buttons = lines.SelectMany(line => System.Text.RegularExpressions.Regex.Matches(line, @"<[^<>]+/(@[^<>/]+)>")
            .Select(match => "[" + match.Groups[1].Value.ToUpperInvariant() + "]")).ToList();
        var segment = new NPCSegment(page, lines, buttons, [], [], []);
        page.SegmentList.Add(segment); player.NPCPage = page;
        player.NPCSuccess.Clear(); player.NPCSuccess.Add(segment, true); player.NPCSpeech = lines;
        player.Enqueue(new S.NPCResponse { Page = lines });
    }
}
