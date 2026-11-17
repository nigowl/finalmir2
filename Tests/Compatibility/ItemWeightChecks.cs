using Crystal.Native;
using Mir2.Companions;
using Mir2.Companion;
using Server.MirDatabase;
using Server.MirObjects;
using S = ServerPackets;

internal static class ItemWeightChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var type in Enum.GetValues<ItemType>())
        foreach (var raw in new byte[] { 0, 1, 50, 100, 255 })
        {
            var info = new ItemInfo { Type = type, Weight = raw };
            check(info.IsEquipment ? info.UnitWeight == raw : info.UnitWeight is >= .01m and <= 1m,
                $"物品单重越界或装备标准重量改变：{type}/{raw}");
        }
        foreach (var (raw, expected) in new[] { ((byte)0, .01m), ((byte)1, .01m), ((byte)2, .02m), ((byte)3, .03m), ((byte)4, .04m) })
        {
            var potion = new UserItem(new ItemInfo { Type = ItemType.Potion, Weight = raw }) { Count = 10 };
            check(potion.Info.UnitWeight == expected && potion.Weight == expected * 10, "不同规格药品单重或堆叠重量未递增：" + raw);
        }
        var medicine = new UserItem(new ItemInfo { Index = 990101, Name = "红药", Type = ItemType.Potion, Weight = 1, StackSize = 999 }) { Count = 101 };
        var material = new UserItem(new ItemInfo { Index = 990102, Name = "材料", Type = ItemType.CraftingMaterial, Weight = 25, StackSize = 999 }) { Count = 3 };
        var task = new UserItem(new ItemInfo { Index = 990103, Type = ItemType.Quest, Weight = 0 }) { Count = 3 };
        var weapon = new UserItem(new ItemInfo { Index = 990104, Type = ItemType.Weapon, Weight = 12 });
        check(medicine.Weight == 1.01m && material.Weight == .75m && task.Weight == 3m && weapon.Weight == 12,
            "堆叠重量未精确累加，或将整叠错误限制为1");
        check(task.Info.UnitWeight == 1m && medicine.Info.UnitWeight < material.Info.UnitWeight && material.Info.UnitWeight < task.Info.UnitWeight,
            "消耗品/普通道具/任务专用品重量梯度错误");
        check(ItemPresentation.Details(medicine).Contains("单重 0.01 · 总重 1.01"), "物品详情没有区分单重与堆叠总重");
        foreach (var type in new[] { ItemType.Amulet, ItemType.Bait })
        {
            var pack = new UserItem(new ItemInfo { Type = type, Weight = 1 }) { Count = 1000 };
            check(pack.Weight == .01m, "护符/鱼饵的原整包重量语义被改变");
        }
        var player = new PlayerObject { Info = new CharacterInfo(), Stats = new Stats { [Stat.BagWeight] = 14 } };
        player.Info.Inventory[0] = medicine; player.Info.Inventory[1] = material; player.Info.Inventory[2] = weapon;
        player.Info.QuestInventory[0] = task; player.RefreshBagWeight();
        var state = new GameState { BaseStats = new BaseStats(MirClass.Warrior) { Stats = [new(Stat.HP) { Base = 100 }, new(Stat.BagWeight) { Base = 14 }] } };
        foreach (var item in new[] { medicine, material, task, weapon }) state.Apply(new S.NewItemInfo { Info = item.Info });
        state.Apply(new S.UserInformation { ObjectID = 1, HP = 100, Inventory = player.Info.Inventory, Equipment = player.Info.Equipment, QuestInventory = player.Info.QuestInventory });
        check(state.InventoryWeight == 13.76m && player.CurrentBagWeight == state.InventoryWeight, "服务器/客户端小数负重不一致或任务栏占普通负重");
        check(CompanionSafety.CanRun(state, 10000, 9500), "小数负重未超上限仍拒绝跑步");
        medicine.Count = 126; player.RefreshBagWeight();
        check(state.InventoryWeight == 14.01m && player.CurrentBagWeight == 14.01m && !CompanionSafety.CanRun(state, 10000, 9500), "0.01超重被截断后仍允许跑步");
        check(CompanionInventoryRules.CanCollect(7.99m, 10, 10, .01m, false, false) &&
            !CompanionInventoryRules.CanCollect(7.99m, 10, 10, .02m, false, false) &&
            !CompanionInventoryRules.CanCollect(9.99m, 10, 10, .02m, true, false), "拾取预留或重量上限丢失小数精度");
        check(!CompanionInventoryRules.NeedsSpace(7.99m, 10, 10) && CompanionInventoryRules.NeedsSpace(8m, 10, 10), "减负阈值未精确按80%判定");
        check(CompanionInventoryRules.CanCollect(10.39m, 13, 10, .01m, false, false) &&
            !CompanionInventoryRules.CanCollect(10.39m, 13, 10, .02m, false, false), "非整数80%容量丢失小数边界");
        RoundTrip(medicine.Info, check); RoundTrip(weapon.Info, check);
    }

    private static void RoundTrip(ItemInfo info, Action<bool, string> check)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) info.Save(writer);
        var bytes = stream.ToArray(); stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var loaded = new ItemInfo(reader);
        using var replay = new MemoryStream();
        using (var writer = new BinaryWriter(replay, System.Text.Encoding.UTF8, true)) loaded.Save(writer);
        check(loaded.Weight == info.Weight && loaded.UnitWeight == info.UnitWeight && bytes.SequenceEqual(replay.ToArray()),
            "小数重量改变了物品定义原字节存档/协议格式");
    }
}
