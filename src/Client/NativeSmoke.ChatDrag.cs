using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task MoveMouse(Vector2 position, bool held = false)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless") GetViewport().WarpMouse(position);
        using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position, ButtonMask = held ? MouseButtonMask.Left : 0 };
        GetViewport().PushInput(motion, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void MouseButtonAt(Vector2 position, bool pressed, MouseButton button = MouseButton.Left)
    {
        using var mouse = new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = button, Pressed = pressed,
            ButtonMask = pressed && button == MouseButton.Left ? MouseButtonMask.Left : 0 };
        GetViewport().PushInput(mouse, true);
    }

    private async Task VerifyChatInteraction()
    {
        stage = "chat wheel, history retention and top-only bounded resizing";
        var messages = (RichTextLabel)Client.FindChild("ChatMessages", true, false);
        var frame = (Control)Client.FindChild("ChatFrame", true, false);
        var handle = (Control)Client.FindChild("ChatResizeHandle", true, false);
        var input = Client.FindChildren("*", "LineEdit", true, false).Cast<LineEdit>().Single(edit => edit.MaxLength == Globals.MaxChatLength);
        var original = messages.GetParsedText(); var location = State.User!.Location; var inputRect = input.GetRect();
        for (var line = 0; line < 40; line++) Client.Log($"聊天滚动检查 {line:00}");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var bar = messages.GetVScrollBar(); var bottom = bar.Value;
        var point = messages.GetGlobalRect().GetCenter(); await MoveMouse(point);
        for (var step = 0; step < 5; step++) MouseButtonAt(point, true, MouseButton.WheelUp);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (bar.Value >= bottom || State.User.Location != location) throw new Exception("Mouse wheel did not scroll chat or leaked into the world.");
        var reading = bar.Value; Client.Log("阅读历史时的新消息");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (bar.Value != reading) throw new Exception("A new message displaced the chat history being read.");
        point = handle.GetGlobalRect().GetCenter(); await MoveMouse(point); MouseButtonAt(point, true);
        await MoveMouse(point - new Vector2(0, 200), true); MouseButtonAt(point - new Vector2(0, 200), false);
        if (frame.Size != new Vector2(629, 136) || frame.Position != new Vector2(230, 600) || input.GetRect() != inputRect)
            throw new Exception($"Top resize exceeded twice the original height or moved the fixed input/bottom edge: frame={frame.GetRect()}, handle={handle.GetRect()}, input={input.GetRect()} vs {inputRect}.");
        await Screenshot("chat-expanded");
        point = handle.GetGlobalRect().GetCenter(); await MoveMouse(point); MouseButtonAt(point, true);
        await MoveMouse(point + new Vector2(0, 300), true); MouseButtonAt(point + new Vector2(0, 300), false);
        if (frame.Size != new Vector2(629, 68) || frame.Position != new Vector2(230, 668)) throw new Exception("Chat shrank below its original height.");
        point = frame.GetRect().End - Vector2.One; await MoveMouse(point); MouseButtonAt(point, true);
        await MoveMouse(point - new Vector2(0, 35), true); MouseButtonAt(point - new Vector2(0, 35), false);
        if (frame.Size.Y != 68) throw new Exception("Dragging the bottom resized chat.");
        input.GrabFocus(); input.Text = "@mm";
        foreach (var key in new[] { Key.M, Key.I, Key.K, Key.C, Key.G, Key.Key1 })
        {
            using var typing = new InputEventKey { PhysicalKeycode = key, Pressed = true };
            Client._UnhandledInput(typing);
        }
        if (Client.MiniMap.Expanded || input.Text != "@mm") throw new Exception("Typing a chat command triggered a game shortcut.");
        input.Text = ""; input.ReleaseFocus();
        messages.Text = original;
        GD.Print("PASS native chat: wheel scroll, new messages retain history position, top-only 68–136px resize and fixed bottom/input.");
    }

    private async Task VerifyItemDrag()
    {
        stage = "item cursor preview, inventory move, drop cancellation and authoritative ground drop";
        var start = State.User!.Location;
        var item = State.User!.Inventory.First(item => item?.Info?.Type == ItemType.Potion && item.Info.Stats[Stat.HP] > 0);
        using var key = new InputEventKey { PhysicalKeycode = Key.I, Pressed = true }; Client._UnhandledInput(key);
        // 按原格位位置定位，避免依赖翻译后的物品提示文本。
        OriginalItemButton Cell(int slot) => Client.FindChildren("*", "Button", true, false).OfType<OriginalItemButton>().Single(button =>
            button.Position == (slot < 6 ? new Vector2(242 + slot * 35, 621) : new Vector2(9 + (slot - 6) % 8 * 37, 37 + (slot - 6) / 8 * 33)));
        var from = Array.IndexOf(State.User.Inventory, item); var source = Cell(from).GetGlobalRect().GetCenter();
        var to = Enumerable.Range(6, 40).First(slot => State.User.Inventory[slot] == null); var target = Cell(to).GetGlobalRect().GetCenter();
        await MoveMouse(source); MouseButtonAt(source, true); await MoveMouse(target, true);
        var preview = (TextureRect)Client.FindChild("ItemDragIcon", true, false);
        if (!preview.Visible || preview.Texture == null || preview.GetGlobalRect().GetCenter().DistanceTo(target) > 1 || preview.MouseFilter != Control.MouseFilterEnum.Ignore)
            throw new Exception($"The dragged item texture did not follow the cursor or intercepted input: visible={preview.Visible}, texture={preview.Texture != null}, rect={preview.GetGlobalRect()}, target={target}, source={source}.");
        await Screenshot("item-drag"); packets.RemoveAll(packet => packet is S.MoveItem); MouseButtonAt(target, false);
        var moved = await Packet<S.MoveItem>();
        if (!moved.Success || moved.From != from || moved.To != to || State.User.Inventory[to]?.UniqueID != item.UniqueID)
            throw new Exception("Dragging inventory cells did not receive its own authoritative move result.");
        var count = item.Count; var dialog = (ConfirmationDialog)Client.FindChild("DropItemDialog", true, false);
        source = Cell(to).GetGlobalRect().GetCenter(); target = new Vector2(600, 400);
        await MoveMouse(source); MouseButtonAt(source, true); await MoveMouse(target, true); MouseButtonAt(target, false);
        await Until(() => dialog.Visible);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var hint = dialog.FindChildren("*", "Label", true, false).Cast<Label>().Single(label => label.Text.StartsWith("丢弃 "));
        var quantity = dialog.FindChildren("*", "SpinBox", true, false).Cast<SpinBox>().Single();
        if (hint.GetGlobalRect().Intersects(quantity.GetGlobalRect()) || quantity.Size.Y > 40)
            throw new Exception("Drop confirmation overlaps its hint and quantity input.");
        await Screenshot("drop-confirmation");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide();
        if (item.Count != count) throw new Exception("Canceling a drop consumed the item locally.");
        await MoveMouse(source); MouseButtonAt(source, true);
        using (var cancel = new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true }) GetViewport().PushInput(cancel, true);
        await MoveMouse(target, true); MouseButtonAt(target, false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (dialog.Visible || preview.Visible) throw new Exception("Esc did not cancel item dragging.");
        packets.RemoveAll(packet => packet is S.DropItem);
        await MoveMouse(source); MouseButtonAt(source, true); await MoveMouse(target, true); MouseButtonAt(target, false);
        await Until(() => dialog.Visible);
        if (item.Count != count) throw new Exception("Opening drop confirmation changed inventory before acknowledgement.");
        var beforeActors = State.Actors.Keys.ToHashSet();
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide();
        var result = await Packet<S.DropItem>();
        if (!result.Success || result.UniqueID != item.UniqueID || result.Count != 1 || (State.User.Inventory.FirstOrDefault(value => value?.UniqueID == item.UniqueID)?.Count ?? 0) != count - 1)
            throw new Exception("Ground drop did not deduct exactly the acknowledged count.");
        await Until(() => State.Actors.Values.Any(actor => actor.Kind == ObjectType.Item && !beforeActors.Contains(actor.Id)));
        var loot = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Item && !beforeActors.Contains(actor.Id));
        Client.World.ShowItemNames = false; Client.World.UpdateHover(Vector2.Zero, true);
        if (Client.World.NameVisible(loot)) throw new Exception("Disabled ground labels remained visible without hover.");
        Client.World.UpdateHover(Client.World.Screen(loot.Location) + new Vector2(24, 16), false);
        if (Client.World.Hovered != loot.Id || !Client.World.NameVisible(loot)) throw new Exception("Hovering a ground item did not reveal its name.");
        Client.World.UpdateHover(Vector2.Zero, true); Client.World.ShowItemNames = true;
        if (!Client.World.NameVisible(loot)) throw new Exception("Enabled ground item names were hidden.");
        await Screenshot("ground-item-names"); Client.World.ShowItemNames = false;
        Client.SelectTarget(loot.Id); await Until(() => !State.Actors.ContainsKey(loot.Id));
        if (State.User.Inventory.Where(value => value?.ItemIndex == item.ItemIndex).Sum(value => value.Count) < count)
            throw new Exception("Picking the dropped item back up lost inventory quantity.");
        Client.SetDestination(start); await Until(() => State.User.Location == start);
        await Until(() => Environment.TickCount64 - State.Me!.MotionTime >= Actor.MoveDuration);
        Client._UnhandledInput(key);
        GD.Print("PASS native item drag: cursor art, inventory relocation, cancel/Esc, quantity confirmation, server drop acknowledgement and ground pickup conservation.");
    }
}
