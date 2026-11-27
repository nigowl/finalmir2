using Godot;
using System.Reflection;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyHeroPortrait()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-heroes.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("英雄头像检查仅允许隔离版本。");
        await Until(() => Client.HandshakeReady); Client.Login("TradeRight", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.Hero != null && State.User != null && !Client.World.Loading);
        var portrait = (HeroPortraitButton)Client.FindChild("HeroShortcut", true, false);
        var controls = new[] { "HeroShortcut", "SummonHero", "HeroPortraitMode" }
            .Select(name => (Control)Client.FindChild(name, true, false)).ToArray();
        var hero = State.Hero!; var user = State.User!; var originalClass = hero.Class; var originalGender = hero.Gender;
        var refresh = typeof(Main).GetMethod("RefreshCompanionPortraits", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Refresh() => refresh.Invoke(Client, null);
        string Snapshot() => JsonSerializer.Serialize(new { user.RealId, user.Gold, State.ActiveHero!.Index,
            Bag = user.Inventory.Select(item => item == null ? "-" : $"{item.UniqueID}:{item.ItemIndex}:{item.Count}"),
            Equipment = hero.Equipment.Select(item => item?.UniqueID ?? 0) });
        var before = Snapshot();
        if (!controls.Select(control => control.Position.Y).SequenceEqual(new float[] { 516, 600, 600 }))
            throw new Exception("圆形英雄区域改变了原布局位置。");
        using (var synthetic = Image.CreateEmpty(17, 17, false, Image.Format.Rgba8))
        {
            synthetic.Fill(Colors.Black);
            for (var y = 4; y <= 12; y++) for (var x = 4; x <= 12; x++) synthetic.SetPixel(x, y, new Color("b99977"));
            for (var y = 7; y <= 9; y++) for (var x = 7; x <= 9; x++) synthetic.SetPixel(x, y, Colors.Black);
            using var shaped = HeroPortraitButton.RoundPortrait(synthetic);
            if (shaped.GetPixel(0, 0).A != 0 || shaped.GetPixel(30, 30).A != 1 || shaped.GetPixel(30, 30).R > .01f ||
                shaped.GetPixel(30, 4).G < .05f || synthetic.GetWidth() != 17 || synthetic.GetPixel(8, 8) != Colors.Black)
                throw new Exception("圆形裁切、外缘黑底替换或内部暗部保护不正确。");
        }
        stage = "all original class and gender portraits";
        try
        {
            foreach (var gender in Enum.GetValues<MirGender>())
            foreach (var job in Enum.GetValues<MirClass>())
            {
                hero.Class = job; hero.Gender = gender; Refresh();
                var frame = 1400 + (int)job + 10 * (int)gender;
                await Until(() => portrait.Portrait != null && portrait.Portrait == Client.Assets.Sprite("Prguse", frame)?.Texture && portrait.PortraitReady);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                using var face = portrait.FaceTexture!.GetImage();
                if (face.GetWidth() != 60 || face.GetPixel(0, 0).A != 0 || face.GetPixel(59, 59).A != 0 || face.GetPixel(30, 30).A < .99f ||
                    portrait._HasPoint(Vector2.Zero) || !portrait._HasPoint(portrait.Size / 2))
                    throw new Exception($"{job}/{gender}头像未正确圆形显示或命中范围仍是方框。");
            }
            foreach (var gender in Enum.GetValues<MirGender>())
            {
                hero.Class = MirClass.Wizard; hero.Gender = gender; Refresh();
                await Until(() => portrait.Portrait == Client.Assets.Sprite("Prguse", 1401 + 10 * (int)gender)?.Texture);
                await MoveMouse(new Vector2(700, 420)); await Screenshot("hero-circle-" + gender);
            }
            user.HasHero = false; Refresh();
            if (controls.Any(control => control.Visible) || portrait.Portrait != null) throw new Exception("没有英雄时圆头像或控件仍显示。");
        }
        finally { hero.Class = originalClass; hero.Gender = originalGender; user.HasHero = true; Refresh(); }
        await VerifyHeroVitalRings(portrait);
        if (portrait.Health != hero.HP || portrait.Mana != hero.MP || portrait.MaximumHealth != State.CalculateHeroStats()[Stat.HP] ||
            portrait.MaximumMana != State.CalculateHeroStats()[Stat.MP] || !portrait.TooltipText.Contains($"{portrait.Mana}/{portrait.MaximumMana}"))
            throw new Exception("头像血蓝环没有同步真实英雄生命、魔法及上限提示。");
        stage = "original hero click, bag context, mode and summon actions";
        var bag = GameArt("关闭背包").GetParent<Control>(); bag.Hide();
        await ClickSelectionControl(portrait); var equipment = GameArt("关闭角色").GetParent<Control>();
        await Until(() => equipment.Visible);
        if (bag.Visible) throw new Exception("点击圆头像自动打开了背包。");
        await ClickSelectionControl(GameArt("关闭角色"));
        var mode = (OptionButton)controls[2]; var originalMode = user.HeroBehaviour;
        var selectedMode = originalMode == HeroBehaviour.Follow ? HeroBehaviour.Attack : HeroBehaviour.Follow;
        async Task Mode(HeroBehaviour target)
        {
            await ClickSelectionControl(mode); await Until(() => mode.GetPopup().Visible);
            if (!mode.GetPopup().TransparentBg || mode.GetPopup().ThemeTypeVariation != "GameOptionPopup") throw new Exception("英雄模式下拉丢失半透明原样式。");
            mode.GetPopup().EmitSignal(PopupMenu.SignalName.IndexPressed, mode.GetItemIndex((int)target)); mode.GetPopup().Hide();
            await Until(() => user.HeroBehaviour == target);
        }
        await Mode(selectedMode); await Mode(originalMode);
        await ToggleHero("休息"); await Until(() => State.HeroState == HeroSpawnState.Unsummoned && portrait.SpawnState == HeroSpawnState.Unsummoned);
        if (portrait.Health != 0 || portrait.Mana != 0 || !mode.Disabled) throw new Exception("休息状态下血蓝圆环或模式控件未同步。");
        await MoveMouse(new Vector2(700, 420)); await Screenshot("hero-circle-resting");
        await ToggleHero("召唤"); await Until(() => State.HeroState == HeroSpawnState.Summoned && portrait.SpawnState == HeroSpawnState.Summoned);
        if (Snapshot() != before) throw new Exception("头像改动改变英雄身份、装备或共用背包。");
        await VerifyHeroHudLayout();
        GD.Print("PASS native circular hero HUD: ten original class/gender portraits, transparent corners, black-background removal with inner shadows retained, circular hit test, raised anchor/absence, original hero-window/bag/mode/rest/summon actions and asset identity.");
        await Client.Quit();
    }

    private async Task VerifyHeroVitalRings(HeroPortraitButton actual)
    {
        stage = "hero left health and right mana ring pixels";
        var probe = new HeroPortraitButton { Portrait = actual.Portrait, FrameSource = actual.FrameSource, Level = actual.Level, SpawnState = HeroSpawnState.Summoned,
            Position = actual.Position, Size = actual.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        actual.GetParent().AddChild(probe);
        try
        {
            var samples = new[] {
                (HP: 100, MaxHP: 100, MP: 100, MaxMP: 100, Filled: new[] { true, true, true, true }, Name: "full"),
                (HP: 50, MaxHP: 100, MP: 25, MaxMP: 100, Filled: new[] { false, true, false, true }, Name: "partial"),
                (HP: 0, MaxHP: 100, MP: 0, MaxMP: 100, Filled: new[] { false, false, false, false }, Name: "empty"),
                (HP: -10, MaxHP: 100, MP: 50, MaxMP: 0, Filled: new[] { false, false, false, false }, Name: "invalid"),
                (HP: 200, MaxHP: 100, MP: 200, MaxMP: 100, Filled: new[] { true, true, true, true }, Name: "overflow") };
            var angles = new[] { 5 * Mathf.Pi / 4, 3 * Mathf.Pi / 4, -Mathf.Pi / 4, Mathf.Pi / 4 };
            foreach (var sample in samples)
            {
                probe.Health = sample.HP; probe.MaximumHealth = sample.MaxHP; probe.Mana = sample.MP; probe.MaximumMana = sample.MaxMP;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var screen = GetViewport().GetTexture().GetImage(); var scale = screen.GetWidth() / 1024f;
                for (var i = 0; i < angles.Length; i++)
                {
                    var at = (probe.GetGlobalRect().GetCenter() + Vector2.FromAngle(angles[i]) * 33) * scale;
                    var pixel = screen.GetPixel((int)at.X, (int)at.Y);
                    var filled = i < 2 ? pixel.R > .65f && pixel.R > pixel.G * 1.4f : pixel.B > .65f && pixel.B > pixel.R * 1.5f;
                    if (filled != sample.Filled[i]) throw new Exception($"血蓝环比例、左右颜色或边界错误：{sample.Name}/{i}/{pixel}。");
                }
                if (sample.Name is "full" or "partial") await Screenshot("hero-rings-" + sample.Name);
            }
        }
        finally { probe.QueueFree(); }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("PASS native hero ring pixels: left red health/right blue mana with original carved-gold frame, full/half/quarter/empty, negative/zero maximum/overflow, bottom-up fill and real-stat tooltip.");
    }
}
