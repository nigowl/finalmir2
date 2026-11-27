namespace Server.MirObjects;

public partial class PlayerObject
{
    public void AppraiseEquipment(ClientPackets.AppraiseEquipment request)
    {
        void Reply(bool success, string message) => Enqueue(new ServerPackets.EquipmentAppraisalResult { UniqueID = request.ItemID, Success = success, Message = message });
        var scroll = Info.Inventory.FirstOrDefault(item => item?.UniqueID == request.ScrollID);
        var target = Info.Inventory.FirstOrDefault(item => item?.UniqueID == request.ItemID);
        if (Dead || CurrentMap == null || TradePartner != null || ItemRentalPartner != null || Fishing ||
            !EquipmentAppraisal.IsScroll(scroll?.Info) || scroll.Count < 1 || scroll.RentalInformation != null || scroll.SealedInfo != null ||
            scroll.ExpireInfo?.ExpiryDate <= Server.MirEnvir.Envir.Main.Now || scroll.SoulBoundId >= 0 && scroll.SoulBoundId != Info.Index || !CanUseItem(scroll))
        { Reply(false, "当前不能使用鉴定卷轴。未消耗卷轴。"); return; }
        if (!EquipmentAppraiser.Identify(this, target, out var error)) { Reply(false, error); return; }
        ConsumeItem(scroll, 1); RefreshBagWeight();
        Enqueue(new ServerPackets.RefreshItem { Item = target });
        var message = $"鉴定成功：{target.Info.FriendlyName} · {EquipmentAppraisal.Name(target)}。";
        Reply(true, message); ReceiveChat(message, ChatType.System);
    }
}
