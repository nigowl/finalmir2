using Crystal.Native;
using Godot;

public partial class Main
{
    private readonly List<SkillIconButton> skillBar = new();
    private Control quickBars = null!;
    private void BuildSkillBar()
    {
        for (var number = 1; number <= 12; number++)
        {
            var key = SkillKeys.FunctionKey(number);
            var button = new SkillIconButton { Name = $"SkillShortcut{number}", Assets = Assets, Position = OriginalArt.QuickSlotPosition(number + 5),
                Size = new Vector2(32, 32), ShowSlotFrame = true, FocusMode = FocusModeEnum.None };
            CellStyle(button); quickBars.AddChild(button); skillBar.Add(button);
            var label = Text(button, $"F{number}", 1, 0, 28); label.AddThemeFontSizeOverride("font_size", 9); label.Size = new Vector2(28, 14);
            label.AddThemeColorOverride("font_shadow_color", Colors.Black); label.AddThemeConstantOverride("shadow_offset_x", 1); label.AddThemeConstantOverride("shadow_offset_y", 1);
            button.Pressed += () => { var index = State.User?.Magics.FindIndex(magic => magic.Key == key) ?? -1; if (index >= 0) Cast(index); };
        }
    }

    private void RefreshSkillBar()
    {
        for (var i = 0; i < skillBar.Count; i++)
        {
            var button = skillBar[i]; var key = SkillKeys.FunctionKey(i + 1);
            var magic = i < Assets.FunctionKeys ? State.User?.Magics.FirstOrDefault(magic => magic.Key == key) : null;
            button.Magic = magic;
            button.TooltipText = i >= Assets.FunctionKeys ? "当前服务端未启用 F9–F12" : magic == null ? $"{SkillKeys.Label(key)} · 在技能页分配技能" : $"{ClientSkillRules.Tooltip(magic, State.ActiveSkills.Contains(magic.Spell), State.PlayerTiming)}\n快捷键：{SkillKeys.Label(key)}";
            button.Disabled = magic == null || !ClientSkillRules.IsImplemented(magic.Spell) || ClientSkillRules.IsPassive(magic.Spell) || State.Me?.Dead != false ||
                !ClientSkillRules.IsToggle(magic.Spell) && State.DisabledSkills.Contains(magic.Spell);
        }
    }
}
