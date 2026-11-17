using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyNpcGoods(S.NPCGoods goods)
    {
        stage = "grouped original goods, item icons and inline quest link";
        var panel = (Control)Client.FindChild("NpcDialog", true, false);
        var scroll = (ScrollContainer)Client.FindChild("NpcScroll", true, false);
        var rows = (VBoxContainer)Client.FindChild("NpcGoods", true, false);
        var groups = rows.GetChildren().OfType<Button>().ToArray();
        if (groups.Length != goods.List.GroupBy(item => (item.Info.Type, item.Info.FriendlyName)).Count() ||
            panel.GetChildren().OfType<Button>().Any(button => button.Text is "返回" or "任务") ||
            !panel.ClipContents || scroll.GetRect().End.Y > 190 || scroll.GetRect().End.X > 428)
            throw new Exception("商品分组、移除底部任务/返回按钮或内容裁剪不正确。");
        groups[0].ButtonPressed = true;
        var first = rows.FindChildren("GoodsItem", "Button", true, false).OfType<ItemTipButton>().First();
        await Until(() => first.Icon != null);
        if (new[] { "normal", "hover", "pressed" }.Any(state => first.GetThemeStylebox(state).ContentMarginLeft != 16))
            throw new Exception("商品图标或悬停固定内边距错误。");
        await Screenshot("npc-grouped-goods");
        packets.RemoveAll(packet => packet is S.NPCResponse);
        Client.Send(new C.CallNPC { ObjectID = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant).Id, Key = "[@MAIN]" });
        await Packet<S.NPCResponse>();
        var link = (LinkButton)Client.FindChild("NpcQuestLink", true, false);
        if (link.Text != "查看 任务" || link.Underline != LinkButton.UnderlineMode.OnHover || link.GetParent().Name != "NpcText")
            throw new Exception("任务入口没有位于对白内并使用统一链接样式。");
        await Screenshot("npc-inline-quest");
        Client.Send(new C.CallNPC { ObjectID = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant).Id, Key = "[@BUY]" }); await Packet<S.NPCGoods>();
        GD.Print("PASS native NPC goods: deduplicated expandable groups, real item icons, fixed hover inset, clipped scroll and inline task link; no back/footer actions.");
    }

    private async Task VerifyNpcRepair()
    {
        stage = "visible repair slot and inventory-only original repair protocol";
        var npc = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
        packets.RemoveAll(packet => packet is S.NPCResponse or S.NPCRepair or S.RepairItem or S.ItemRepaired or S.RemoveItem);
        Client.SelectTarget(npc.Id); await Packet<S.NPCResponse>();
        Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@REPAIR]" }); await Packet<S.NPCRepair>();
        var panel = (Control)Client.FindChild("NpcRepair", true, false);
        var cell = (Button)Client.FindChild("RepairCell", true, false);
        var confirm = panel.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == "修理");
        if (!panel.IsVisibleInTree() || !confirm.Disabled) throw new Exception("Repair did not reveal an empty placement slot and disabled submit button.");
        var item = State.User!.Equipment.FirstOrDefault(item => item?.Info != null && item.CurrentDura < item.MaxDura && item.MaxDura > 0)
            ?? throw new Exception("The isolated danger monster did not damage any equipment for repair testing.");
        var index = Array.IndexOf(State.User.Equipment, item);
        using var equipment = new InputEventKey { PhysicalKeycode = Key.C, Pressed = true }; Client._UnhandledInput(equipment);
        var equipmentPanel = Client.FindChild("PlayerAttributes", true, false).GetParent<Control>();
        var origin = new Vector2[] { new(123,7),new(163,7),new(203,7),new(203,134),new(203,98),new(8,170),new(203,170),new(8,206),new(203,206),new(8,242),new(48,242),new(88,242),new(128,242),new(203,62) }[index] + new Vector2(8,90);
        equipmentPanel.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Position == origin).EmitSignal(BaseButton.SignalName.Pressed);
        if (!(await Packet<S.RemoveItem>()).Success || State.User.Equipment[index] != null || packets.OfType<S.RepairItem>().Any())
            throw new Exception("Repair mode bypassed inventory unequip or sent an invalid equipped-item repair.");
        Client._UnhandledInput(equipment);
        var slot = Array.FindIndex(State.User.Inventory, value => value?.UniqueID == item.UniqueID);
        var source = await VisibleBagCell(slot);
        await MoveMouse(source.GetGlobalRect().GetCenter()); MouseButtonAt(source.GetGlobalRect().GetCenter(), true);
        await MoveMouse(cell.GetGlobalRect().GetCenter(), true); MouseButtonAt(cell.GetGlobalRect().GetCenter(), false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (confirm.Disabled || cell.Icon == null) throw new Exception("Dragging the backpack item did not populate the repair slot and price.");
        await Screenshot("npc-repair");
        var gold = State.User.Gold; var durability = item.CurrentDura;
        confirm.EmitSignal(BaseButton.SignalName.Pressed);
        var repaired = await Packet<S.ItemRepaired>();
        if (repaired.UniqueID != item.UniqueID || item.CurrentDura != item.MaxDura || item.CurrentDura < durability || State.User.Gold > gold)
            throw new Exception("Repair failed to apply server durability/gold results.");
        packets.RemoveAll(packet => packet is S.NPCResponse);
        Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" }); await Packet<S.NPCResponse>();
        Client.UseItem(item.UniqueID); if (!(await Packet<S.EquipItem>()).Success) throw new Exception("Repaired equipment failed to re-equip.");
        using var close = new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true }; Client._UnhandledInput(close);
        using var inventory = new InputEventKey { PhysicalKeycode = Key.I, Pressed = true }; Client._UnhandledInput(inventory);
        GD.Print("PASS native repair: visible placement/price, original unequip then drag, acknowledged inventory repair, authoritative durability/gold and re-equip.");
    }
}
