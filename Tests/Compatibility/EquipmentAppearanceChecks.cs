using Crystal.Native;
using Server.Helpers;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using C = ClientPackets;
using S = ServerPackets;

internal static class EquipmentAppearanceChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var weapon = new UserItem(new ItemInfo { Index = 1, Name = "品质刀", Type = ItemType.Weapon, Stats = new Stats { [Stat.MinDC] = 3, [Stat.MaxDC] = 5 } })
            { EquipmentQuality = EquipmentGrade.Supreme, CurrentDura = 4301, MaxDura = 10000, UniqueID = 1 };
        check(EquipmentAppearance.Glow([weapon]) == EquipmentGlow.None, "未鉴定极品泄露发光");
        weapon.Identified = true;
        check(EquipmentAppearance.Glow([weapon]) == EquipmentGlow.Weapon && ItemPresentation.Details(weapon).StartsWith("耐久 4/10"), "极品武器发光或整数耐久错误");
        var armour = new UserItem(new ItemInfo { Type = ItemType.Armour }) { EquipmentQuality = EquipmentGrade.Supreme, Identified = true };
        check(EquipmentAppearance.Glow([weapon, armour]) == (EquipmentGlow.Weapon | EquipmentGlow.Equipment), "极品装备组合丢失光效");
        weapon.CurrentDura = 0;
        check(EquipmentAppearance.Glow([weapon]) == EquipmentGlow.None, "损坏装备仍发光");
        weapon.CurrentDura = 4301; weapon.EquipmentQuality = EquipmentGrade.Excellent;
        check(EquipmentAppearance.Glow([weapon]) == EquipmentGlow.None, "非极品装备误发光");
        using (var bytes = new MemoryStream())
        {
            using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) weapon.Save(writer);
            bytes.Position = 0; using var reader = new BinaryReader(bytes);
            check(new UserItem(reader, Envir.Version, Envir.CustomVersion).CurrentDura == 4301, "整数显示破坏底层耐久精度");
        }
        var candidate = weapon.Clone(); candidate.AddedStats[Stat.MaxDC] = 2;
        var parts = ItemPresentation.ComparisonLines(candidate, weapon).Single();
        check(parts.Where(part => part.Change != 0).Select(part => part.Text).SequenceEqual(new[] { "+2" }) && parts.Any(part => part.Text == "0" && part.Change == 0), "对比把0或标题高亮");
        var state = new GameState(); state.Apply(new S.ObjectEquipmentGlow { ObjectID = 9, Glow = EquipmentGlow.Weapon });
        check(state.Actors.Count == 0, "光效包创建幽灵对象");
        state.Apply(new S.ObjectPlayer { ObjectID = 9 }); state.Apply(new S.ObjectEquipmentGlow { ObjectID = 9, Glow = EquipmentGlow.Weapon });
        state.Apply(new S.ObjectEquipmentGlow { ObjectID = 9, Glow = (EquipmentGlow)128 }); state.Apply(new S.ObjectPlayer { ObjectID = 9 });
        check(state.Actors[9].EquipmentGlow == EquipmentGlow.Weapon, "非法光效污染状态或外观刷新丢失光效");
        state.Apply(new S.ObjectRemove { ObjectID = 9 }); state.Apply(new S.ObjectPlayer { ObjectID = 9 });
        check(state.Actors[9].EquipmentGlow == EquipmentGlow.None, "新对象继承旧极品外观");
        ServerGlow(check, weapon); StackAndNames(check); Scroll(check); Dropped(check); ManualDropChecks.Run(check); Protocol(check);
    }

    private static void ServerGlow(Action<bool, string> check, UserItem weapon)
    {
        var map = new Map(new MapInfo()) { Width = 3, Height = 3, Cells = new Cell[3, 3] };
        for (var y = 0; y < 3; y++) for (var x = 0; x < 3; x++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        var viewer = new Player { Info = new CharacterInfo(), CurrentMap = map, CurrentLocation = new(1, 1) };
        var target = new Player { Info = new CharacterInfo(), CurrentMap = map, CurrentLocation = new(2, 1) };
        weapon.EquipmentQuality = EquipmentGrade.Supreme; target.Info.Equipment[0] = weapon;
        viewer.Node = new LinkedListNode<MapObject>(viewer); target.Node = new LinkedListNode<MapObject>(target);
        map.Cells[1, 1].Add(viewer); map.Cells[2, 1].Add(target);
        target.SendEquipmentGlow(viewer); check(viewer.Packets.Count == 0, "旧客户端未经请求收到新表现包");
        viewer.EnableClientPresentation();
        check(viewer.Packets.OfType<S.ObjectEquipmentGlow>().Any(packet => packet.ObjectID == target.ObjectID && packet.Glow == EquipmentGlow.Weapon), "能力请求未初始化视野内实际装备光效");
        viewer.Packets.Clear(); viewer.EnableClientPresentation(); check(viewer.Packets.Count == 0, "重复能力请求反复扫描全图");
        target.CurrentLocation = new(999, 999); target.SendEquipmentGlow(viewer); check(viewer.Packets.Count == 0, "泄露视野外装备外观");
        target.CurrentLocation = new(2, 1); target.CurrentMap = new Map(new MapInfo()); target.SendEquipmentGlow(viewer);
        check(viewer.Packets.Count == 0, "跨图装备外观泄露");
    }

    private static void StackAndNames(Action<bool, string> check)
    {
        foreach (var type in Enum.GetValues<ItemType>().Where(type => !new ItemInfo { Type = type }.IsEquipment && type is not (ItemType.Amulet or ItemType.Bait)))
            check(new ItemInfo { Type = type, StackSize = 20 }.StackSize == 999, $"{type}可堆叠道具未统一999");
        check(new ItemInfo { Type = ItemType.Weapon, StackSize = 999 }.StackSize == 1 && new ItemInfo { Type = ItemType.SealedHero }.StackSize == 1 &&
            new ItemInfo { Type = ItemType.Amulet, StackSize = 2000 }.StackSize == 2000, "装备、独立封印或符毒次数被错误合并");
        foreach (var type in new[] { ItemType.Potion, ItemType.Scroll })
        {
            var info = new ItemInfo { Index = 2, Type = type, StackSize = 20 };
            var a = new UserItem(info) { UniqueID = 1, Count = 600 }; var b = new UserItem(info) { UniqueID = 2, Count = 600 };
            var bound = new UserItem(info) { UniqueID = 3, Count = 5, SoulBoundId = 99 }; UserItem[] items = [a, b, bound];
            InventoryStacks.Compact(items);
            check(a.Count == 999 && b.Count == 201 && bound.Count == 5 && a.UniqueID == 1 && b.UniqueID == 2, "999整理溢出丢数量/编号或混合绑定");
            a.Count = 1200; b.Count = 0; items[1] = null!; InventoryStacks.Compact(items);
            check(a.Count == 1200, "旧超上限物品被截断");
        }
        check(ScrollNames.Normalize("随机传送卷") == "随机传送卷轴" && ScrollNames.Normalize("地牢逃脱卷（小）2") == "地牢逃脱卷轴（小）2" &&
            ScrollNames.Normalize("回城卷轴") == "回城卷轴" && ScrollNames.Normalize("修复油") == "修复油", "卷轴命名破坏规格或误改油类");
    }

    private static void Scroll(Action<bool, string> check)
    {
        var scroll = new UserItem(new ItemInfo { Type = ItemType.Scroll, Shape = EquipmentAppraisal.ScrollShape, RequiredClass = (RequiredClass)31,
            RequiredGender = RequiredGender.Male | RequiredGender.Female }) { UniqueID = 2, Count = 2 };
        var item = new UserItem(new ItemInfo { Type = ItemType.Weapon, Stats = new Stats { [Stat.MaxDC] = 5 } }) { UniqueID = 3, EquipmentQuality = EquipmentGrade.Supreme };
        var player = new Player { Info = new CharacterInfo { Index = 7, HP = 10 }, Account = new AccountInfo { Gold = 123 }, CurrentMap = new Map(new MapInfo()) };
        player.Info.Inventory[6] = scroll; player.Info.Inventory[7] = item;
        var request = new C.AppraiseEquipment { ScrollID = 2, ItemID = 3 };
        player.AppraiseEquipment(new C.AppraiseEquipment { ScrollID = 999, ItemID = 3 });
        scroll.ExpireInfo = new ExpireInfo { ExpiryDate = DateTime.MinValue }; player.AppraiseEquipment(request); scroll.ExpireInfo = null;
        player.TradePartner = new Player(); player.AppraiseEquipment(request); player.TradePartner = null;
        check(!item.Identified && scroll.Count == 2, "卷轴缺失/过期/交易仍鉴定或消耗");
        player.Dead = true; player.AppraiseEquipment(request); player.Dead = false;
        check(!item.Identified && scroll.Count == 2, "死亡鉴定消耗卷轴");
        item.SoulBoundId = 8; player.AppraiseEquipment(request); item.SoulBoundId = -1;
        check(!item.Identified && scroll.Count == 2, "他人绑定鉴定消耗卷轴");
        player.AppraiseEquipment(request);
        check(item.Identified && item.GetTotal(Stat.MaxDC) == 7 && scroll.Count == 1 && player.Account.Gold == 123 && player.Packets.OfType<S.EquipmentAppraisalResult>().Last().Success,
            "卷轴鉴定没有复用品质规则、重复扣金币或没有对应成功回执");
        player.AppraiseEquipment(request);
        check(scroll.Count == 1 && item.GetTotal(Stat.MaxDC) == 7 && !player.Packets.OfType<S.EquipmentAppraisalResult>().Last().Success, "重放卷轴重复消耗或叠加品质");
    }

    private static void Dropped(Action<bool, string> check)
    {
        var state = new GameState(); state.Apply(new S.UserInformation { ObjectID = 1, HP = 10, Inventory = new UserItem[86], Equipment = new UserItem[14], QuestInventory = [] });
        var prefs = new ClientPreferences { AutoPickup = true }; prefs.ItemRules["铁剑"] = ItemActions.Pickup;
        state.Actors[2] = new Actor { Id = 2, Kind = ObjectType.Item, Name = "铁剑", Location = new(1, 0) };
        state.Actors[3] = new Actor { Id = 3, Kind = ObjectType.Item, Name = "铁剑", Location = new(2, 0) };
        state.Apply(new S.ManualItemDropped { ObjectID = 2 }); var pickup = new ClientPickup();
        check(pickup.Next(state, prefs, 1)?.Id == 3, "手动丢弃后自动捡回或误排除同名掉落");
        state.Actors[3].Location = new(1, 0);
        check(pickup.Next(state, prefs, 2) == null, "混合格绕过手动丢弃保护");
        state.Apply(new S.ObjectRemove { ObjectID = 2 }); state.Apply(new S.MapChanged());
        check(state.ManuallyDropped.Contains(2), "离开视野或切图遗忘主动丢弃对象");
        state.Apply(new S.LogOutSuccess()); check(state.ManuallyDropped.Count == 0, "换角保留旧会话排除");
        for (uint id = 1; id <= 4096; id++) state.Apply(new S.ManualItemDropped { ObjectID = id });
        state.Apply(new S.ManualItemDropped { ObjectID = 4096 });
        check(state.ManuallyDropped.Count == 4096 && !state.ManualDropHistoryFull, "重复回执占用丢弃记录或误触上限");
        state.Apply(new S.ManualItemDropped { ObjectID = 4097 });
        check(state.ManualDropHistoryFull && state.ManuallyDropped.Contains(1) && state.ManuallyDropped.Count == 4096 && pickup.Next(state, prefs, 3) == null,
            "丢弃记录超限遗忘旧件或继续自动拣取");
        state.Apply(new S.LogOutSuccess()); check(!state.ManualDropHistoryFull, "新会话未清除记录超限状态");
    }

    private static void Protocol(Action<bool, string> check)
    {
        var previous = Packet.IsServer;
        try
        {
            Packet.IsServer = true;
            check(Packet.ReceivePacket(new C.ClientPresentationRequest().GetPacketBytes().ToArray(), out _) is C.ClientPresentationRequest, "客户端表现能力包错误");
            var request = (C.AppraiseEquipment)Packet.ReceivePacket(new C.AppraiseEquipment { ScrollID = 8, ItemID = 9 }.GetPacketBytes().ToArray(), out _);
            check(request.ScrollID == 8 && request.ItemID == 9, "卷轴目标唯一编号丢失");
            Packet.IsServer = false;
            var reply = (S.ObjectEquipmentGlow)Packet.ReceivePacket(new S.ObjectEquipmentGlow { ObjectID = 7, Glow = EquipmentGlow.Weapon }.GetPacketBytes().ToArray(), out _);
            check(reply.ObjectID == 7 && reply.Glow == EquipmentGlow.Weapon && (int)ServerPacketIds.ObjectEquipmentGlow > (int)ServerPacketIds.EquipmentAppraisalResult, "装备光效包不兼容");
        }
        finally { Packet.IsServer = previous; }
    }
}
