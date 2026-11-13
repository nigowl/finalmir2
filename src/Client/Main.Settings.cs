using System.Text.Json;
using Crystal.Native;
using Godot;

public partial class Main
{
    private ClientPreferences preferences = new();
    private Control settingsDialog = null!;
    private TabContainer settingsTabs = null!;
    private HSlider volumeSlider = null!;
    private Label volumeValue = null!, settingsHint = null!;
    private CheckBox npcNames = null!, monsterNames = null!, autoHp = null!, autoMp = null!, autoRandom = null!, autoTown = null!, noMonsterRandom = null!, autoRoam = null!;
    private CheckBox crtFilter = null!;
    private CheckBox itemNames = null!;
    private OptionButton crtStrength = null!;
    private OptionButton movementMode = null!, singleSkill = null!, areaSkill = null!;
    private SpinBox hpPercent = null!, mpPercent = null!, randomPercent = null!, townPercent = null!;
    private Godot.Timer settingsSaveTimer = null!;
    private bool loadingSettings;
    private Texture2D checkedIcon = null!, uncheckedIcon = null!;
    private string settingsFile = "";

    private void BuildSettings()
    {
        settingsFile = Environment.GetEnvironmentVariable("MIR2_CLIENT_SETTINGS") ?? ProjectSettings.GlobalizePath("user://settings.json");
        var error = "";
        try
        {
            if (File.Exists(settingsFile))
            {
                if (new FileInfo(settingsFile).Length > 8192) throw new InvalidDataException("设置文件超过 8KB。");
                preferences = JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(settingsFile)) ?? throw new InvalidDataException("设置文件为空。");
                preferences.Normalize();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { error = "读取设置失败，已使用默认值：" + e.Message; Log(error); }
        ApplyPreferences();
        checkedIcon = CheckIcon(true); uncheckedIcon = CheckIcon(false);
        settingsDialog = Panel(game, 292, 153, 440, 350); settingsDialog.Hide();
        Text(settingsDialog, "游戏设置", 16, 5, 380);
        ArtButton(settingsDialog, "Prguse2", 360, 413, 3, "关闭设置", CloseSettings);
        settingsTabs = new TabContainer { Position = new Vector2(10, 32), Size = new Vector2(420, 260) }; settingsDialog.AddChild(settingsTabs);
        var basic = SettingsPage("基础"); var protection = SettingsPage("保护"); var battle = SettingsPage("挂机");
        classSettings = SettingsPage("职业");
        Text(basic, "音量", 12, 14, 70);
        volumeSlider = new HSlider { Position = new Vector2(85, 15), Size = new Vector2(235, 24), MinValue = 0, MaxValue = 100, Step = 1 }; basic.AddChild(volumeSlider);
        volumeValue = Text(basic, "", 335, 13, 60);
        volumeSlider.ValueChanged += value => { volumeValue.Text = value == 0 ? "静音" : $"{value:0}%"; ApplyVolume((int)value); SettingsChanged(); };
        npcNames = SettingToggle(basic, "显示 NPC 名称", 45);
        monsterNames = SettingToggle(basic, "显示怪物名称（关闭后仅悬停/选中显示）", 78);
        itemNames = SettingToggle(basic, "显示物品名称（地面；关闭后悬停仍显示）", 111);
        Text(basic, "鼠标走跑", 12, 150, 80);
        movementMode = new OptionButton { Name = "MovementMode", Position = new Vector2(96, 148), Size = new Vector2(294, 28) };
        movementMode.AddItem("左键行走 / 右键奔跑"); movementMode.AddItem("左键奔跑 / 右键行走"); basic.AddChild(movementMode);
        movementMode.ItemSelected += _ => SettingsChanged();
        crtFilter = SettingToggle(basic, "CRT 滤镜", 184);
        crtFilter.Size = new Vector2(140, 28);
        crtStrength = new OptionButton { Name = "CrtStrength", Position = new Vector2(213, 184), Size = new Vector2(177, 28) };
        for (var level = 1; level <= 5; level++) crtStrength.AddItem($"强度 {level} 级");
        basic.AddChild(crtStrength); crtStrength.ItemSelected += _ => SettingsChanged();
        autoHp = ProtectionRow(protection, "自动喝红", 12, out hpPercent);
        autoMp = ProtectionRow(protection, "自动喝蓝", 51, out mpPercent);
        autoRandom = ProtectionRow(protection, "自动随机", 90, out randomPercent);
        autoTown = ProtectionRow(protection, "自动回城", 129, out townPercent);
        Text(protection, "随机/回城使用背包卷轴；回城优先，同次低血量仅一次。\n拒绝或超时后暂停，重新开启保护或挂机可恢复。", 12, 176, 390).AddThemeFontSizeOverride("font_size", 10);
        noMonsterRandom = SettingToggle(battle, "无怪随机（等待 3 秒，消耗随机卷）", 12);
        autoRoam = SettingToggle(battle, "无怪跑图（当前地图；无随机卷时也可跑图）", 48);
        singleSkill = SkillChoice(battle, "单体技能", 98); areaSkill = SkillChoice(battle, "群攻技能", 141);
        Text(battle, "群怪优先群攻；缺蓝/技能冷却时改用单体或普攻。\nEsc或菜单停止；点击地面需确认，界面操作不停止。", 12, 182, 390).AddThemeFontSizeOverride("font_size", 10);
        settingsHint = Text(settingsDialog, error.Length > 0 ? error : "更改后自动保存。", 26, 317, 388);
        settingsHint.Size = new Vector2(388, 26); settingsHint.AutowrapMode = TextServer.AutowrapMode.WordSmart; settingsHint.AddThemeFontSizeOverride("font_size", 10);
        settingsSaveTimer = new Godot.Timer { OneShot = true, WaitTime = .2 }; settingsDialog.AddChild(settingsSaveTimer);
        settingsSaveTimer.Timeout += () => SaveSettings();
        ArtButton(game, "Prguse", 1912, 997, 692, "游戏设置", OpenSettings);
        Draggable(settingsDialog);
    }

    private Control SettingsPage(string title)
    {
        var page = new Control { Name = title }; settingsTabs.AddChild(page); return page;
    }

    private CheckBox SettingToggle(Control parent, string text, float y)
    {
        var checkbox = new CheckBox { Text = text, Position = new Vector2(8, y), Size = new Vector2(390, 28), FocusMode = FocusModeEnum.None };
        StyleCheckBox(checkbox);
        checkbox.Toggled += _ => SettingsChanged(); parent.AddChild(checkbox); return checkbox;
    }

    private void StyleCheckBox(CheckBox checkbox)
    {
        checkbox.AddThemeIconOverride("checked", checkedIcon); checkbox.AddThemeIconOverride("unchecked", uncheckedIcon);
        checkbox.AddThemeIconOverride("checked_disabled", checkedIcon); checkbox.AddThemeIconOverride("unchecked_disabled", uncheckedIcon);
        // 各状态使用相同内边距和图标尺寸，避免勾选时文字横移。
        var style = new StyleBoxFlat { BgColor = Colors.Transparent, ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2 };
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" }) checkbox.AddThemeStyleboxOverride(state, style);
    }

    private static Texture2D CheckIcon(bool selected)
    {
        using var image = new Image();
        var tick = selected ? "<path d='M4 8l3 3 5-6' fill='none' stroke='#e2d29e' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/>" : "";
        image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='1' y='1' width='14' height='14' rx='2' fill='#171610' stroke='#9b8962'/>" + tick + "</svg>");
        return ImageTexture.CreateFromImage(image);
    }

    private CheckBox ProtectionRow(Control parent, string title, float y, out SpinBox percent)
    {
        var toggle = SettingToggle(parent, title, y); toggle.Size = new Vector2(140, 28);
        Text(parent, title == "自动喝蓝" ? "魔力≤" : "生命≤", 158, y, 50);
        percent = new SpinBox { Position = new Vector2(213, y), Size = new Vector2(100, 28), MinValue = 1, MaxValue = 95, Step = 1, Suffix = "%" };
        percent.ValueChanged += _ => SettingsChanged(); parent.AddChild(percent); return toggle;
    }

    private OptionButton SkillChoice(Control parent, string label, float y)
    {
        Text(parent, label, 12, y, 80);
        var choice = new OptionButton { Name = label == "单体技能" ? "SingleSkill" : "AreaSkill", Position = new Vector2(100, y), Size = new Vector2(288, 28) };
        choice.ItemSelected += _ => SettingsChanged(); parent.AddChild(choice); return choice;
    }

    private void FillSkills(OptionButton choice, Spell selected, bool area)
    {
        choice.Clear(); choice.AddItem(area ? "不使用群攻技能" : "普通攻击", (int)Spell.None);
        foreach (var magic in State.User?.Magics ?? [])
            if (area ? ClientAutomation.IsAreaSkill(magic.Spell) : ClientAutomation.IsSingleSkill(magic.Spell)) choice.AddItem(magic.Name, (int)magic.Spell);
        choice.Select(Math.Max(0, choice.GetItemIndex((int)selected)));
    }

    private void OpenSettings()
    {
        loadingSettings = true;
        volumeSlider.Value = preferences.Volume; volumeValue.Text = preferences.Volume == 0 ? "静音" : $"{preferences.Volume}%";
        movementMode.Select(preferences.DefaultRun ? 1 : 0); npcNames.ButtonPressed = preferences.ShowNpcNames; monsterNames.ButtonPressed = preferences.ShowMonsterNames; itemNames.ButtonPressed = preferences.ShowItemNames;
        crtFilter.ButtonPressed = preferences.CrtFilter;
        crtStrength.Select(preferences.CrtStrength - 1);
        autoHp.ButtonPressed = preferences.AutoHp; hpPercent.Value = preferences.HpPercent; autoMp.ButtonPressed = preferences.AutoMp; mpPercent.Value = preferences.MpPercent;
        autoRandom.ButtonPressed = preferences.AutoRandom; randomPercent.Value = preferences.RandomPercent; autoTown.ButtonPressed = preferences.AutoTown; townPercent.Value = preferences.TownPercent;
        noMonsterRandom.ButtonPressed = preferences.NoMonsterRandom; autoRoam.ButtonPressed = preferences.AutoRoam;
        FillSkills(singleSkill, preferences.SingleSkill, false); FillSkills(areaSkill, preferences.AreaSkill, true);
        FillClassSettings();
        loadingSettings = false; settingsDialog.Show(); settingsDialog.MoveToFront();
    }

    private void SettingsChanged()
    {
        if (loadingSettings || settingsSaveTimer == null) return;
        settingsHint.Text = "正在自动保存…"; settingsSaveTimer.Start();
    }

    private void CloseSettings()
    {
        if (settingsSaveTimer != null && !settingsSaveTimer.IsStopped() && !SaveSettings()) return;
        ApplyVolume(preferences.Volume); settingsDialog.Hide();
    }

    private bool SaveSettings()
    {
        settingsSaveTimer.Stop();
        var next = new ClientPreferences { Volume = (int)volumeSlider.Value, DefaultRun = movementMode.Selected == 1,
            ShowNpcNames = npcNames.ButtonPressed, ShowMonsterNames = monsterNames.ButtonPressed, ShowItemNames = itemNames.ButtonPressed, CrtFilter = crtFilter.ButtonPressed, CrtStrength = crtStrength.Selected + 1,
            AutoHp = autoHp.ButtonPressed, HpPercent = (int)hpPercent.Value, AutoMp = autoMp.ButtonPressed, MpPercent = (int)mpPercent.Value,
            AutoRandom = autoRandom.ButtonPressed, RandomPercent = (int)randomPercent.Value, AutoTown = autoTown.ButtonPressed, TownPercent = (int)townPercent.Value,
            NoMonsterRandom = noMonsterRandom.ButtonPressed, AutoRoam = autoRoam.ButtonPressed,
            SingleSkill = (Spell)singleSkill.GetSelectedId(), AreaSkill = (Spell)areaSkill.GetSelectedId() };
        ReadClassSettings(next); next.Normalize(); var temporary = settingsFile + "." + Guid.NewGuid() + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsFile))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(next)); File.Move(temporary, settingsFile, true);
            if (next.AutoHp && !preferences.AutoHp || next.AutoMp && !preferences.AutoMp || next.AutoRandom && !preferences.AutoRandom || next.AutoTown && !preferences.AutoTown) automation.Resume();
            preferences = next;
            if (settingsTabs.CurrentTab == 3 && skillEquipmentPending == 0) { skillPreparationPaused = false; classSkillsPaused = false; }
            ApplyPreferences(); settingsHint.Text = "已自动保存。"; return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ApplyVolume(preferences.Volume); settingsHint.Text = "保存失败，更改尚未生效：" + error.Message; return false; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void ApplyPreferences()
    {
        ApplyVolume(preferences.Volume);
        World.ShowNpcNames = preferences.ShowNpcNames; World.ShowMonsterNames = preferences.ShowMonsterNames; World.ShowItemNames = preferences.ShowItemNames;
        crtOverlay.Visible = preferences.CrtFilter;
        ((ShaderMaterial)crtOverlay.Material).SetShaderParameter("strength", preferences.CrtStrength);
    }

    private void ApplyVolume(int volume)
    {
        var db = volume == 0 ? -80 : Mathf.LinearToDb(volume / 100f);
        music.VolumeDb = -20 + db;
        foreach (var player in effectPlayers) player.VolumeDb = -12 + db;
    }
}
