using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

internal static class PublicServicesChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var type in new[] { ItemType.Weapon, ItemType.Book, ItemType.Script, ItemType.Quest, ItemType.Gem, ItemType.MonsterSpawn })
            check(PublicShopRules.Category(new ItemInfo { Type = type }) == -1, $"公共商店允许特殊商品：{type}");
        foreach (var shape in new short[] { 1, 2, 3, 4, 5 })
            check(PublicShopRules.Category(new ItemInfo { Type = ItemType.Potion, Shape = shape }) == -1, "公共商店出售特殊药品");
        check(PublicShopRules.Category(new ItemInfo { Type = ItemType.CraftingMaterial, Name = "沃玛角" }) == 3 &&
            PublicShopRules.Category(new ItemInfo { Type = ItemType.CraftingMaterial, Name = "其他材料" }) == -1 &&
            PublicShopRules.Category(new ItemInfo { Type = ItemType.Quest, Name = "沃玛号角" }) == -1,
            "公共商店没有单独上架建会材料沃玛角");
        check(PublicShopRules.Category(new ItemInfo { Type = ItemType.Scroll, Shape = 1 }) == 1 &&
            PublicShopRules.Category(new ItemInfo { Type = ItemType.Scroll, Shape = 2 }) == 1 &&
            PublicShopRules.Category(new ItemInfo { Type = ItemType.Scroll, Shape = 3 }) == -1, "公共商店卷轴范围错误");
        var offer = new PublicShopEntry { ItemIndex = 7, Price = 60, MaxCount = 99 };
        var state = new GameState();
        state.Apply(new S.UserInformation { ObjectID = 1, HP = 100, Gold = 200, Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        state.Apply(new S.PublicShop { Items = [offer] });
        var purchases = new ClientPublicShop();
        check(purchases.Buy(state, offer, 4, 1000) == null && !purchases.Pending, "金币不足仍发起公共采购");
        var request = purchases.Buy(state, offer, 2, 1000)!;
        check(request.Count == 2 && request.UnitPrice == 60 && purchases.Pending && state.User!.Gold == 200, "公共采购预先扣款或缺少报价");
        check(purchases.Buy(state, offer, 1, 1001) == null && purchases.TimedOut(6000), "公共采购重复发送或没有超时保护");
        purchases.Observe(new S.MapChanged()); purchases.Observe(new S.Death());
        check(purchases.Pending, "切图/死亡释放未知采购锁");
        purchases.Observe(new S.PublicShopResult { RequestId = request.RequestId + 1 });
        check(purchases.Pending, "其他采购回执释放锁");
        purchases.Observe(new S.PublicShopResult { RequestId = request.RequestId });
        check(!purchases.Pending && purchases.Buy(state, offer, 1, 6001)!.RequestId > request.RequestId, "真实回执没有释放采购锁或复用了编号");
        purchases.Observe(new S.Disconnect()); check(!purchases.Pending, "断线没有清理采购锁");
        var notices = new List<string>(); var chats = new List<string>(); state.ItemMessage += (text, _) => notices.Add(text); state.ChatMessage += (text, _) => chats.Add(text);
        state.Apply(new S.PublicShopResult { Success = true, Message = "购买成功" }); state.Apply(new S.PublicShopResult { Message = "金币不足" });
        check(notices.SequenceEqual(["购买成功"]) && chats.SequenceEqual(["金币不足"]), "公共商店消息分类错误");
        Packet.IsServer = true;
        var received = Packet.ReceivePacket(request.GetPacketBytes().ToArray(), out var extra) as C.PublicShopBuy;
        check(received?.RequestId == request.RequestId && received.ItemIndex == 7 && received.UnitPrice == 60 && received.Count == 2 && extra.Length == 0, "公共商店请求协议往返错误");
        Packet.IsServer = false;
        var navigation = new S.QuestNavigation { QuestIndex = 5, RequestId = 8, OriginMap = 1, Targets = [new() { Id = 0, Name = "铁匠", NpcId = 23,
            MapIndex = 2, MapTitle = "比奇", Location = new(297, 612), NextLocation = new(288, 610), Reachable = true }] };
        var route = Packet.ReceivePacket(navigation.GetPacketBytes().ToArray(), out _) as S.QuestNavigation;
        check(route?.Targets.Single().NpcId == 23 && route.Targets[0].Location.X == 297 && route.OriginMap == 1, "任务导航协议丢失实体/地图/坐标");
        state.Apply(navigation); state.Apply(new S.MapChanged()); check(state.QuestNavigation.Count == 0, "任务导航跨地图保留旧坐标");
        state.Apply(new S.LogOutSuccess()); check(state.PublicShop == null, "公共商店跨角色保留旧报价");
    }
}
