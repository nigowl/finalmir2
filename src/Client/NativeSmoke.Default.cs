using System.Diagnostics;
using System.Reflection;
using Godot;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    // 显式入口仅使用启动时给定的账号，不注册、发物或修改版本定义。
    private async Task VerifyDefaultClient()
    {
        stage = "default 自动登录与角色选择";
        await Until(() => Client.HandshakeReady && State.Characters.Count > 0);
        await VerifySelection(); await Screenshot("default-selection");
        var name = Argument("--character");
        var role = name.Length > 0 ? State.Characters.Single(character => character.Name == name) : State.Characters[0];
        Client.Send(new C.StartGame { CharacterIndex = role.Index });
        await Until(() => State.User != null && !Client.World.Loading && Client.MiniMap.Revision.Length == 64);
        GD.Print($"PASS default login: {State.User!.Name}, {State.User.Class}, map {State.Map!.FileName}, {State.User.Inventory.Length} total slots.");
        await VerifyDefaultInventory();
        await VerifyDefaultWindows();
        await VerifyDefaultMovement();
        stage = "default 资源与持续帧采样";
        var libraries = (System.Collections.IDictionary)typeof(ResourceCache).GetField("libraries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client.Assets)!;
        foreach (var kind in new[] { ObjectType.Item, ObjectType.Spell })
            Client.World.Visual(new Actor { Kind = kind, Image = 9999 }, Environment.TickCount64);
        if (libraries.Contains("Monster/9999")) throw new Exception("物品/地面特效仍请求怪物动画库。");
        var gaps = new List<double>(); var timer = Stopwatch.StartNew(); var previous = timer.Elapsed.TotalMilliseconds;
        while (timer.Elapsed.TotalSeconds < 5)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var now = timer.Elapsed.TotalMilliseconds; gaps.Add(now - previous); previous = now;
        }
        gaps.Sort();
        GD.Print($"PASS default frames: {gaps.Count} frames/5s, p95={gaps[(int)(gaps.Count * .95)]:F2}ms, max={gaps[^1]:F2}ms, textures={Client.Assets.TextureCount}, pending resources={Client.Assets.PendingDownloads}.");
        await Screenshot("default-world");
        GD.Print("PASS default checks: startup login, selection, separate bag/shortcuts, reversible item move, windows/tabs, maps, movement and ground-resource lookup.");
        if (!OS.GetCmdlineUserArgs().Contains("--leave-open")) await Client.Quit();
    }

    private async Task VerifyDefaultInventory()
    {
        stage = "default 80格背包与独立快捷栏";
        var inventory = State.User!.Inventory;
        var bag = GameArt("关闭背包").GetParent<Control>(); bag.Show();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var cells = bag.FindChildren("InventorySlot*", "Button", true, false).Cast<Button>().ToArray();
        var shown = cells.Select(cell => int.Parse(cell.Name.ToString()[13..])).Order().ToArray();
        if (inventory.Length != 86 || !shown.SequenceEqual(ItemPresentation.BagSlots(inventory).Order()) ||
            shown.Any(slot => slot < 6 && ItemPresentation.CanQuickUse(inventory[slot])) || bag.GetNode<Label>("WindowTitle").Text != "背包")
            throw new Exception("default 背包与快捷栏重复、容量不一致或旧装备失去入口。");
        if (Enumerable.Range(0, 6).Any(index => Client.FindChild($"ItemShortcut{index + 1}", true, false) == null))
            throw new Exception("快捷栏未保留六个独立入口。");
        var snapshot = string.Join('/', inventory.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.Count}"));
        var from = Array.FindIndex(inventory, 0, 6, ItemPresentation.CanQuickUse);
        var to = Array.FindLastIndex(inventory, item => item == null);
        if (from >= 0 && to >= 6)
        {
            var id = inventory[from].UniqueID;
            var quick = (Control)Client.FindChild($"ItemShortcut{from + 1}", true, false);
            var bagCell = await VisibleBagCell(to);
            async Task Drag(Control source, Control target)
            {
                var start = source.GetGlobalRect().GetCenter(); var end = target.GetGlobalRect().GetCenter();
                await MoveMouse(start); MouseButtonAt(start, true); await MoveMouse(end, true);
                packets.Clear(); MouseButtonAt(end, false);
            }
            await Drag(quick, bagCell);
            if (!(await Packet<S.MoveItem>()).Success || inventory[to]?.UniqueID != id) throw new Exception("default 道具移入背包失败。");
            await Drag(bagCell, quick);
            if (!(await Packet<S.MoveItem>()).Success || snapshot != string.Join('/', inventory.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.Count}")))
                throw new Exception("default 道具归位后格位、编号或数量不一致。");
        }
        else GD.Print("SKIP default reversible item move: no usable item or free bag slot.");
        await Screenshot("default-inventory"); bag.Hide();
        GD.Print($"PASS default inventory: 80 main bag slots, 6 independent shortcuts, {cells.Length} visible cells including legacy equipment, original item IDs/counts preserved.");
    }

    private async Task VerifyDefaultWindows()
    {
        stage = "default 窗口、页签与大小地图";
        var equipment = GameArt("关闭角色").GetParent<Control>(); equipment.Show();
        var tabs = equipment.GetChildren().OfType<OriginalButton>().Where(button => button.ToggleMode).OrderBy(button => button.Position.X).ToArray();
        foreach (var tab in tabs)
        {
            tab.EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (tabs.Count(button => button.ButtonPressed) != 1) throw new Exception("人物页签未互斥选中。");
        }
        equipment.Hide();
        var settings = SettingsPanel(); VerifySharedTabSpacing(); await Screenshot("default-settings"); CloseSettingsPanel(settings);
        var map = Client.MiniMap;
        map.SetExpanded(true); await Screenshot("default-large-map"); map.SetExpanded(false);
        if (map.Position.Y != 8 || map.GetRect().End.X != 1016 || map.GetNode<Label>("MapCaption").Visible)
            throw new Exception("default 小地图边距或名称显示不正确。");
        GD.Print("PASS default windows: character tabs mutually exclusive, settings tabs spaced, large/small map toggle and 8px margins.");
    }

    private async Task VerifyDefaultMovement()
    {
        stage = "default 短距离走跑及返回";
        var origin = State.User!.Location;
        bool Valid(Point point) => Client.World.Walkable(point) && !State.Actors.Values.Any(actor => actor.Location == point &&
            actor.Kind is ObjectType.Monster or ObjectType.Player or ObjectType.Merchant && !actor.Dead && actor.Id != State.User.ObjectID);
        var goal = Enumerable.Range(0, 8).Select(direction => Functions.PointMove(origin, (MirDirection)direction, 6))
            .FirstOrDefault(point => Valid(point) && Client.World.PathTo(point) is { Length: > 4 and < 10 });
        if (goal == default) { GD.Print("SKIP default movement: no short clear route."); return; }
        var map = State.Map!.MapIndex;
        var timer = Stopwatch.StartNew();
        var steps = new List<(int Distance, long Interval)>();
        var previous = origin; var previousAt = Environment.TickCount64;
        void Observe(Packet packet)
        {
            if (packet is not S.UserLocation location || location.Location == previous) return;
            var now = Environment.TickCount64;
            steps.Add((Functions.MaxDistance(previous, location.Location), now - previousAt));
            previous = location.Location; previousAt = now;
        }
        Client.PacketReceived += Observe;
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Client.SetDestination(goal, true); await Until(() => State.User.Location == goal);
                Client.SetDestination(origin, true); await Until(() => State.User.Location == origin);
            }
        }
        finally { Client.PacketReceived -= Observe; }
        if (State.Map?.MapIndex != map) throw new Exception("default 测试路线意外跨图。");
        GD.Print($"PASS default movement: {origin} -> {goal} -> {origin} ×3, {timer.ElapsedMilliseconds}ms, original TCP positions confirmed; steps={string.Join(',', steps.Select(step => $"{step.Distance}:{step.Interval}"))}.");
    }
}
