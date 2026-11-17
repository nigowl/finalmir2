using System.Text.Json;
using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private Control SettingsPanel()
    {
        var button = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "游戏设置");
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return Client.FindChildren("*", "Label", true, false).Cast<Label>().Single(label => label.Text == "游戏设置").GetParent<Control>();
    }

    private static CheckBox Setting(Control panel, string prefix) => panel.FindChildren("*", "CheckBox", true, false).Cast<CheckBox>().Single(button => button.Text.StartsWith(prefix));
    private static void CloseSettingsPanel(Control panel) => panel.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "关闭设置").EmitSignal(BaseButton.SignalName.Pressed);
    private static void SelectSetting(OptionButton choice, int index) { choice.Select(index); choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index); }

    private async Task VerifySettings()
    {
        stage = "tabbed settings, immediate persistence and original close art";
        var path = Environment.GetEnvironmentVariable("MIR2_CLIENT_SETTINGS") ?? throw new InvalidOperationException("Native settings checks require an isolated MIR2_CLIENT_SETTINGS path.");
        var panel = SettingsPanel();
        if (panel.GetThemeStylebox("panel") is not StyleBoxTexture || Client.Theme.GetStylebox("panel", "PopupMenu") is not StyleBoxTexture menuFrame ||
            Client.Theme.GetStylebox("panel", "GameMiniMap") is not StyleBoxTexture mapFrame || mapFrame.DrawCenter || mapFrame.Texture.GetSize() != new Vector2(64, 64) || menuFrame.Texture.GetSize() != new Vector2(64, 64) ||
            mapFrame.TextureMarginLeft != 24 || mapFrame.TextureMarginTop != 24 || mapFrame.TextureMarginRight != 24 || mapFrame.TextureMarginBottom != 24)
            throw new Exception("Settings/menu/maps did not reuse the processed original window frame.");
        using (var frame = menuFrame.Texture.GetImage())
        using (var source = Client.Assets.Sprite("Prguse", 995)!.Texture.GetImage())
            if (frame.GetPixel(12, 52) != source.GetPixel(12, source.GetHeight() - 12))
                throw new Exception("The full original lower corner ornament was cropped away.");
        using (var frame = menuFrame.Texture.GetImage())
        using (var map = mapFrame.Texture.GetImage())
        {
            if (frame.GetData().Where((_, index) => index % 4 == 3).Any(alpha => alpha != 255))
                throw new Exception("通用面板边框包含原图的透明空列或角区空洞。");
            if (Argument("--screenshot") is { Length: > 0 } screenshot) map.SavePng(Path.ChangeExtension(screenshot, "map-frame-skin.png"));
            foreach (var x in new[] { 0, 40 })
            foreach (var y in new[] { 0, 40 })
            for (var dx = 0; dx < 24; dx++)
            for (var dy = 0; dy < 24; dy++)
            {
                var original = frame.GetPixel(x + dx, y + dy); var actual = map.GetPixel(x + dx, y + dy);
                if (actual.A != 0 && actual != original)
                    throw new Exception("地图去除角饰底纹时损坏了原金饰。");
                if (actual.A == 0 && original != frame.GetPixel(32, 32))
                    throw new Exception("通用角饰内侧没有填入一致的面板底色。");
                if (actual.A != 0 && actual.R + actual.G + actual.B < .9f) throw new Exception("地图角饰仍有暗底残点。");
            }
            foreach (var x in new[] { 0, 40 })
            foreach (var y in new[] { 0, 40 })
                if (Enumerable.Range(0,24).Sum(dx => Enumerable.Range(0,24).Count(dy => map.GetPixel(x + dx, y + dy).A > 0)) < 64)
                    throw new Exception("地图抠底误删了角饰主体。");
            if (map.GetPixel(32, 12).A != 0 || map.GetPixel(12, 32).A != 0 || map.GetPixel(32, 52).A != 0 || map.GetPixel(52, 32).A != 0)
                throw new Exception("地图直边内侧仍有遮挡地图的多余填色。");
            foreach (var point in new[] { new Vector2I(20,20), new Vector2I(44,20), new Vector2I(20,44), new Vector2I(44,44) })
                if (map.GetPixelv(point).A != 0) throw new Exception("地图角饰内侧仍残留方形黑色底纹。");
        }
        if (!GetViewport().GuiEmbedSubwindows || Client.Theme.GetStylebox("embedded_border", "Window") is not StyleBoxTexture || Client.Theme.GetStylebox("panel", "AcceptDialog") is not StyleBoxEmpty ||
            Client.FindChildren("*", "AcceptDialog", true, false).OfType<AcceptDialog>().Any(dialog => !dialog.TransparentBg) ||
            Client.Theme.GetIcon("close", "Window").GetInstanceId() != Client.Assets.Sprite("Prguse2", 360)?.Texture.GetInstanceId())
            throw new Exception("Confirmation windows retained duplicate/platform borders or stale close art after reconnect.");
        var tabs = panel.FindChildren("*", "TabContainer", true, false).Cast<TabContainer>().Single();
        if (!panel.Visible || Client.Theme.DefaultFontSize != ClientUi.Body || tabs.GetTabCount() != 4 || !Enumerable.Range(0, 4).Select(tabs.GetTabTitle).SequenceEqual(new[] { "基础", "保护", "挂机", "职业" }))
            throw new Exception("The hammer did not open four settings tabs with the smaller font.");
        if (panel.FindChildren("*", "Button", true, false).Cast<Button>().Any(button => button.Text is "取消" or "应用") || Setting(panel, "自动喝红").ButtonPressed || Setting(panel, "自动随机").ButtonPressed || Setting(panel, "自动回城").ButtonPressed)
            throw new Exception("Settings retained confirmation buttons or enabled protection without consent.");
        tabs.CurrentTab = 3;
        var classTitles = tabs.GetChild<Control>(3).FindChildren("*", "CheckBox", true, false).Cast<CheckBox>().Select(option => option.Text).ToArray();
        if (classTitles.Length != 10 || !classTitles.Any(title => title.StartsWith("智能半月")) || !classTitles.Any(title => title.StartsWith("智能刺杀")) ||
            !classTitles.Contains("自动烈火剑法") || !classTitles.Contains("自动双龙斩")) throw new Exception("战士职业页混入其他职业或缺少辅助选项。");
        await Screenshot("settings-Warrior"); tabs.CurrentTab = 0;
        var slider = panel.FindChildren("*", "HSlider", true, false).Cast<HSlider>().Single();
        var music = Client.FindChildren("*", "AudioStreamPlayer", true, false).Cast<AudioStreamPlayer>().First();
        var movement = (OptionButton)panel.FindChild("MovementMode", true, false);
        var strength = (OptionButton)panel.FindChild("CrtStrength", true, false);
        if (strength.ItemCount != 5 || strength.Selected != 2) throw new Exception("CRT strength lost its five levels or compatible default.");
        var checkbox = Setting(panel, "显示怪物");
        if (checkbox.GetThemeIcon("checked").GetSize() != checkbox.GetThemeIcon("unchecked").GetSize() || new[] { "normal", "hover", "pressed", "hover_pressed" }.Select(state => checkbox.GetThemeStylebox(state).GetMinimumSize()).Distinct().Count() != 1)
            throw new Exception("Checkbox selection changes its icon width or text inset.");
        await Screenshot("checkbox-off");
        slider.Value = 40; SelectSetting(movement, 1);
        Setting(panel, "显示 NPC").ButtonPressed = false; Setting(panel, "显示怪物").ButtonPressed = true;
        Setting(panel, "显示物品").ButtonPressed = true;
        Setting(panel, "CRT").ButtonPressed = true;
        SelectSetting(strength, 4);
        await Until(() => File.Exists(path) && JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Volume").GetInt32() == 40);
        if (Client.World.ShowNpcNames || !Client.World.ShowMonsterNames || !Client.World.ShowItemNames || music.VolumeDb >= -20) throw new Exception("Autosaved name visibility or volume was not applied.");
        var crt = (ColorRect)Client.FindChild("CrtFilter", true, false);
        if (!crt.Visible || crt.MouseFilter != Control.MouseFilterEnum.Ignore || crt.Material is not ShaderMaterial)
            throw new Exception("Autosaved CRT filter did not enable its screen shader without intercepting input.");
        if ((int)((ShaderMaterial)crt.Material).GetShaderParameter("strength") != 5 || !JsonDocument.Parse(File.ReadAllText(path)).RootElement.TryGetProperty("CrtStrength", out var savedStrength) || savedStrength.GetInt32() != 5)
            throw new Exception("CRT strength did not persist or reach the shader.");
        await Screenshot("settings-basic");
        SelectSetting(strength, 0); CloseSettingsPanel(panel);
        if ((int)((ShaderMaterial)crt.Material).GetShaderParameter("strength") != 1) throw new Exception("The lowest CRT level did not apply.");
        panel = SettingsPanel(); SelectSetting(strength, 4); CloseSettingsPanel(panel); panel = SettingsPanel();
        tabs.CurrentTab = 1; await VerifyProtectionAlignment(tabs.GetChild<Control>(1));
        await Screenshot("settings-protection"); tabs.CurrentTab = 2; await Screenshot("settings-battle");
        CloseSettingsPanel(panel); panel = SettingsPanel();
        if (slider.Value != 40 || movement.Selected != 1 || strength.Selected != 4 || Setting(panel, "显示 NPC").ButtonPressed || !Setting(panel, "显示怪物").ButtonPressed || !Setting(panel, "显示物品").ButtonPressed || !Setting(panel, "CRT").ButtonPressed)
            throw new Exception("Reopening settings lost automatically saved values.");
        var backup = path + ".test-backup"; var saved = File.ReadAllText(path);
        File.Move(path, backup); Directory.CreateDirectory(path);
        try
        {
            slider.Value = 0;
            await Until(() => panel.FindChildren("*", "Label", true, false).Cast<Label>().Any(label => label.Text.StartsWith("保存失败")));
            if (!panel.Visible || music.VolumeDb <= -80 || File.ReadAllText(backup) != saved)
                throw new Exception("A failed autosave changed applied preferences or discarded the saved file.");
        }
        finally { Directory.Delete(path); File.Move(backup, path); }
        slider.Value = 100; SelectSetting(movement, 0); Setting(panel, "显示 NPC").ButtonPressed = true; Setting(panel, "显示怪物").ButtonPressed = false;
        Setting(panel, "CRT").ButtonPressed = false;
        Setting(panel, "显示物品").ButtonPressed = false;
        CloseSettingsPanel(panel);
        if (crt.Visible) throw new Exception("Disabling the saved CRT option left the filter visible.");
        var closeButtons = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Where(button => button.Library == "Prguse2" && button.Index == 360).ToArray();
        if (closeButtons.Length < 8 || closeButtons.Any(button => button.GetThemeStylebox("hover") is not StyleBoxEmpty)) throw new Exception("A dialog close button retained the square hover style.");
        if (closeButtons.Single(button => button.TooltipText == "关闭 NPC").Position != new Vector2(413, 3) || closeButtons.Single(button => button.TooltipText == "关闭背包").Position != new Vector2(289, 3) || closeButtons.Single(button => button.TooltipText == "关闭设置").GetRect().End.X > 437)
            throw new Exception("Close art is not aligned with the original dialog borders.");
        var chat = Client.FindChildren("*", "LineEdit", true, false).Cast<LineEdit>().Single(edit => edit.MaxLength == Globals.MaxChatLength);
        var messages = (RichTextLabel)Client.FindChild("ChatMessages", true, false);
        if (chat.GetThemeFontSize("font_size") != ClientUi.Small || messages.GetThemeFontSize("normal_font_size") != ClientUi.Small || !Client.FindChildren("*", "Panel", true, false).Cast<Panel>().Any(frame => frame.GetRect().Encloses(chat.GetRect()) && frame.Position == new Vector2(230, 668) && frame.Size == new Vector2(629, 68)))
            throw new Exception("Chat font/frame did not shrink inside the original HUD.");
        ToggleBattleMenu();
        var menuButton = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "游戏菜单 Ctrl+E");
        using (var input = new InputEventMouseButton { Position = menuButton.GetGlobalRect().GetCenter(), ButtonIndex = MouseButton.Left, Pressed = true }) Client._Input(input);
        menuButton.EmitSignal(BaseButton.SignalName.Pressed);
        var menu = (PopupMenu)Client.FindChild("GameMenu", true, false);
        if (menu.GetItemText(menu.GetItemIndex(7)) != "停止挂机") throw new Exception("Opening the game menu stopped active automation.");
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, 7L); menu.Hide();
        if (menu.GetItemText(menu.GetItemIndex(7)) != "开始挂机") throw new Exception("The menu did not switch back after stopping automation.");
        Client.GetWindow().GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        menu.PopupCentered();
        menu.GrabFocus();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await MoveMouse((Vector2)menu.Position + (Vector2)menu.Size / 2);
        if (!menu.Visible) throw new Exception("The game menu closed before its actions could be selected.");
        await Screenshot("original-game-menu"); menu.Hide();
        GD.Print("PASS native settings: four tabs, autosave/reopen/failure recovery, name switches, mouse walk/run swap, start/stop menu, smaller aligned chat and original close hover.");
    }

    private void ToggleBattleMenu()
    {
        var button = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "游戏菜单 Ctrl+E");
        button.EmitSignal(BaseButton.SignalName.Pressed);
        var menu = (PopupMenu)Client.FindChild("GameMenu", true, false);
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, 7L); menu.Hide();
    }

    private void EnableAutomaticBattle(bool roam)
    {
        var panel = SettingsPanel(); Setting(panel, "无怪跑图").ButtonPressed = roam;
        CloseSettingsPanel(panel); ToggleBattleMenu();
    }

    private async Task VerifyAutomaticManaPotion()
    {
        stage = "acknowledged automatic mana potion";
        var blue = State.User!.Inventory.First(item => item?.Info?.Type == ItemType.Potion && item.Info.Stats[Stat.MP] > 0 && item.Info.Stats[Stat.HP] == 0);
        // 原引擎施法锁为 1800ms，等待它结束后在空格施法，避免等级恢复 MP 掩盖喝药条件。
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        var mp = State.User.MP; var count = blue.Count;
        packets.RemoveAll(packet => packet is S.Magic);
        Client.Send(new C.Magic { ObjectID = State.User.ObjectID, Spell = Spell.FireBall, Direction = MirDirection.Up, TargetID = 0, Location = new Point(State.User.Location.X, State.User.Location.Y - 1) });
        if (!(await Packet<S.Magic>()).Cast) throw new Exception("The empty-cell spell used to exercise auto-MP was rejected.");
        await Until(() => State.User.MP < mp);
        var panel = SettingsPanel(); Setting(panel, "自动喝蓝").ButtonPressed = true;
        panel.FindChildren("*", "SpinBox", true, false).Cast<SpinBox>().OrderBy(input => input.Position.Y).ElementAt(1).Value = 95;
        CloseSettingsPanel(panel);
        await Until(() => packets.OfType<S.UseItem>().Any(packet => packet.UniqueID == blue.UniqueID));
        var result = packets.OfType<S.UseItem>().First(packet => packet.UniqueID == blue.UniqueID); packets.Remove(result);
        var remaining = State.User.Inventory.FirstOrDefault(item => item?.UniqueID == blue.UniqueID)?.Count ?? 0;
        if (!result.Success || remaining != count - 1) throw new Exception("Automatic mana use did not consume exactly one acknowledged potion.");
        panel = SettingsPanel(); Setting(panel, "自动喝蓝").ButtonPressed = false; CloseSettingsPanel(panel);
        GD.Print("PASS native auto-MP: empty-cell spell lowers MP, enabled threshold selects blue potion, exactly one item consumed after server acknowledgement.");
    }

    private async Task VerifyAutomaticRoam()
    {
        stage = "automatic same-map roaming and manual cancellation";
        using var cancel = new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true }; Client._UnhandledInput(cancel);
        var start = State.User!.Location;
        EnableAutomaticBattle(true);
        await Until(() => State.User.Location != start && Client.MiniMap.Route.Count > 1);
        Client._UnhandledInput(cancel); var stopped = State.User.Location;
        await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
        if (State.User.Location != stopped || Client.MiniMap.Route.Count != 0) throw new Exception("Manual cancellation did not stop the roaming path.");
        Client.SetDestination(start); await Until(() => State.User.Location == start);
        await Until(() => Environment.TickCount64 - State.Me!.MotionTime >= Actor.MoveDuration);
        GD.Print("PASS native auto-battle: acknowledged monster kill, same-map roaming, visible route and immediate Esc cancellation without a follow-up move.");
    }
}
