using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyNpcGoods(S.NPCGoods goods)
    {
        stage = "compact NPC links and clipped scrollable goods";
        var panel = (Control)Client.FindChild("NpcDialog", true, false);
        var scroll = (ScrollContainer)Client.FindChild("NpcScroll", true, false);
        var text = (VBoxContainer)Client.FindChild("NpcText", true, false);
        var rows = (VBoxContainer)Client.FindChild("NpcGoods", true, false);
        await Until(() => scroll.GetVScrollBar().MaxValue > scroll.GetVScrollBar().Page);
        var back = panel.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == "返回");
        var quest = panel.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == "任务");
        if (quest.Position.Y != back.Position.Y || quest.GetRect().End.X >= back.Position.X || quest.GetThemeStylebox("normal") is not StyleBoxTexture || back.GetThemeStylebox("normal") is not StyleBoxTexture)
            throw new Exception("NPC quest/back actions do not share the original button skin and bottom baseline.");
        if (goods.List.Count < 15 || !panel.ClipContents || !scroll.ClipContents || scroll.GetRect().End.Y > 190 || scroll.GetRect().End.X > 428 ||
            text.GetChildCount() != 0 || !back.IsVisibleInTree() || back.GetRect().End.Y > 225)
            throw new Exception("NPC content lacks compact highlighted/hover-underlined links or escaped its original art bounds.");
        var point = scroll.GetGlobalRect().GetCenter(); await MoveMouse(point);
        for (var index = 0; index < 25; index++) MouseButtonAt(point, true, MouseButton.WheelDown);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (scroll.ScrollVertical <= 0 || rows.GetChildren().Cast<Control>().Any(row => row.Size.X > scroll.Size.X))
            throw new Exception("The long goods list did not scroll within its clipped viewport.");
        var itemButton = rows.GetChildren().OfType<Button>().First();
        var insets = new[] { "normal", "hover", "pressed", "disabled" }.Select(state => itemButton.GetThemeStylebox(state).ContentMarginLeft).ToArray();
        if (insets.Any(inset => inset != 7)) throw new Exception("Hovering goods shifts the fixed text inset.");
        await Screenshot("npc-goods");
        packets.RemoveAll(packet => packet is S.NPCResponse);
        back.EmitSignal(BaseButton.SignalName.Pressed); await Packet<S.NPCResponse>();
        var links = text.FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().ToArray();
        if (links.Length == 0 || links.Any(link => link.Underline != LinkButton.UnderlineMode.OnHover || link.GetThemeFontSize("font_size") != 11))
            throw new Exception("NPC main links lack compact gold text and hover-only underlines.");
        await MoveMouse(links[0].GetGlobalRect().GetCenter());
        await Screenshot("npc-link-hover");
        Client.Send(new C.CallNPC { ObjectID = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant).Id, Key = "[@BUY]" }); await Packet<S.NPCGoods>();
        GD.Print("PASS native NPC goods: 20+ original entries, compact gold links with hover underline, wheel scroll and clipped original content area.");
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
        var source = Client.FindChildren("*", "Button", true, false).OfType<OriginalItemButton>().Single(button =>
            button.Position == (slot < 6 ? OriginalArt.QuickSlotPosition(slot) : new Vector2(9 + (slot - 6) % 8 * 37, 37 + (slot - 6) / 8 * 33)));
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
