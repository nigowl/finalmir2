using System.Text.RegularExpressions;
using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyChineseWorld()
    {
        await VerifyChineseFont();
        stage = "Chinese world, item names and NPC dialogue";
        static bool Chinese(string value) => Regex.IsMatch(value, @"[\u4e00-\u9fff]");
        if (!Chinese(State.Map!.Title) || Client.GetWindow().Title != "传奇2")
            throw new Exception("地图标题或窗口标题未汉化。");
        var items = State.User!.Inventory.Concat(State.User.Equipment).Where(i => i?.Info != null).ToArray();
        if (items.Length == 0 || items.Any(i => !Chinese(i!.Info.Name) || Regex.IsMatch(i.Info.Name, "[A-Za-z]{3}")))
            throw new Exception("原世界物品名称未汉化。");
        await Until(() => State.Actors.Values.Any(a => a.Kind == ObjectType.Merchant));
        var npc = State.Actors.Values.Where(a => a.Kind == ObjectType.Merchant)
            .OrderBy(a => Functions.MaxDistance(a.Location, State.User.Location)).First();
        if (npc.Name != "刘镖头") throw new Exception("原世界 NPC 未使用自然中文姓名：" + npc.Name);
        packets.RemoveAll(p => p is S.NPCResponse); Client.SelectTarget(npc.Id);
        await Until(() => packets.OfType<S.NPCResponse>().Any(p => p.Page.Any(Chinese)));
        var response = packets.OfType<S.NPCResponse>().Last(p => p.Page.Any(Chinese));
        var display = string.Join(" ", response.Page);
        if (!display.Replace(" ", "").Contains("鄙人姓刘，走镖为生")) throw new Exception("NPC 对话未使用符合身份的中文口吻。");
        display = Regex.Replace(display, @"<\$[^>]*>|/@[^>]*|/[A-Za-z]+\}", "");
        if (Regex.IsMatch(display, "[A-Za-z]{3}")) throw new Exception("原世界 NPC 对话残留英文：" + display);
        await Screenshot("chinese-npc");
        var service = Client.FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().FirstOrDefault(b => b.Text == "传送服务");
        if (service == null) throw new Exception("汉化后的传送服务按钮缺失。");
        packets.RemoveAll(p => p is S.NPCResponse); service.EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => packets.OfType<S.NPCResponse>().Any(p => p.Page.Any(line => line.Contains("比奇城/@BichonWall"))));
        await Screenshot("chinese-npc-links");
        using var close = new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true }; Client._UnhandledInput(close);
        GD.Print($"PASS native Chinese world: {State.Map.Title}, {items.Length} original item names, {npc.Name}, Chinese NPC dialogue and original script links.");
    }

    private async Task VerifyChineseFont()
    {
        stage = "bundled Chinese font coverage and rendering";
        const string sample = "杂货使者　药草使者　李铁匠　王屠户　张大婶\n购买杂货　出售杂货　修理兵器　锻造首饰\n雪原巨人　赤狐妖　千年尸霸　噬魂妖　魑魅魍魉";
        var font = Client.Theme.DefaultFont;
        var path = Argument("--font-characters");
        var characters = sample + (path.Length == 0 ? "" : File.ReadAllText(path));
        var chinese = characters.EnumerateRunes().Where(r => r.Value is >= 0x3400 and <= 0x9FFF).Distinct().ToArray();
        var file = (FontFile)font;
        var fallback = file.AllowSystemFallback; file.AllowSystemFallback = false;
        var missing = chinese.Where(r => !font.HasChar(r.Value)).ToArray();
        file.AllowSystemFallback = fallback;
        if (font != ThemeDB.FallbackFont || missing.Length != 0)
            throw new Exception("中文字体缺少字形或界面/场景字体不一致：" + string.Concat(missing));
        var panel = new Panel { Position = new Vector2(25, 45), Size = new Vector2(690, 175) };
        Client.AddChild(panel);
        var label = new Label { Text = sample, Position = new Vector2(16, 16), Size = new Vector2(658, 140) };
        label.AddThemeFontSizeOverride("font_size", 17); panel.AddChild(label);
        await Screenshot("chinese-font"); panel.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"PASS native Chinese font: {chinese.Length} distinct Chinese glyphs, UI and world share the bundled font.");
    }
}
