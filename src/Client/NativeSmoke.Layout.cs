using Godot;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyItemTooltipScroll(UserItem item)
    {
        stage = "bounded equipment tooltip and actual mouse wheel";
        item.Info!.ToolTip = string.Join('\n', Enumerable.Range(1, 60).Select(index => $"滚动明细 {index:D2}：装备说明与要求。"));
        var button = new ItemTipButton { Position = new Vector2(460, 180), Text = "物品", TooltipText = "装备详情",
            TooltipBuilder = () => Client.BuildItemTooltip(item) };
        Client.AddChild(button); button.Size = new Vector2(40, 32);
        try
        {
            await MoveMouse(button.GetGlobalRect().GetCenter());
            await Until(() => GodotObject.IsInstanceValid(button.OpenTooltip) && button.OpenTooltip!.IsVisibleInTree());
            var tooltip = button.OpenTooltip!;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var bar = tooltip.GetVScrollBar();
            if (tooltip.Size.Y > ItemTooltip.MaximumHeight || tooltip.Size.Y < 100 || bar.MaxValue <= bar.Page ||
                tooltip.GetParent<PopupPanel>().GetThemeStylebox("panel") is not StyleBoxFlat { BorderWidthLeft: 1, ContentMarginLeft: 8 })
                throw new Exception($"长装备详情没有限制高度或使用紧凑细边背景：尺寸={tooltip.Size}，内容={bar.MaxValue}，页高={bar.Page}。");
            await Screenshot("equipment-tooltip-bounded");
            WheelAt(button.GetGlobalRect().GetCenter(), MouseButton.WheelDown);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(tooltip) || !tooltip.IsVisibleInTree() || tooltip.ScrollVertical <= 0)
                throw new Exception("真实滚轮没有滚动装备详情，或滚轮使提示消失。");
            for (var index = 0; index < 50; index++) WheelAt(button.GetGlobalRect().GetCenter(), MouseButton.WheelDown);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Math.Abs(tooltip.ScrollVertical - (bar.MaxValue - bar.Page)) > 1) throw new Exception("长装备详情无法滚到最后一行。");
            await Screenshot("equipment-tooltip-scroll");
            for (var index = 0; index < 50; index++) WheelAt(button.GetGlobalRect().GetCenter(), MouseButton.WheelUp);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (tooltip.ScrollVertical != 0) throw new Exception("装备详情无法向上滚回顶部。");
            await MoveMouse(new Vector2(80, 80));
            await Until(() => !GodotObject.IsInstanceValid(tooltip));
            button.TooltipBuilder = () => Client.BuildItemTooltip(new UserItem(new ItemInfo { Name = "短说明药品", Type = ItemType.Potion }));
            await MoveMouse(button.GetGlobalRect().GetCenter());
            await Until(() => GodotObject.IsInstanceValid(button.OpenTooltip) && button.OpenTooltip!.IsVisibleInTree());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (button.OpenTooltip!.Size.Y >= 120) throw new Exception($"短物品详情保留了首次富文本换行撑出的空白：{button.OpenTooltip.Size}。");
            await Screenshot("item-tooltip-short");
            await MoveMouse(new Vector2(80, 80));
            await Until(() => !GodotObject.IsInstanceValid(button.OpenTooltip));
            GD.Print("PASS native equipment tooltip: 360px content maximum, compact dark 1px frame, actual wheel to bottom/top, short-content shrink and cleanup on exit.");
        }
        finally { button.QueueFree(); }
    }

    private void WheelAt(Vector2 point, MouseButton direction)
    {
        using var wheel = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = direction, Pressed = true, Factor = 1 };
        GetViewport().PushInput(wheel, true);
    }

    private async Task VerifyProtectionAlignment(Control page)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var toggles = page.GetChildren().OfType<CheckBox>().OrderBy(toggle => toggle.Position.Y).ToArray();
        var thresholds = page.GetChildren().OfType<SpinBox>().OrderBy(spin => spin.Position.Y).ToArray();
        var labels = page.GetChildren().OfType<Label>().Where(label => label.Text is "生命≤" or "魔力≤").OrderBy(label => label.Position.Y).ToArray();
        if (toggles.Length != 4 || thresholds.Length != 4 || labels.Length != 4) throw new Exception("保护设置缺少四组独立选项或阈值。");
        for (var index = 0; index < 4; index++)
        {
            var toggle = toggles[index]; var spin = thresholds[index]; var label = labels[index]; var edit = spin.GetLineEdit();
            if (toggle.Size.Y != 28 || spin.Size.Y != 28 || label.Size.Y != 28 || toggle.GetRect().GetCenter().Y != spin.GetRect().GetCenter().Y ||
                label.GetRect().GetCenter().Y != spin.GetRect().GetCenter().Y || edit.GetThemeStylebox("normal") is not StyleBoxTexture ||
                spin.Position.X != thresholds[0].Position.X || edit.GetThemeFontSize("font_size") != ClientUi.Body)
                throw new Exception($"保护设置第{index + 1}行没有统一28px高度和中心线：{toggle.GetRect()} / {label.GetRect()} / {spin.GetRect()}。");
        }
    }

    private async Task VerifyMapFrameFill()
    {
        if (DisplayServer.GetName() == "headless") return;
        stage = "opaque map corners over contrasting scene backgrounds";
        var map = Client.MiniMap; var parent = map.GetParent(); var index = map.GetIndex(); var expanded = map.Expanded;
        var backdrop = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(backdrop);
        try
        {
            await MoveMouse(new Vector2(100, 700));
            foreach (var large in new[] { false, true })
            {
                map.SetExpanded(large); backdrop.Position = map.Position; backdrop.Size = map.Size;
                backdrop.MoveToFront(); map.MoveToFront();
                backdrop.Color = Colors.Magenta; var first = await Pixels();
                backdrop.Color = Colors.Cyan; var second = await Pixels();
                if (!first.SequenceEqual(second)) throw new Exception("地图角区或边线仍透出后面的场景底色。");
                backdrop.Hide(); await Screenshot(large ? "map-filled-large" : "map-filled-small"); backdrop.Show();
            }
        }
        finally
        {
            parent.RemoveChild(backdrop); backdrop.QueueFree(); map.SetExpanded(expanded); parent.MoveChild(map, index);
        }
        GD.Print("PASS native map frame: small/large map corners and borders remain pixel-identical over magenta/cyan backgrounds; no scene leakage.");

        async Task<byte[]> Pixels()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var frame = GetViewport().GetTexture().GetImage();
            var scale = (Vector2)frame.GetSize() / GetWindow().ContentScaleSize;
            var rect = map.GetGlobalRect();
            using var region = frame.GetRegion(new Rect2I((Vector2I)((rect.Position + Vector2.One) * scale).Round(), (Vector2I)((rect.Size - Vector2.One * 2) * scale).Floor()));
            return region.GetData();
        }
    }

    private async Task VerifyTabMouseStates(Button[] tabs)
    {
        await MoveMouse(new Vector2(680, 160));
        var baseline = await TabPixels(tabs, "before");
        foreach (var tab in tabs.Concat(tabs.Reverse()))
        {
            await MoveMouse(tab.GetGlobalRect().GetCenter());
            MouseButtonAt(tab.GetGlobalRect().GetCenter(), true);
            MouseButtonAt(tab.GetGlobalRect().GetCenter(), false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!tab.ButtonPressed || tabs.Count(button => button.ButtonPressed) != 1 || tabs.Any(button => button.Size != new Vector2(59, ClientUi.TabHeight)))
                throw new Exception("真实鼠标往返切换后页签选中状态或尺寸不一致。");
        }
        await MoveMouse(new Vector2(680, 160));
        var restored = await TabPixels(tabs, "restored");
        if (baseline.Where((pixels, index) => !pixels.SequenceEqual(restored[index])).Any())
            throw new Exception("页签往返切换后像素未恢复，存在边线/文字残影或位置漂移。");
        var hover = (StyleBoxTexture)tabs[0].GetThemeStylebox("hover");
        if (hover.Texture.GetInstanceId() != ((StyleBoxTexture)tabs[0].GetThemeStylebox("normal")).Texture.GetInstanceId() ||
            tabs.Select(tab => tab.ButtonGroup.GetInstanceId()).Distinct().Count() != 1 ||
            Client.Theme.GetStylebox("panel", "TooltipPanel").GetInstanceId() == Client.Theme.GetStylebox("panel", "GameItemTooltip").GetInstanceId())
            throw new Exception("未选中页签悬停冒充选中底图，或页签没有互斥分组。");
        await MoveMouse(tabs[1].GetGlobalRect().GetCenter());
        var hovered = await TabPixels(tabs, "hover");
        if (baseline.Where((pixels, index) => index != 1 && !pixels.SequenceEqual(hovered[index])).Any())
            throw new Exception("悬停一个页签改变了其他页签的边线或选中像素。");
        MouseButtonAt(tabs[1].GetGlobalRect().GetCenter(), true);
        var pressed = await TabPixels(tabs, "pressed");
        if (!tabs[1].ButtonPressed || tabs.Count(tab => tab.ButtonPressed) != 1) throw new Exception("按下新页签时新旧页面同时高亮，或没有立即切换。");
        MouseButtonAt(tabs[1].GetGlobalRect().GetCenter(), false);
        var selected = await TabPixels(tabs, "selected");
        if (pressed.Where((pixels, index) => !pixels.SequenceEqual(selected[index])).Any()) throw new Exception("页签松开时边线或文字像素跳动。");
        await Screenshot("tabs-after-switching");
        tabs[0].EmitSignal(BaseButton.SignalName.Pressed);
        GD.Print("PASS native tab pixels: eight actual switches restore every tab pixel; hover leaves other tabs unchanged; normal/hover/pressed/selected strips captured.");
    }

    private async Task<byte[][]> TabPixels(Button[] tabs, string state)
    {
        if (DisplayServer.GetName() == "headless") return tabs.Select(_ => Array.Empty<byte>()).ToArray();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        var scale = (Vector2)frame.GetSize() / GetWindow().ContentScaleSize;
        var pixels = new List<byte[]>();
        foreach (var tab in tabs)
        {
            var rect = tab.GetGlobalRect();
            using var region = frame.GetRegion(new Rect2I((Vector2I)(rect.Position * scale).Round(), (Vector2I)(rect.Size * scale).Round()));
            pixels.Add(region.GetData());
        }
        if (Argument("--screenshot") is { Length: > 0 } path)
        {
            var rect = tabs[0].GetGlobalRect(); rect.Size = new Vector2(tabs[^1].GetGlobalRect().End.X - rect.Position.X, rect.Size.Y);
            using var strip = frame.GetRegion(new Rect2I((Vector2I)(rect.Position * scale).Round(), (Vector2I)(rect.Size * scale).Round()));
            strip.SavePng(Path.ChangeExtension(path, $"tab-pixels-{state}.png"));
        }
        return pixels.ToArray();
    }
}
