using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task ClickMapTeleport(Point point)
    {
        await Until(() => !Client.World.Loading);
        var map = Client.MiniMap; map.SetExpanded(true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var cell = map.ImageRect.Position + new Vector2((point.X + .5f) / Client.World.Map!.Width, (point.Y + .5f) / Client.World.Map.Height) * map.ImageRect.Size;
        if (map.CellAt(cell) != point) throw new Exception("地图右键坐标没有往返映射到原格子。");
        var position = map.GlobalPosition + cell;
        await MoveMouse(position); MouseButtonAt(position, true, MouseButton.Right); MouseButtonAt(position, false, MouseButton.Right);
    }

    private async Task VerifyGmMapTeleport()
    {
        stage = "GM right-click map coordinate teleport";
        Client.Login("NativeGM", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(character => character.Name == "NativeGM").Index });
        await Until(() => State.User != null && Client.World.Map != null && !Client.World.Loading && Client.MiniMap.Revision.Length == 64);
        var start = State.User!.Location; var target = new Point(start.X + 3, start.Y - 2);
        if (!Client.World.Walkable(target)) throw new Exception("隔离GM传送落点不可通行。");
        await ClickMapTeleport(target); await Until(() => State.User.Location == target);
        if (Client.MiniMap.Expanded || Client.MiniMap.Route.Count != 0) throw new Exception("GM坐标传送仍保留寻路路线或大地图。");
        await Screenshot("gm-map-teleport");
        await ClickMapTeleport(start); await Until(() => State.User.Location == start);
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        await Client.Connect(Argument("--server")); await Until(() => Client.HandshakeReady); packets.Clear();
        GD.Print("PASS native GM map teleport: actual right-click coordinates, original @mv server authority, same-map arrival and no walking route.");
    }

    private async Task VerifyMapTeleportDenied(Point start)
    {
        stage = "unauthorized map teleport and letterbox rejection";
        var target = new Point(start.X + 2, start.Y - 2); var map = Client.MiniMap;
        map.SetExpanded(true); packets.RemoveAll(packet => packet is S.Chat);
        using (var outside = new InputEventMouseButton { Position = Vector2.Zero, ButtonIndex = MouseButton.Right, Pressed = true }) map._GuiInput(outside);
        if (!map.Expanded) throw new Exception("右键地图留白错误关闭了地图。");
        await ClickMapTeleport(target);
        await Until(() => packets.OfType<S.Chat>().Any(chat => chat.Message.Contains("无权限")));
        if (State.User!.Location != start || map.Route.Count != 0) throw new Exception("无GM或装备权限时发生传送或生成了寻路。");
    }

    private async Task VerifyRingMapTeleport()
    {
        stage = "equipped teleport ring and original cooldown";
        var ring = State.User!.Inventory.Single(item => item?.Info?.Name == "NativeTeleportRing");
        Client.UseItem(ring.UniqueID);
        if (!(await Packet<S.EquipItem>()).Success) throw new Exception("原服拒绝佩戴隔离传送戒指。");
        var start = State.User.Location; var target = new Point(start.X + 2, start.Y);
        packets.RemoveAll(packet => packet is S.Chat);
        await ClickMapTeleport(target); await Until(() => State.User.Location == target);
        if (!packets.OfType<S.Chat>().Any(chat => chat.Message.Contains("坐标传送成功")))
            await Until(() => packets.OfType<S.Chat>().Any(chat => chat.Message.Contains("坐标传送成功")));
        packets.RemoveAll(packet => packet is S.Chat);
        await ClickMapTeleport(start);
        await Until(() => packets.OfType<S.Chat>().Any(chat => chat.Message.Contains("秒") || chat.Message.Contains("second", StringComparison.OrdinalIgnoreCase)));
        if (State.User.Location != target) throw new Exception("传送装备绕过了原10秒冷却。");
        var free = Array.IndexOf(State.User.Inventory, null);
        Client.Send(new C.RemoveItem { Grid = MirGridType.Inventory, UniqueID = ring.UniqueID, To = free });
        if (!(await Packet<S.RemoveItem>()).Success) throw new Exception("测试传送戒指无法卸下。");
        await Screenshot("ring-map-teleport");
        Client.SetDestination(start); await Until(() => State.User.Location == start);
        await Until(() => Environment.TickCount64 - State.Me!.MotionTime >= Actor.MoveDuration);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("PASS native map teleport: non-GM without gear denied; equipped ring arrives at clicked coordinates, immediate retry rejected by original cooldown, item restored to inventory.");
    }
}
