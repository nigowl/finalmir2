using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyUiOnly()
    {
        stage = "focused UI check using existing disposable smoke character";
        await EnterUiFixture();
        var equipment = GameArt("关闭角色").GetParent<Control>();
        var tabs = equipment.GetChildren().OfType<OriginalButton>().Where(button => button.ToggleMode).OrderBy(button => button.Position.X).ToArray();
        tabs[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => equipment.GetChildren().OfType<TextureRect>().Single().Texture != null && tabs[0].GetThemeStylebox("pressed") is StyleBoxTexture);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (equipment.GetThemeStylebox("panel") is not StyleBoxEmpty || tabs.Any(tab => tab.Size != new Vector2(59, ClientUi.TabHeight)) ||
            tabs[0].Position.X != 11 || tabs[^1].GetRect().End.X != equipment.Size.X - 11)
            throw new Exception($"人物面板重复绘框或页签整组越界：{equipment.GetThemeStylebox("panel").GetClass()}，{equipment.Size}，{string.Join(';', tabs.Select(tab => tab.GetRect()))}，min={tabs[0].GetCombinedMinimumSize()}。");
        CheckOriginalTabArtwork(equipment, tabs[0]);
        await VerifyTabMouseStates(tabs); await Screenshot("character-tabs-clean");
        OpenSkills();
        var row = SkillRow(Spell.FireBall); var icon = row.GetNode<SkillIconButton>("SkillIcon");
        if (!icon.TooltipText.Contains("发射火球") || !icon.TooltipText.Contains("冷却：") || icon.TooltipText != row.GetNode<Label>("SkillName").TooltipText ||
            Client.Theme.GetStylebox("panel", "TooltipPanel") is not StyleBoxFlat tip || tip.BgColor.A < .95f)
            throw new Exception("技能说明缺少功能/冷却或背景过于透明。");
        await MoveMouse(icon.GetGlobalRect().GetCenter()); await Screenshot("skill-description");
        GameArt("关闭角色").EmitSignal(BaseButton.SignalName.Pressed);
        GameArt("背包 I").EmitSignal(BaseButton.SignalName.Pressed);
        var bag = GameArt("关闭背包").GetParent<Control>();
        await Until(() => bag.GetChildren().OfType<TextureRect>().Single().Texture != null);
        if (bag.GetThemeStylebox("panel") is not StyleBoxEmpty || bag.GetChildren().OfType<TextureRect>().Single().Size != new Vector2(313, 235))
            throw new Exception("背包仍存在重复边框或底图被拉伸。");
        await MoveMouse(new Vector2(600, 400)); await Screenshot("bag-single-frame");
        bag.Hide(); Client.OpenGuild();
        var guild = GameArt("关闭行会 H").GetParent<Control>();
        await Until(() => guild.GetChildren().OfType<TextureRect>().Single().Texture != null);
        if (guild.GetThemeStylebox("panel") is not StyleBoxEmpty || guild.Size != new Vector2(590, 432))
            throw new Exception("行会仍有底部重复边框。");
        await Screenshot("guild-single-frame");
        GameArt("关闭行会 H").EmitSignal(BaseButton.SignalName.Pressed);
        await VerifyMapFrameFill();
        await VerifySettingsReset();
        GD.Print("PASS focused native UI: continuous character-tab borders, eight mouse switches, opaque skill effect/cooldown tooltip, single bag/guild frames and filled map corners; no combat or restart suite.");
        await Client.Quit();
    }

    private async Task VerifySettingsReset()
    {
        stage = "settings reset confirmation, cancellation, failure and persistence";
        var path = Environment.GetEnvironmentVariable("MIR2_CLIENT_SETTINGS")!;
        var panel = SettingsPanel();
        var tabs = panel.GetChildren().OfType<TabContainer>().Single(); tabs.CurrentTab = 2;
        if (panel.FindChild("SingleSkill", true, false) != null || panel.FindChild("AreaSkill", true, false) != null) throw new Exception("挂机页还保留单体/群攻技能选择。");
        var pickup = Setting(panel, "自动拣取"); var names = (TextEdit)panel.FindChild("PickupNames", true, false); var mode = (OptionButton)panel.FindChild("PickupMode", true, false);
        if (pickup.ButtonPressed || names.GetParent<Control>().Visible) throw new Exception("自动拣取默认开启或关闭后仍显示名单。");
        pickup.ButtonPressed = true; names.Text = "鲜鹿肉\n金币"; SelectSetting(mode, 1);
        await Until(() => System.Text.Json.JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(path)) is { AutoPickup: true, PickupWhitelist: true, PickupNames.Length: 2 });
        await Screenshot("pickup-settings"); CloseSettingsPanel(panel); panel = SettingsPanel();
        if (!pickup.ButtonPressed || !names.GetParent<Control>().Visible || names.Text != "鲜鹿肉\n金币" || mode.Selected != 1) throw new Exception("拣取名单没有保存或重开丢失。");
        pickup.ButtonPressed = false; tabs.CurrentTab = 3;
        if (!Setting(panel, "智能组合").ButtonPressed) throw new Exception("默认职业智能组合未开启。");
        var manual = (Control)panel.FindChild("ManualClassSkills", true, false);
        if (manual.Visible) throw new Exception("启用智能组合后仍显示其他技能辅助。");
        Setting(panel, "智能组合").ButtonPressed = false;
        if (!manual.Visible) throw new Exception("关闭智能组合没有恢复手动辅助。");
        Setting(panel, "智能组合").ButtonPressed = true;
        await Screenshot("class-settings"); tabs.CurrentTab = 0;
        var volume = panel.FindChildren("*", "HSlider", true, false).Cast<HSlider>().Single(); volume.Value = 37;
        Setting(panel, "CRT").ButtonPressed = true;
        await Until(() => File.Exists(path) && System.Text.Json.JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(path))?.Volume == 37);
        var reset = panel.GetChildren().OfType<Button>().Single(button => button.Text == "重置");
        var dialog = (ConfirmationDialog)Client.FindChild("ResetSettingsConfirmation", true, false);
        var saved = File.ReadAllText(path);
        reset.EmitSignal(BaseButton.SignalName.Pressed); await Screenshot("settings-reset-confirmation");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide();
        if (File.ReadAllText(path) != saved || volume.Value != 37) throw new Exception("取消重置仍修改了游戏设置。");
        var backup = path + ".reset-check"; File.Move(path, backup); Directory.CreateDirectory(path);
        try
        {
            reset.EmitSignal(BaseButton.SignalName.Pressed); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide();
            if (volume.Value != 37 || File.ReadAllText(backup) != saved || !panel.FindChildren("*", "Label", true, false).Cast<Label>().Any(label => label.Text.StartsWith("保存失败")))
                throw new Exception("重置保存失败后丢失旧设置或没有错误提示。");
        }
        finally { Directory.Delete(path); File.Move(backup, path); }
        reset.EmitSignal(BaseButton.SignalName.Pressed); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide();
        var defaults = System.Text.Json.JsonSerializer.Serialize(new ClientPreferences());
        if (File.ReadAllText(path) != defaults || volume.Value != 100 || Setting(panel, "CRT").ButtonPressed || !Setting(panel, "智能组合").ButtonPressed)
            throw new Exception("确认重置没有恢复完整默认值并保存。");
        CloseSettingsPanel(panel); panel = SettingsPanel();
        if (volume.Value != 100) throw new Exception("重开设置丢失重置结果。");
        await Screenshot("settings-reset-defaults"); CloseSettingsPanel(panel);
        GD.Print("PASS native settings reset: no battle skill selectors, class combo, cancel/failure preservation, confirmed defaults, atomic save and reopen.");
    }

    private async Task EnterUiFixture()
    {
        await Until(() => Client.HandshakeReady);
        Client.Login("NativeSmoke", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(character => character.Name == "NativeWarrior").Index });
        await Until(() => State.User != null && !Client.World.Loading && Client.MiniMap.Revision.Length == 64);
    }
}
