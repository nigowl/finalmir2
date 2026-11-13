using Crystal.Native;
using Godot;

public partial class Main
{
    private Control classSettings = null!;
    private MirClass settingsClass;
    private readonly Dictionary<Spell, CheckBox> classBuffOptions = new();
    private CheckBox? swapAmulet, smartHalfMoon, smartThrusting, autoPoison, autoSummon, autoHeal;
    private OptionButton? summonSkill;
    private SpinBox? healPercent;

    private void FillClassSettings()
    {
        Clear(classSettings); classBuffOptions.Clear();
        swapAmulet = smartHalfMoon = smartThrusting = autoPoison = autoSummon = autoHeal = null; summonSkill = null; healPercent = null;
        if (State.User == null) { Text(classSettings, "进入游戏后显示当前职业的辅助选项。", 12, 12, 390); return; }
        if (State.User.Class is not (MirClass.Warrior or MirClass.Wizard or MirClass.Taoist))
        { Text(classSettings, "职业辅助仅开放战士、法师、道士。", 12, 12, 390); return; }
        settingsClass = State.User.Class;
        Text(classSettings, $"{settingsClass.ToLocalizedString()}职业辅助 · 学会并启用对应技能后生效", 12, 4, 390).AddThemeFontSizeOverride("font_size", 10);
        var scroll = new ScrollContainer { Position = new Vector2(0, 29), Size = new Vector2(404, 198), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var content = new Control { CustomMinimumSize = new Vector2(386, 240) }; scroll.AddChild(content); classSettings.AddChild(scroll);
        var y = 0f;
        CheckBox Toggle(string title, bool value)
        {
            var option = SettingToggle(content, title, y); option.Size = new Vector2(382, 28); option.SetPressedNoSignal(value); y += 33; return option;
        }
        if (settingsClass == MirClass.Warrior)
        {
            smartHalfMoon = Toggle("智能半月（命中范围内多怪时使用）", preferences.SmartHalfMoon);
            smartThrusting = Toggle("智能刺杀（隔一格攻击目标）", preferences.SmartThrusting);
        }
        if (settingsClass == MirClass.Taoist)
            swapAmulet = Toggle("自动毒符切换（按技能需要换装）", preferences.AutoSwapAmulet);
        if (settingsClass == MirClass.Taoist) autoPoison = Toggle("挂机自动施毒（补齐红毒、绿毒）", preferences.AutoPoison);
        if (settingsClass == MirClass.Taoist)
        {
            autoSummon = Toggle("自动召唤（视野内无存活召唤兽时）", preferences.AutoSummon);
            Text(content, "召唤技能", 12, y, 80);
            summonSkill = new OptionButton { Name = "ClassSummonSkill", Position = new Vector2(100, y), Size = new Vector2(277, 28) };
            summonSkill.AddItem("自动选择已学且就绪的技能", (int)Spell.None);
            foreach (var spell in ClassSkillRules.Summons(settingsClass)) summonSkill.AddItem(spell.ToLocalizedString(), (int)spell);
            summonSkill.Select(Math.Max(0, summonSkill.GetItemIndex((int)preferences.SummonSkill)));
            summonSkill.ItemSelected += _ => SettingsChanged(); content.AddChild(summonSkill); y += 36;
        }
        foreach (var spell in ClassSkillRules.Maintained(settingsClass))
            classBuffOptions[spell] = Toggle($"自动{spell.ToLocalizedString()}", preferences.AutoSkills.Contains(spell));
        if (settingsClass == MirClass.Taoist)
        {
            autoHeal = Toggle("自动治愈（生命不足时自疗）", preferences.AutoHeal);
            Text(content, "自疗生命≤", 12, y, 100);
            healPercent = new SpinBox { Position = new Vector2(120, y), Size = new Vector2(125, 28), MinValue = 1, MaxValue = 95, Step = 1, Suffix = "%", Value = preferences.HealPercent };
            healPercent.ValueChanged += _ => SettingsChanged(); content.AddChild(healPercent); y += 36;
        }
        Text(content, "自动动作遵守耗蓝、耗材、冷却和原服权限；失败后暂停。\n交易、仓库、NPC交互时暂缓；各项默认关闭。", 12, y + 4, 370).AddThemeFontSizeOverride("font_size", 10);
        content.CustomMinimumSize = new Vector2(386, y + 60);
    }

    private void ReadClassSettings(ClientPreferences next)
    {
        next.AutoSwapAmulet = swapAmulet?.ButtonPressed ?? preferences.AutoSwapAmulet;
        next.SmartHalfMoon = smartHalfMoon?.ButtonPressed ?? preferences.SmartHalfMoon;
        next.SmartThrusting = smartThrusting?.ButtonPressed ?? preferences.SmartThrusting;
        next.AutoPoison = autoPoison?.ButtonPressed ?? preferences.AutoPoison;
        next.AutoSummon = settingsClass == MirClass.Taoist && autoSummon != null ? autoSummon.ButtonPressed : preferences.AutoSummon;
        next.SummonSkill = settingsClass == MirClass.Taoist && summonSkill != null ? (Spell)summonSkill.GetSelectedId() : preferences.SummonSkill;
        next.AutoHeal = autoHeal?.ButtonPressed ?? preferences.AutoHeal;
        next.HealPercent = healPercent == null ? preferences.HealPercent : (int)healPercent.Value;
        next.AutoSkills = new(preferences.AutoSkills);
        foreach (var (spell, option) in classBuffOptions) { if (option.ButtonPressed) next.AutoSkills.Add(spell); else next.AutoSkills.Remove(spell); }
    }
}
