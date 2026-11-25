using System.Reflection;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyClientPolish()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("客户端专项仅使用隔离fixture。");
        await Until(() => Client.HandshakeReady);
        Client.Login("NativeGM", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(role => role.Name == "NativeGM").Index });
        await Until(() => State.User != null && !Client.World.Loading && Client.MiniMap.Revision.Length == 64);
        var prefs = (ClientPreferences)typeof(Main).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!;
        prefs.SmartBattle = prefs.AutoSell = false; prefs.DefaultRun = true;
        await VerifyCrtPixels(); await VerifyOwnedItemFilter();
        if (!OS.GetCmdlineUserArgs().Contains("--polish-visual-items-only")) await VerifyKeyboardTravel();
        await VerifyPeriodicOrganization();
        GD.Print("PASS native client polish: CRT Mattias1/3/5 GPU pixels/off restoration, owned-item filter and saved rules, eight-direction original TCP travel/release/chat focus, periodic merge/sort and item conservation.");
        await Client.Quit();
    }

    private async Task VerifyOwnedItemFilter()
    {
        stage = "owned item filter and immediate rule persistence";
        var panel = SettingsPanel(); var tabs = panel.FindChildren("*", "TabContainer", true, false).Cast<TabContainer>().Single(); tabs.CurrentTab = 4;
        var filter = (CheckBox)panel.FindChild("ItemRuleOwnedOnly", true, false);
        var table = (Tree)panel.FindChild("ItemRuleTable", true, false);
        var search = (LineEdit)panel.FindChild("ItemRuleSearch", true, false);
        List<TreeItem> Rows()
        { var rows = new List<TreeItem>(); for (var row = table.GetRoot()?.GetFirstChild(); row != null; row = row.GetNext()) rows.Add(row); return rows; }
        filter.ButtonPressed = true;
        var owned = State.User!.Inventory.Where(item => item?.Info != null).Select(item => item.Info.FriendlyName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!owned.SetEquals(Rows().Select(row => row.GetText(3)))) throw new Exception("背包筛选遗漏物品或显示未持有物品。");
        var first = Rows()[0]; var name = first.GetText(3); var selected = first.IsChecked(2);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var point = table.GlobalPosition + table.GetItemAreaRect(first, 2).GetCenter();
        await MoveMouse(point); MouseButtonAt(point, true); MouseButtonAt(point, false);
        if (first.IsChecked(2) == selected) throw new Exception("筛选后的物品规则不能点击修改。");
        search.Text = name; search.EmitSignal(LineEdit.SignalName.TextChanged, name);
        if (Rows().Any(row => !row.GetText(3).Contains(name, StringComparison.OrdinalIgnoreCase))) throw new Exception("背包筛选未叠加名称搜索。");
        search.Text = ""; search.EmitSignal(LineEdit.SignalName.TextChanged, ""); filter.ButtonPressed = false;
        if (Rows().Count <= owned.Count) throw new Exception("取消背包筛选没有恢复目录。");
        filter.ButtonPressed = true; await Screenshot("owned-item-settings"); CloseSettingsPanel(panel);
        await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
        var path = Environment.GetEnvironmentVariable("MIR2_CLIENT_SETTINGS")!;
        var saved = System.Text.Json.JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(path))!;
        var preferences = (ClientPreferences)typeof(Main).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!;
        if (!saved.ItemRules.OrderBy(pair => pair.Key).SequenceEqual(preferences.ItemRules.OrderBy(pair => pair.Key))) throw new Exception("筛选后修改的物品规则未保存。");
    }

    private void Arrow(Key key, bool pressed)
    {
        using var input = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed };
        GetViewport().PushInput(input, true);
    }

    private async Task VerifyKeyboardTravel()
    {
        stage = "eight direction key travel, release and input focus";
        Client.GetWindow().GrabFocus();
        var start = State.User!.Location;
        var centre = Enumerable.Range(-10, 21).SelectMany(x => Enumerable.Range(-10, 21).Select(y => new System.Drawing.Point(start.X + x, start.Y + y)))
            .OrderBy(point => Functions.MaxDistance(point, start))
            .First(point => Enumerable.Range(-2, 5).All(x => Enumerable.Range(-2, 5).All(y => Client.World.Walkable(new System.Drawing.Point(point.X + x, point.Y + y)))) &&
                !State.Actors.Values.Any(actor => actor.Id != State.User.ObjectID && actor.Kind is not (ObjectType.Item or ObjectType.Spell) && Functions.MaxDistance(actor.Location, point) <= 2) &&
                (point == start || Client.World.NextStep(point) != null));
        foreach (var keys in new[] { new[] { Key.Up }, [Key.Up, Key.Right], [Key.Right], [Key.Down, Key.Right], [Key.Down], [Key.Down, Key.Left], [Key.Left], [Key.Up, Key.Left] })
        {
            Client.SetDestination(centre, true); await Until(() => State.User!.Location == centre);
            await ToSignal(GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout);
            var origin = State.User!.Location;
            var direction = ClientMovement.KeyboardDirection(keys.Contains(Key.Up), keys.Contains(Key.Down), keys.Contains(Key.Left), keys.Contains(Key.Right))!.Value;
            stage = $"keyboard {direction} from {origin}";
            foreach (var key in keys) Arrow(key, true);
            try { await Until(() => State.User.Location != origin); }
            finally { foreach (var key in keys) Arrow(key, false); }
            if (Main.Direction(origin, State.User.Location) != direction) throw new Exception("方向键实际移动方向不匹配。");
            var stopped = State.User.Location;
            await ToSignal(GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout);
            if (State.User.Location != stopped) throw new Exception("松开方向键后仍持续移动。");
            GD.Print($"PASS native keyboard {direction}: {origin} -> {stopped}.");
        }
        var chat = Client.FindChildren("*", "LineEdit", true, false).Cast<LineEdit>().Single(input => input.PlaceholderText.StartsWith("聊天："));
        chat.GrabFocus(); var location = State.User!.Location; Arrow(Key.Right, true);
        await ToSignal(GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout); Arrow(Key.Right, false);
        if (State.User.Location != location) throw new Exception("聊天输入时方向键仍移动角色。");
        chat.ReleaseFocus(); await CombatScreenshot("keyboard-travel");
        GD.Print("PASS native keyboard: all eight directions confirmed by server, no movement after release or while typing.");
    }

    private async Task VerifyPeriodicOrganization()
    {
        stage = "30 second automatic inventory merge with original TCP receipts";
        var original = State.User!.Inventory.First(item => item?.Info != null && !item.Info.IsEquipment && item.Info.StackSize > 1 && item.Count >= 3);
        var total = State.User.Inventory.Where(item => item?.ItemIndex == original.ItemIndex).Sum(item => item.Count);
        var originalIds = State.User.Inventory.Where(item => item != null).Select(item => item.UniqueID).ToHashSet();
        packets.Clear(); Client.Send(new C.SplitItem { Grid = MirGridType.Inventory, UniqueID = original.UniqueID, Count = 1 });
        await Until(() => packets.OfType<S.SplitItem1>().Any(packet => packet.Success) && State.User.Inventory.Any(item => item != null && !originalIds.Contains(item.UniqueID)));
        var split = State.User.Inventory.First(item => item != null && !originalIds.Contains(item.UniqueID));
        var slot = Array.FindIndex(State.User.Inventory, item => item?.UniqueID == split.UniqueID);
        if (slot < 6)
        {
            var free = Array.FindIndex(State.User.Inventory, 6, item => item == null); packets.Clear();
            Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = slot, To = free }); await Until(() => packets.OfType<S.MoveItem>().Any(packet => packet.Success));
        }
        var organizer = (ClientInventoryOrganizer)typeof(Main).GetField("inventoryOrganizer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!;
        organizer.Reset(); packets.Clear();
        await ToSignal(GetTree().CreateTimer(29), SceneTreeTimer.SignalName.Timeout);
        if (packets.OfType<S.MergeItem>().Any()) throw new Exception("整理早于30秒触发。");
        await Until(() => packets.OfType<S.MergeItem>().Any(packet => packet.Success));
        await Until(() => organizer.Pending == null);
        if (State.User.Inventory.Where(item => item?.ItemIndex == original.ItemIndex).Sum(item => item.Count) != total || !State.User.Inventory.Any(item => item?.UniqueID == original.UniqueID))
            throw new Exception("自动整理改变物品总数或目标原编号。");
        var bag = GameArt("关闭背包").GetParent<Control>(); bag.Show();
        var cells = Client.FindChild("InventoryScroll", true, false).FindChildren("InventorySlot*", "Button", true, false).Cast<Button>().ToArray();
        var displayed = cells.OrderBy(cell => cell.Position.Y).ThenBy(cell => cell.Position.X).Select(cell => int.Parse(cell.Name.ToString()[13..])).ToArray();
        if (!displayed.SequenceEqual(ClientInventoryOrganizer.DisplayOrder(State.User.Inventory))) throw new Exception("背包实际格子未按整理顺序排列。");
        await CombatScreenshot("organized-inventory");
        GD.Print("PASS native organization: no early merge, real 30s interval, server merge acknowledgements, unchanged totals/original target IDs and sorted reused cells.");
    }
}
