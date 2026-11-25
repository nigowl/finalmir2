using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task<OriginalItemButton> VisibleBagCell(int slot)
    {
        var cell = (OriginalItemButton)Client.FindChild($"InventorySlot{slot}", true, false);
        var scroll = (ScrollContainer)Client.FindChild("InventoryScroll", true, false);
        scroll.EnsureControlVisible(cell);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!scroll.GetGlobalRect().Encloses(cell.GetGlobalRect())) throw new Exception($"背包{slot}格被裁切：{cell.GetGlobalRect()} / {scroll.GetGlobalRect()}。");
        return cell;
    }

    private async Task VerifyItemUiOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("物品界面检查必须使用隔离fixture。");
        await EnterUiFixture();
        if (State.User!.Inventory.Where(item => item?.Info.Type == ItemType.Potion && item.Info.Stats[Stat.HP] > 0).Sum(item => item!.Count) < 2)
        {
            var merchant = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
            Client.SelectTarget(merchant.Id); await Packet<S.NPCResponse>();
            Client.Send(new C.CallNPC { ObjectID = merchant.Id, Key = "[@BUY]" });
            var goods = await Packet<S.NPCGoods>();
            var potion = goods.List.First(item => State.Items[item.ItemIndex].Type == ItemType.Potion && State.Items[item.ItemIndex].Stats[Stat.HP] > 0);
            Client.Send(new C.BuyItem { ItemIndex = potion.UniqueID, Count = 2, Type = goods.Type });
            await Until(() => State.User.Inventory.Where(item => item?.Info.Type == ItemType.Potion && item.Info.Stats[Stat.HP] > 0).Sum(item => item!.Count) >= 2);
            GameArt("关闭 NPC").EmitSignal(BaseButton.SignalName.Pressed);
        }
        stage = "compact minimap without caption and translucent item UI";
        var map = Client.MiniMap; map.SetExpanded(false);
        if (map.GetNode<Label>("MapCaption").Visible || Math.Abs(map.Size.Y - map.ImageRect.Size.Y - 6) > .1 || map.Position.Y != 8 || map.GetRect().End.X != 1016)
            throw new Exception("小地图名称或底部空条未移除，或8px外边距改变。");
        map.SetExpanded(true);
        if (!map.GetNode<Label>("MapCaption").Visible) throw new Exception("大地图标题被误删除。");
        map.SetExpanded(false);
        if (Client.Theme.GetStylebox("panel", "GameItemTooltip") is not StyleBoxFlat style || style.BgColor.A is < .6f or > .8f)
            throw new Exception("装备对比背景不是半透明。");
        GameArt("背包 I").EmitSignal(BaseButton.SignalName.Pressed);
        var bag = GameArt("关闭背包").GetParent<Control>();
        await Until(() => bag.GetChildren().OfType<TextureRect>().Single().Texture != null);
        if (State.User.Inventory.Length != Globals.BaseInventorySize || bag.FindChildren("InventorySlot*", "Button", true, false).Count != ItemPresentation.BagSlots(State.User.Inventory).Length)
            throw new Exception("背包格位没有与服务端的80格一致。");
        await VisibleBagCell(6); await VisibleBagCell(85);
        await VerifyQuickItemFiltering();
        var sample = new UserItem(new ItemInfo { Name = "详情检查", Type = ItemType.Weapon,
            Stats = new Stats { [Stat.MinDC] = 3, [Stat.MaxDC] = 9 } });
        var details = Client.BuildItemTooltip(sample);
        var wrapper = new PanelContainer { Position = new Vector2(220, 240), MouseFilter = Control.MouseFilterEnum.Ignore };
        wrapper.AddThemeStyleboxOverride("panel", style); wrapper.AddChild(details); Client.AddChild(wrapper);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var text = (RichTextLabel)details.FindChild("ItemDetails", true, false);
        if (text.GetParsedText().Contains("滚轮查看") || text.GetParsedText().Contains("耐久")) throw new Exception("详情仍显示滚轮提示或0/0耐久。");
        await CombatScreenshot("item-ui-translucent"); wrapper.QueueFree();
        await VerifyItemTooltipScroll(sample);
        bag.Hide();
        GD.Print("PASS focused native item UI: captionless compact minimap, 80 accessible inventory cells, equipment shortcut rejection, actual potion drag/key/click, translucent comparison, durability/category text and bounded tooltip wheel.");
        await Client.Quit();
    }

    private async Task VerifyQuickItemFiltering()
    {
        stage = "actual equipment drag rejection and original potion acknowledgement";
        var user = State.User!;
        var gear = user.Inventory.FirstOrDefault(item => item?.Info.IsEquipment == true);
        if (gear == null) throw new Exception("隔离背包缺少用于快捷栏过滤的装备。");
        var from = Array.IndexOf(user.Inventory, gear);
        if (from != 0)
        { packets.Clear(); Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = from, To = 0 }); if (!(await Packet<S.MoveItem>()).Success) throw new Exception("不能准备旧快捷格装备。"); }
        var quick = (OriginalItemButton)Client.FindChild("ItemShortcut1", true, false);
        await Until(() => quick.SlotIcon == null);
        packets.Clear();
        using (var key = new InputEventKey { PhysicalKeycode = Key.Key1, Pressed = true }) Client._UnhandledInput(key);
        await MoveMouse(quick.GetGlobalRect().GetCenter()); MouseButtonAt(quick.GetGlobalRect().GetCenter(), true); MouseButtonAt(quick.GetGlobalRect().GetCenter(), false);
        var source = await VisibleBagCell(0); var target = (Control)Client.FindChild("ItemShortcut2", true, false);
        await MoveMouse(source.GetGlobalRect().GetCenter()); MouseButtonAt(source.GetGlobalRect().GetCenter(), true);
        await MoveMouse(target.GetGlobalRect().GetCenter(), true); MouseButtonAt(target.GetGlobalRect().GetCenter(), false);
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        if (user.Inventory[0]?.UniqueID != gear.UniqueID || packets.Any(packet => packet is S.MoveItem or S.EquipItem or S.UseItem))
            throw new Exception("快捷栏允许拖入或使用装备。");
        var free = from >= 6 ? from : 6;
        source = await VisibleBagCell(0); await MoveMouse(source.GetGlobalRect().GetCenter()); MouseButtonAt(source.GetGlobalRect().GetCenter(), true);
        target = await VisibleBagCell(free); await MoveMouse(target.GetGlobalRect().GetCenter(), true); MouseButtonAt(target.GetGlobalRect().GetCenter(), false);
        if (!(await Packet<S.MoveItem>()).Success || user.Inventory[free]?.UniqueID != gear.UniqueID) throw new Exception("旧快捷格装备无法从普通背包取出。");
        var potion = user.Inventory.First(item => item?.Info.Type == ItemType.Potion && item.Info.Stats[Stat.HP] > 0);
        from = Array.IndexOf(user.Inventory, potion);
        if (from < 6)
        { packets.Clear(); Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = from, To = 7 }); if (!(await Packet<S.MoveItem>()).Success) throw new Exception("不能准备背包药品拖动。"); from = 7; }
        source = await VisibleBagCell(from);
        await MoveMouse(source.GetGlobalRect().GetCenter()); MouseButtonAt(source.GetGlobalRect().GetCenter(), true);
        await MoveMouse(quick.GetGlobalRect().GetCenter(), true); packets.Clear(); MouseButtonAt(quick.GetGlobalRect().GetCenter(), false);
        if (!(await Packet<S.MoveItem>()).Success || user.Inventory[0]?.UniqueID != potion.UniqueID) throw new Exception("可使用药品不能拖入快捷栏。");
        await Until(() => quick.SlotIcon != null);
        await UseQuickPotion(false); await UseQuickPotion(true);
        var notices = (Control)Client.FindChild("ItemNotices", true, false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!notices.GetChildren().OfType<PanelContainer>().Any(panel => panel.GetThemeStylebox("panel") is StyleBoxFlat skin && skin.BgColor.A is >= .6f and <= .8f))
            throw new Exception("物品消息背景没有半透明。");
    }
}
