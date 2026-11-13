using Crystal.Native;
using Godot;
using C = ClientPackets;

public partial class Main
{
    private readonly Dictionary<Spell, (Label name, Label experience, Button cast)> skillRows = new();

    private void RenderSkills()
    {
        Clear(skillList); skillRows.Clear();
        if (State.User == null) return;
        var hint = new Label { Text = "点击图标或按快捷键施放；自动技能在职业设置中调整。", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        hint.AddThemeFontSizeOverride("font_size", 9); skillList.AddChild(hint);
        for (var i = 0; i < State.User.Magics.Count; i++) AddSkillRow(i);
        if (State.User.Magics.Count == 0) skillList.AddChild(new Label { Text = "尚未学会技能。" });
        RefreshSkillRows();
    }

    private void AddSkillRow(int index)
    {
        var magic = State.User!.Magics[index];
        var row = new Control { Name = $"Skill{magic.Spell}", CustomMinimumSize = new Vector2(0, 56), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        skillList.AddChild(row);
        var name = new Label { Text = magic.Name, Position = new Vector2(46, 0), Size = new Vector2(176, 24),
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        row.AddChild(name);
        name.TooltipText = ClientSkillRules.IsPassive(magic.Spell) ? "被动技能按原引擎自动生效。" : ClientSkillRules.IsToggle(magic.Spell)
            ? "点击图标或按快捷键切换原技能开关。" : "点击图标或按快捷键施放；自动技能在职业设置中调整。";
        var cast = new SkillIconButton { Name = "SkillIcon", Assets = Assets, Magic = magic, GlobalCooldownUntil = () => spellTime, Position = new Vector2(0, 8), Size = new Vector2(40, 40),
            TooltipText = $"{magic.Name} · {(ClientSkillRules.IsPassive(magic.Spell) ? "被动" : ClientSkillRules.IsTrigger(magic.Spell) ? "蓄力" : "点击施放/切换")}", FocusMode = FocusModeEnum.None };
        CellStyle(cast); row.AddChild(cast); cast.Pressed += () => Cast(index);
        var experience = Text(row, "", 46, 29, 118); experience.Size = new Vector2(118, 20); experience.AddThemeFontSizeOverride("font_size", 9);
        skillRows[magic.Spell] = (name, experience, cast);
        if (ClientSkillRules.IsPassive(magic.Spell)) return;
        var key = new OptionButton { Position = new Vector2(168, 28), Size = new Vector2(54, 24), FitToLongestItem = false,
            ClipText = true, FocusMode = FocusModeEnum.None, TooltipText = $"技能快捷键 · {SkillKeys.Label(magic.Key)}" };
        key.AddThemeFontSizeOverride("font_size", 9); row.AddChild(key); key.AddItem("无", 0);
        for (var k = 1; k <= Assets.FunctionKeys; k++) key.AddItem($"F{k}", SkillKeys.FunctionKey(k));
        for (var k = 9; k <= 16; k++) key.AddItem(SkillKeys.Label((byte)k), k);
        key.Select(Math.Max(0, key.GetItemIndex(magic.Key)));
        key.ItemSelected += selected => { BindMagic(index, (byte)key.GetItemId((int)selected)); RenderSkills(); };
    }

    private void SetSkillToggle(int index, bool enabled)
    {
        if (State.User == null || index < 0 || index >= State.User.Magics.Count || !HandshakeReady || State.Me?.Dead != false) { RefreshSkillRows(); return; }
        var spell = State.User.Magics[index].Spell;
        if (!ClientSkillRules.IsToggle(spell)) return;
        if (enabled) State.DisabledSkills.Remove(spell); else State.DisabledSkills.Add(spell);
        if (ClientSkillRules.IsToggle(spell))
        {
            Send(new C.SpellToggle { Spell = spell, CanUse = enabled });
            if (enabled) State.ActiveSkills.Add(spell); else State.ActiveSkills.Remove(spell);
        }
        RefreshSkillRows();
    }

    private void RefreshSkillRows()
    {
        if (State.User == null) return;
        foreach (var magic in State.User.Magics)
        {
            if (!skillRows.TryGetValue(magic.Spell, out var row)) { if (skills.Visible) RenderSkills(); return; }
            var passive = ClientSkillRules.IsPassive(magic.Spell);
            var enabled = ClientSkillRules.IsToggle(magic.Spell) ? State.ActiveSkills.Contains(magic.Spell) : !State.DisabledSkills.Contains(magic.Spell);
            row.name.Modulate = ClientSkillRules.IsToggle(magic.Spell) && enabled ? new Color("78d888") : Colors.White;
            if (ClientSkillRules.IsToggle(magic.Spell)) row.name.TooltipText = $"{magic.Name} · {(enabled ? "已开启" : "已关闭")}；点击图标或按快捷键切换。";
            row.cast.Disabled = !ClientSkillRules.IsImplemented(magic.Spell) || passive || (!ClientSkillRules.IsToggle(magic.Spell) && !enabled) || State.Me?.Dead != false;
            if (!ClientSkillRules.IsImplemented(magic.Spell)) row.cast.TooltipText = "当前原引擎尚未实现此技能。";
            row.experience.Text = ClientSkillRules.ExperienceText(magic).Replace("熟练度 ", "");
            row.experience.TooltipText = ClientSkillRules.ExperienceText(magic);
        }
    }
}
