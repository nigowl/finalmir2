using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyInventoryOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("背包检查仅使用隔离fixture。");
        stage = "80 inventory cells in eight columns and ten visible rows";
        await Until(() => Client.HandshakeReady);
        Client.Login("NativeGM", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(role => role.Name == "NativeGM").Index });
        await Until(() => State.User != null && !Client.World.Loading);
        foreach (var slot in Enumerable.Range(0, 6).Where(slot => State.User!.Inventory[slot] != null && !ItemPresentation.CanQuickUse(State.User.Inventory[slot])))
        {
            var free = Array.FindIndex(State.User!.Inventory, 6, item => item == null);
            packets.Clear(); Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = slot, To = free });
            if (!(await Packet<S.MoveItem>()).Success) throw new Exception("隔离旧快捷装备无法移入背包。");
        }
        var bag = GameArt("关闭背包").GetParent<Control>(); bag.Show();
        var scroll = (ScrollContainer)Client.FindChild("InventoryScroll", true, false);
        var cells = scroll.FindChildren("InventorySlot*", "Button", true, false).Cast<Button>().OrderBy(cell => cell.Position.Y).ThenBy(cell => cell.Position.X).ToArray();
        await Until(() => bag.GetChildren().OfType<TextureRect>().Single().Texture != null);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (bag.GetNode<Label>("WindowTitle").Text != "背包") throw new Exception("背包标题未显示服务端实际80格容量。");
        if (State.User!.Inventory.Length != 86 || cells.Length != 80 || cells.Any(cell => int.Parse(cell.Name.ToString()[13..]) < 6) || cells.Select(cell => cell.Name.ToString()).Distinct().Count() != 80 ||
            cells.Select(cell => cell.Position.Y).Distinct().Count() != 10 || cells.GroupBy(cell => cell.Position.Y).Any(row => row.Count() != 8) || scroll.GetVScrollBar().Visible)
            throw new Exception("背包没有直接显示80个真实格位，或不是每行8格、10行。");
        foreach (var cell in cells)
        {
            if (cell.Size != new Vector2(36, 32) || !scroll.GetGlobalRect().Encloses(cell.GetGlobalRect()))
                throw new Exception($"{cell.Name}格位大小不一致或被裁切。");
            await MoveMouse(cell.GetGlobalRect().GetCenter());
            if (GetViewport().GuiGetHoveredControl() != cell) throw new Exception($"{cell.Name}不能接收鼠标输入。");
        }
        using var skin = bag.GetChildren().OfType<TextureRect>().Single().Texture.GetImage();
        if (skin.GetSize() != new Vector2I(313, 400)) throw new Exception("背包原边框没有完整延长。");
        for (var row = 1; row < 10; row++)
        for (var y = 0; y < 33; y++)
        for (var x = 9; x < 304; x++)
            if (skin.GetPixel(x, 37 + row * 33 + y) != skin.GetPixel(x, 37 + y))
                throw new Exception($"第{row + 1}行还有特殊空位或格线不一致。");
        stage = "original TCP item move into and out of the bottom-right inventory cell";
        var inventory = State.User.Inventory;
        var from = Array.FindIndex(inventory, 6, item => item != null && item.ItemIndex != inventory[85]?.ItemIndex);
        if (from < 0 || cells[^1].Name != "InventorySlot85") throw new Exception("隔离背包缺少最后一格交换检查物品。");
        string Snapshot() => string.Join('/', inventory.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.Count}"));
        var before = Snapshot(); var moved = inventory[from].UniqueID;
        packets.Clear(); Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = from, To = 85 });
        if (!(await Packet<S.MoveItem>()).Success || inventory[85]?.UniqueID != moved) throw new Exception("第10行第8格没有实际收下物品。");
        await MoveMouse(new Vector2(620, 470)); await Screenshot("inventory-8x10");
        packets.Clear(); Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = 85, To = from });
        if (!(await Packet<S.MoveItem>()).Success || before != Snapshot()) throw new Exception("背包格位交换后物品编号或数量改变。");
        stage = "legacy 46-slot server must not show phantom available cells";
        try
        {
            State.User.Inventory = inventory.Take(46).ToArray(); State.InventoryRevision++;
            await Until(() => scroll.FindChildren("InventorySlot*", "Button", true, false).Count == 40 && bag.GetNode<Label>("WindowTitle").Text == "背包");
            await MoveMouse(scroll.GlobalPosition + new Vector2(260, 100));
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screen = GetViewport().GetTexture().GetImage();
            var scale = (Vector2)screen.GetSize() / GetWindow().ContentScaleSize;
            var bottom = scroll.GlobalPosition + new Vector2(0, 8 * 33);
            var baseline = screen.GetPixelv((Vector2I)((bottom + new Vector2(20, 10)) * scale));
            foreach (var point in new[] { new Vector2(0, 10), new Vector2(37, 10), new Vector2(74, 0), new Vector2(240, 0) })
                if (screen.GetPixelv((Vector2I)((bottom + point) * scale)) != baseline)
                    throw new Exception("旧46格背包下方仍画出了虚假的空格线。");
            await Screenshot("inventory-legacy-capacity");
        }
        finally { State.User.Inventory = inventory; State.InventoryRevision++; }
        GD.Print("PASS native inventory: 80 unique bag cells plus 6 separate shortcuts, 8 columns, 10 fully visible rows, original backgrounds, bottom-right TCP swap and original item IDs/counts restored.");
        await Client.Quit();
    }
}
