using Godot;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyItemWeightOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("金币/重量检查必须使用隔离原生fixture。");
        var record = Argument("--weight-record");
        if (record.Length == 0) throw new InvalidOperationException("缺少--weight-record。");
        if (!OS.GetCmdlineUserArgs().Contains("--verify-save")) await VerifyGroundGoldArt();
        await EnterUiFixture();
        if (OS.GetCmdlineUserArgs().Contains("--verify-save"))
        {
            if (File.ReadAllText(record) != ItemWeightSnapshot()) throw new Exception("重启后物品编号/数量、金币或小数负重不一致。");
            GD.Print("PASS native item weight restart: exact inventory/equipment/quest IDs and counts, gold and decimal weight restored.");
        }
        else
        {
            await VerifyPotionWeight(); await VerifyGoldNotices();
            GameArt("背包 I").EmitSignal(BaseButton.SignalName.Pressed);
            var bag = GameArt("关闭背包").GetParent<Control>();
            await Until(() => bag.GetChildren().OfType<TextureRect>().Single().Texture != null &&
                bag.GetNode<Label>("BagWeight").Text == $"{State.InventoryWeight:0.##}/{State.CalculateStats()[Stat.BagWeight]}");
            var potion = State.User!.Inventory.First(item => item?.Info.Type == ItemType.Potion);
            var cell = await VisibleBagCell(Array.IndexOf(State.User.Inventory, potion));
            await MoveMouse(cell.GetGlobalRect().GetCenter()); await Screenshot("decimal-item-weight");
            if (!cell.TooltipText.Contains(ItemPresentation.Details(potion))) throw new Exception("真实药品详情未显示小数重量。");
            File.WriteAllText(record, ItemWeightSnapshot());
            GD.Print($"PASS native item weight: real buy/use/drop/pickup changes by 0.01 per potion, stable IDs/counts/gold, bag/HUD decimal text; saved weight={State.InventoryWeight:0.##}.");
        }
        await Client.Quit();
    }

    private string ItemWeightSnapshot()
    {
        object? Item(UserItem? item) => item == null ? null : new { item.UniqueID, item.ItemIndex, item.Count, item.Weight };
        var user = State.User!;
        return JsonSerializer.Serialize(new { user.Name, user.Gold, State.InventoryWeight,
            inventory = user.Inventory.Select(Item), equipment = user.Equipment.Select(Item), quests = user.QuestInventory.Select(Item) });
    }

    private async Task VerifyPotionWeight()
    {
        stage = "original potion buy/use/drop/pickup with decimal weight";
        var merchant = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
        packets.Clear(); Client.SelectTarget(merchant.Id); await Packet<S.NPCResponse>();
        Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@BUY]" });
        var goods = await Packet<S.NPCGoods>();
        var supply = goods.List.First(item => item.Info.Type == ItemType.Potion && item.Info.Stats[Stat.HP] > 0);
        int Count() => State.User!.Inventory.Where(item => item?.ItemIndex == supply.ItemIndex).Sum(item => item!.Count);
        var total = Count(); var weight = State.InventoryWeight; var gold = State.User!.Gold;
        Client.Send(new C.BuyItem { ItemIndex = supply.UniqueID, Count = 2, Type = goods.Type });
        await Until(() => Count() == total + 2 && State.User.Gold < gold);
        if (supply.Info.UnitWeight != .01m || State.InventoryWeight != weight + .02m) throw new Exception("购买2份药品的负重不是0.02。");
        GameArt("关闭 NPC").EmitSignal(BaseButton.SignalName.Pressed);
        var potion = State.User.Inventory.First(item => item?.ItemIndex == supply.ItemIndex)!;
        packets.Clear(); Client.UseItem(potion.UniqueID);
        if (!(await Packet<S.UseItem>()).Success || Count() != total + 1 || State.InventoryWeight != weight + .01m)
            throw new Exception("实际使用药品没有精确减重0.01。");
        var before = ItemWeightSnapshot(); var known = State.Actors.Keys.ToHashSet();
        packets.Clear(); Client.Send(new C.DropItem { UniqueID = potion.UniqueID, Count = 1 });
        if (!(await Packet<S.DropItem>()).Success) throw new Exception("重量检查的原丢弃请求被拒绝。");
        await Until(() => State.Actors.Values.Any(actor => !known.Contains(actor.Id) && actor.Kind == ObjectType.Item));
        var ground = State.Actors.Values.Single(actor => !known.Contains(actor.Id) && actor.Kind == ObjectType.Item);
        if (Count() != total || State.InventoryWeight != weight) throw new Exception("丢弃1份药品没有精确减重0.01。");
        Client.SetDestination(ground.Location); await Until(() => State.User.Location == ground.Location);
        Client.Send(new C.PickUp());
        await Until(() => !State.Actors.ContainsKey(ground.Id) && Count() == total + 1);
        if (ItemWeightSnapshot() != before) throw new Exception("药品丢弃/拾取后编号、数量、金币或重量不守恒。");
    }

    private async Task VerifyGroundGoldArt()
    {
        stage = "six gold tiers rendered through actual WorldView";
        await Until(() => Client.HandshakeReady && Client.Assets.Ready);
        var state = new GameState();
        state.Apply(new S.UserInformation { ObjectID = 0x71000000, HP = 100, Location = new(294, 576),
            Class = MirClass.Warrior, Gender = MirGender.Male, Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
        state.Me!.Hidden = true;
        var viewport = new SubViewport { Size = new Vector2I(1024, 768), World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Disable3D = true };
        Client.AddChild(viewport);
        var world = new WorldView { State = state, Assets = Client.Assets }; viewport.AddChild(world);
        try
        {
            await world.Load("0");
            // 只清本地测试地图的装饰，避免建筑遮挡样本，不写资源或服务端地图。
            foreach (var cell in world.Map!.MapCells) { cell.MiddleImage = 0; cell.FrontImage = 0; }
            var samples = new[] { (99u, 112, "少量金币"), (999u, 114, "中量金币"), (1000u, 116, "大量金币"),
                (1000000u, 117, "金条"), (5000000u, 121, "金砖"), (10000000u, 122, "金盒") };
            for (var i = 0; i < samples.Length; i++)
            {
                var (amount, frame, name) = samples[i];
                var point = new System.Drawing.Point(288 + i % 3 * 6, 572 + i / 3 * 8);
                state.Apply(new S.ObjectGold { ObjectID = (uint)(i + 1), Gold = amount, Location = point });
                if (state.Actors[(uint)(i + 1)].Image != frame) throw new Exception("金币档位素材错误：" + name);
                await Until(() => Client.Assets.Sprite("DNItems", frame) != null);
                viewport.AddChild(new Label { Text = $"{name} · {amount}", Theme = Client.Theme,
                    Position = new Vector2(70 + i % 3 * 288, 255 + i / 3 * 256), Size = new Vector2(240, 30), HorizontalAlignment = HorizontalAlignment.Center });
            }
            await Until(() => !world.Loading && Client.Assets.PendingDownloads == 0);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var shot = viewport.GetTexture().GetImage();
            if (Argument("--screenshot") is { Length: > 0 } path) shot.SavePng(Path.ChangeExtension(path, "gold-tiers.png"));
            GD.Print("PASS native gold art: DNItems 112/114/116/117/121/122 rendered by actual WorldView; ObjectGold quantities unchanged.");
        }
        finally { Client.RemoveChild(viewport); viewport.QueueFree(); }
    }
}
