using System.Text.Json;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyHealthDisplay()
    {
        var path = Environment.GetEnvironmentVariable("MIR2_CLIENT_SETTINGS") ?? "";
        if (!path.Contains("mir2-health.", StringComparison.Ordinal)) throw new InvalidOperationException("血量验证必须使用隔离设置。");
        stage = "血量原TCP登录";
        await Until(() => Client.HandshakeReady);
        Client.Login("NativeSkills", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        if (OS.GetCmdlineUserArgs().Contains("--verify-health-save"))
        {
            await Until(() => State.Me!.CurrentHealth != null);
            var saved = JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(path))!;
            if (!Client.World.ShowHealthBars || saved.SoundEnabled || saved.Volume != 40 || !saved.DefaultRun || !AudioServer.IsBusMute(0))
                throw new Exception("重启没有保留血条、静音、原音量和走跑互换。");
            GD.Print("PASS native health/settings after full restart: exact HP, all bars, mute with remembered volume and walk/run checkbox.");
            await Client.Quit(); return;
        }
        await VerifyHealthSettings(path);
        await VerifyChatDisplay();
        stage = "所有可见生命对象血条、独立名称开关及HUD";
        await Until(() => State.Actors.Values.Where(a => a.Kind is ObjectType.Player or ObjectType.Monster && !a.Dead).All(a => a.CurrentHealth != null));
        var target = State.Actors.Values.Single(a => a.Name == "普通血量靶");
        await VerifyHeadHealthBars();
        if (!Client.World.HealthBarVisible(State.Me!) || !Client.World.HealthBarVisible(target) || WorldView.HealthLabel(target) != "1023/1023" ||
            Client.FindChild("PlayerVitals", true, false) != null || Client.World.ShowMonsterNames)
            throw new Exception("全体血条依赖名称/选中，血量为猜测，或左下血魔文字未移除。");
        GameArt("背包 I").EmitSignal(BaseButton.SignalName.Pressed);
        var bag = GameArt("关闭背包").GetParent<Control>();
        if (bag.GetNode<Label>("WindowTitle").Text != "背包") throw new Exception("背包标题仍显示容量。");
        bag.Hide(); await Screenshot("health-bars");
        var initial = target.CurrentHealth!.Value;
        packets.Clear(); Client.Attack(MirDirection.Right, Spell.None);
        await Until(() => packets.OfType<S.DamageIndicator>().Any(p => p.ObjectID == target.Id && p.Damage < 0));
        var hit = packets.OfType<S.DamageIndicator>().Single(p => p.ObjectID == target.Id && p.Type == DamageType.Hit);
        var actual = packets.OfType<S.ObjectVitals>().First(p => p.ObjectID == target.Id && p.HP < initial);
        if (hit.Damage != actual.HP - initial || hit.Damage != -80 || !Client.World.CombatTexts.Any(t => t.Actor == target.Id && t.Text == "80"))
            throw new Exception($"普通攻击飘字与真实扣血不一致：{initial}->{actual.HP}, {hit.Damage}。");
        await Until(() => Client.World.CombatTextsDrawn > 0);
        await CombatScreenshot("health-real-hit");
        await VerifyGuardHealth();
        GD.Print("PASS native health display: shared bag title, no orb text, real settings clicks/persistence, hidden names with all exact bars, real 80HP attack and guard overkill capped to actual HP.");
        await Client.Quit();
    }

    private async Task VerifyHealthSettings(string path)
    {
        stage = "基础设置复选框、静音和CRT实际几何";
        var panel = SettingsPanel(); var tabs = panel.FindChildren("*", "TabContainer", true, false).Cast<TabContainer>().Single(); tabs.CurrentTab = 0;
        var sound = Setting(panel, "音量"); var health = Setting(panel, "显示血条"); var movement = Setting(panel, "走跑互换");
        var slider = panel.FindChildren("*", "HSlider", true, false).Cast<HSlider>().Single();
        var curvature = (OptionButton)panel.FindChild("CrtCurvature", true, false); var strength = (OptionButton)panel.FindChild("CrtStrength", true, false);
        await Until(() => curvature.GetThemeStylebox("normal") is StyleBoxTexture);
        if (panel.Size != new Vector2(440, 350) || curvature.Size.X != 128 || strength.Size.X != 128 ||
            strength.Position.X - curvature.GetRect().End.X != 10 || strength.GetRect().End.X > 390 ||
            Setting(panel, "CRT").GetRect().End.X > curvature.Position.X)
            throw new Exception($"CRT参数宽度/间距或窗口尺寸不符：{curvature.GetRect()} / {strength.GetRect()}。");
        slider.Value = 40;
        await ClickSelectionControl(sound); await ClickSelectionControl(health); await ClickSelectionControl(movement);
        await Until(() => File.Exists(path) && JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(path)) is { SoundEnabled: false, ShowHealthBars: true, DefaultRun: true });
        if (!AudioServer.IsBusMute(0) || slider.Editable || slider.Value != 40) throw new Exception("取消音量后未静音或丢失原音量。");
        CloseSettingsPanel(panel); panel = SettingsPanel();
        if (sound.ButtonPressed || !health.ButtonPressed || !movement.ButtonPressed || slider.Value != 40) throw new Exception("重新打开基础设置值不符。");
        await ClickSelectionControl(sound);
        await Until(() => !AudioServer.IsBusMute(0));
        if (slider.Value != 40 || !slider.Editable) throw new Exception("启用音量未恢复原值。");
        await Screenshot("health-basic-settings");
        await ClickSelectionControl(curvature);
        var popup = curvature.GetPopup();
        await Until(() => popup.Visible);
        if (popup.ThemeTypeVariation != "GameOptionPopup" || !popup.TransparentBg ||
            popup.GetThemeStylebox("panel") is not StyleBoxFlat { BgColor.A: > .5f and < 1, BorderWidthLeft: 1 })
            throw new Exception("选项下拉仍为花边框或非半透明背景。");
        await Screenshot("option-popup"); popup.Hide();
        await ClickSelectionControl(sound);
        CloseSettingsPanel(panel);
    }

    private async Task VerifyGuardHealth()
    {
        stage = "护卫原AI秒杀与实际剩余HP飘字";
        var npc = State.Actors.Values.Single(a => a.Kind == ObjectType.Merchant);
        packets.Clear(); Client.SelectTarget(npc.Id); await Packet<S.NPCResponse>();
        var link = Client.FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().Single(b => b.Text == "护卫秒杀验证");
        await ClickSelectionControl(link);
        await Until(() => packets.OfType<S.ObjectMonster>().Any(p => p.Name == "护卫秒杀靶"));
        var id = packets.OfType<S.ObjectMonster>().First(p => p.Name == "护卫秒杀靶").ObjectID;
        await Until(() => packets.OfType<S.DamageIndicator>().Any(p => p.ObjectID == id && p.Damage < 0));
        var hit = packets.OfType<S.DamageIndicator>().Single(p => p.ObjectID == id && p.Type == DamageType.Hit);
        var before = packets.OfType<S.ObjectVitals>().First(p => p.ObjectID == id && p.HP > 0);
        if (before.HP != 80 || hit.Damage != -before.HP || !packets.OfType<S.ObjectVitals>().Any(p => p.ObjectID == id && p.HP == 0) ||
            !Client.World.CombatTexts.Any(t => t.Actor == id && t.Text == "80")) throw new Exception("护卫秒杀没有按原80HP显示或实际伤害未落地。");
        await Until(() => Client.World.CombatTextsDrawn > 0);
        await CombatScreenshot("health-guard-kill");
    }

    private async Task VerifyHeadHealthBars()
    {
        var pixels = new byte[12 * 12 * 4];
        void Dot(int x, int y) { var i = (y * 12 + x) * 4; pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 255; }
        for (var y = 0; y < 12; y++) Dot(0, y);
        for (var y = 4; y < 12; y++) for (var x = 4; x <= 9; x++) Dot(x, y);
        if (ResourceCache.MeasureHeadAnchor(pixels, 12, 12, new Vector2(6, 7)) != new Vector2(6, 4) || WorldView.HealthBarWidth != 48)
            throw new Exception("血条顶点仍被长武器拉偏或没有缩短到48像素。");
        foreach (var actor in State.Actors.Values.Where(a => a.Kind is ObjectType.Player or ObjectType.Monster))
        {
            await Until(() => Client.World.NameBody(actor) != null);
            var now = Environment.TickCount64; var body = Client.World.NameBody(actor)!;
            var head = Client.World.Screen(actor.Location) + body.Offset + body.HeadAnchor;
            var bar = Client.World.HealthBarAnchor(actor, now);
            if (bar.Y + 5 > head.Y - 3 || Math.Abs(bar.X - head.X) > 1)
                throw new Exception("真实人物/怪物血条没有位于头顶上方或偏离身体中心。");
        }
    }
}
