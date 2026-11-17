using Crystal.Native;
using Godot;

public partial class Main
{
    private Control classSettings = null!;
    private MirClass settingsClass;
    private readonly Dictionary<Spell, CheckBox> classBuffOptions = new();
    private CheckBox? swapAmulet, smartHalfMoon, smartThrusting, autoPoison, autoSummon, autoHeal, smartBattle;
    private OptionButton? summonSkill;
    private SpinBox? healPercent;

    private void FillClassSettings()
    {
        Clear(classSettings); classBuffOptions.Clear();
        swapAmulet = smartHalfMoon = smartThrusting = autoPoison = autoSummon = autoHeal = smartBattle = null; summonSkill = null; healPercent = null;
        if (State.User == null) { Text(classSettings, "进入游戏后显示当前职业的辅助选项。", 12, 12, 390); return; }
        settingsClass = State.User.Class;
        Text(classSettings, $"{settingsClass.ToLocalizedString()}职业辅助 · 学会并启用对应技能后生效", 12, 4, 390).AddThemeFontSizeOverride("font_size", 10);
        var scroll = new ScrollContainer { Position = new Vector2(0, 29), Size = new Vector2(404, 198), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var content = new Control { CustomMinimumSize = new Vector2(386, 240) }; scroll.AddChild(content); classSettings.AddChild(scroll);
        Control host = content;
        var y = 0f;
        CheckBox Toggle(string title, bool value)
        {
            var option = SettingToggle(host, title, y); option.Size = new Vector2(382, 28); option.SetPressedNoSignal(value); y += 33; return option;
        }
        smartBattle = Toggle("智能组合（手动打怪与挂机共用）", preferences.SmartBattle);
        var manual = new Control { Name = "ManualClassSkills", Position = new Vector2(0, 33) }; content.AddChild(manual); host = manual; y = 0;
        var summary = Text(content, settingsClass switch {
            MirClass.Warrior => "单怪优先攻杀/烈火/双龙，群怪按命中范围用半月。\n保留手动刺杀开关，增益按实际状态补充。",
            MirClass.Wizard => "先补魔法盾与增益，单体主攻和范围技能自动切换。\n按蓝量、冷却及真实命中范围选择，不反复铺同一火墙。",
            MirClass.Assassin => "战斗时补充疾风步与轻身术，单怪使用双重斩。\n多怪按实际范围选择神圣剑术与弯月斩，缺蓝回退普攻。",
            MirClass.Archer => "战斗时补充元素屏障与凝神，按需聚集护盾元素。\n单怪优先双重射击/直线射击，群怪切范围箭，低血优先吸血箭。",
            _ => "自疗/净化优先，其次召唤、补红绿毒，再补增益。\n已有状态不重复施放，随后用火符/毒云攻击。"
        }, 12, 40, 370); summary.AutowrapMode = TextServer.AutowrapMode.WordSmart; summary.Size = new Vector2(370, 100);
        if (settingsClass == MirClass.Warrior)
        {
            smartHalfMoon = Toggle("智能半月（命中范围内多怪时使用）", preferences.SmartHalfMoon);
            smartThrusting = Toggle("智能刺杀（隔一格攻击目标）", preferences.SmartThrusting);
        }
        if (settingsClass == MirClass.Taoist)
            swapAmulet = Toggle("自动毒符切换（按技能需要换装）", preferences.AutoSwapAmulet);
        if (settingsClass == MirClass.Taoist) autoPoison = Toggle("自动施毒（战斗时补齐红毒、绿毒）", preferences.AutoPoison);
        if (settingsClass == MirClass.Wizard) autoSummon = Toggle("自动分身（视野内无存活召唤物时）", preferences.AutoMirroring);
        if (settingsClass == MirClass.Taoist)
        {
            autoSummon = Toggle("自动召唤（视野内无存活召唤兽时）", preferences.AutoSummon);
            Text(manual, "召唤技能", 12, y, 80);
            summonSkill = new OptionButton { Name = "ClassSummonSkill", Position = new Vector2(100, y), Size = new Vector2(277, 28) };
            summonSkill.AddItem("自动选择已学且就绪的技能", (int)Spell.None);
            foreach (var spell in ClassSkillRules.Summons(settingsClass)) summonSkill.AddItem(spell.ToLocalizedString(), (int)spell);
            summonSkill.Select(Math.Max(0, summonSkill.GetItemIndex((int)preferences.SummonSkill)));
            summonSkill.ItemSelected += _ => SettingsChanged(); manual.AddChild(summonSkill); y += 36;
        }
        foreach (var spell in ClassSkillRules.Maintained(settingsClass))
            classBuffOptions[spell] = Toggle($"自动{spell.ToLocalizedString()}{(spell == Spell.Purification ? "（自身异常时）" : spell == Spell.PetEnhancer ? "（召唤物）" : "")}", preferences.AutoSkills.Contains(spell));
        if (settingsClass == MirClass.Taoist)
        {
            autoHeal = Toggle("自动治愈（生命不足时自疗）", preferences.AutoHeal);
            Text(manual, "自疗生命≤", 12, y, 100).Size = new Vector2(100, 28);
            healPercent = SettingPercent(manual, 120, y, 125); healPercent.Value = preferences.HealPercent; y += 36;
        }
        Text(manual, "耗蓝、耗材及权限以原服为准；拒绝/超时暂停，不重发。", 12, y + 4, 370).AddThemeFontSizeOverride("font_size", 10);
        void Visibility()
        {
            manual.Visible = !smartBattle.ButtonPressed; summary.Visible = smartBattle.ButtonPressed;
            content.CustomMinimumSize = new Vector2(386, smartBattle.ButtonPressed ? 160 : y + 93);
        }
        smartBattle.Toggled += _ => Visibility(); Visibility();
    }

    private void ReadClassSettings(ClientPreferences next)
    {
        next.SmartBattle = smartBattle?.ButtonPressed ?? preferences.SmartBattle;
        next.AutoSwapAmulet = swapAmulet?.ButtonPressed ?? preferences.AutoSwapAmulet;
        next.SmartHalfMoon = smartHalfMoon?.ButtonPressed ?? preferences.SmartHalfMoon;
        next.SmartThrusting = smartThrusting?.ButtonPressed ?? preferences.SmartThrusting;
        next.AutoPoison = autoPoison?.ButtonPressed ?? preferences.AutoPoison;
        next.AutoSummon = settingsClass == MirClass.Taoist && autoSummon != null ? autoSummon.ButtonPressed : preferences.AutoSummon;
        next.AutoMirroring = settingsClass == MirClass.Wizard && autoSummon != null ? autoSummon.ButtonPressed : preferences.AutoMirroring;
        next.SummonSkill = settingsClass == MirClass.Taoist && summonSkill != null ? (Spell)summonSkill.GetSelectedId() : preferences.SummonSkill;
        next.AutoHeal = autoHeal?.ButtonPressed ?? preferences.AutoHeal;
        next.HealPercent = healPercent == null ? preferences.HealPercent : (int)healPercent.Value;
        next.AutoSkills = new(preferences.AutoSkills);
        foreach (var (spell, option) in classBuffOptions) { if (option.ButtonPressed) next.AutoSkills.Add(spell); else next.AutoSkills.Remove(spell); }
    }
}
