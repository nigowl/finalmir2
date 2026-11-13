using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private Control SkillRow(Spell spell) => (Control)Client.FindChild($"Skill{spell}", true, false);
    private void OpenSkills() => Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>()
        .Single(button => button.TooltipText == "技能").EmitSignal(BaseButton.SignalName.Pressed);

    private async Task VerifySkillControls()
    {
        stage = "skill passive controls, proficiency, shortcuts and original toggle persistence";
        foreach (var spell in new[] { Spell.Thrusting, Spell.Fencing })
        {
            var book = State.User!.Inventory.Single(item => item?.Info is { Type: ItemType.Book } info && info.Shape == (short)spell);
            Client.UseItem(book.UniqueID); if (!(await Packet<S.UseItem>()).Success) throw new Exception("测试开关/被动技能学习失败。");
            await Packet<S.NewMagic>();
        }
        OpenSkills();
        var row = SkillRow(Spell.FireBall);
        var cast = row.GetChildren().OfType<SkillIconButton>().Single();
        var skill = State.User.Magics.Single(magic => magic.Spell == Spell.FireBall);
        if (!row.GetChildren().OfType<Label>().Any(label => label.TooltipText == ClientSkillRules.ExperienceText(skill) && !label.Text.Contains('\n') && label.Text.Contains("0/200")))
            throw new Exception("技能等级/熟练度没有在同一行显示当前经验和升级要求。");
        await Until(() => Client.Assets.Sprite("MagIcon2", skill.Icon * 2) != null);
        if (cast.Position.X != 0 || row.GetChildren().OfType<Label>().First().Position.X < cast.GetRect().End.X || row.Size.Y > 70)
            throw new Exception("技能图标/名称/熟练度没有按紧凑两行布局。");
        if (row.GetParent().FindChildren("*", "CheckBox", true, false).Count > 0)
            throw new Exception("技能列表仍保留自动释放复选框。");
        foreach (var magic in State.User.Magics.Where(magic => ClientSkillRules.IsPassive(magic.Spell)))
            if (SkillRow(magic.Spell).GetChildren().OfType<OptionButton>().Any() || !SkillRow(magic.Spell).GetChildren().OfType<SkillIconButton>().Single().Disabled)
                throw new Exception("被动技能仍显示快捷键设置或允许点击施放。");
        var selector = row.GetChildren().OfType<OptionButton>().Single();
        SelectSetting(selector, selector.GetItemIndex(16));
        if (skill.Key != 16) throw new Exception("技能快捷键没有更新。");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        row = SkillRow(Spell.FireBall);
        var choice = row.GetChildren().OfType<OptionButton>().Single();
        if (choice.ItemCount != 21 || choice.GetItemText(choice.GetItemIndex(16)) != "Shift+F8" || choice.GetItemText(choice.GetItemIndex(28)) != "F12" || choice.GetRect().End.X > 224 || choice.Size.X > 54)
            throw new Exception($"技能快捷键选项被裁切或丢失：选项={choice.ItemCount}，右边界={choice.GetRect().End.X}。");
        SelectSetting(choice, choice.GetItemIndex(28));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var shortcuts = Client.FindChildren("*", "Button", true, false).OfType<SkillIconButton>().Where(button => button.Name.ToString().StartsWith("SkillShortcut")).ToArray();
        var last = shortcuts.Single(button => button.Name == "SkillShortcut12");
        if (shortcuts.Length != 12 || last.Magic != skill || shortcuts.Any(button => !button.ShowSlotFrame || button.Size != new Vector2(32, 32) || button.Position.Y != 629 || button.GetRect().End.X > 859))
            throw new Exception("物品栏右侧未显示12格技能栏，或技能图标未跟随F12绑定。");
        await MoveMouse(new Vector2(650, 400)); Client.World.Selected = 0;
        packets.RemoveAll(packet => packet is S.Magic);
        using (var key = new InputEventKey { PhysicalKeycode = Key.F12, Pressed = true }) Client._UnhandledInput(key);
        if (!(await Packet<S.Magic>()).Cast) throw new Exception("F12真实TCP施放没有成功。");
        var castAt = skill.CastTime;
        last.EmitSignal(BaseButton.SignalName.Pressed);
        if (last.CooldownRemaining <= 0 || skill.CastTime != castAt) throw new Exception("技能格未显示冷却等待或允许冷却中重复施放。");
        if (Argument("--screenshot") is { Length: > 0 } shot)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Capture(Path.ChangeExtension(shot, "skill-cooldown.png"));
        }
        await Until(() => Environment.TickCount64 - skill.CastTime >= Math.Max(1800, skill.Delay));
        packets.RemoveAll(packet => packet is S.Magic); last.EmitSignal(BaseButton.SignalName.Pressed);
        if (!(await Packet<S.Magic>()).Cast) throw new Exception("技能快捷栏图标点击没有真实施放。");
        var thrust = SkillRow(Spell.Thrusting).GetChildren().OfType<SkillIconButton>().Single();
        if (State.ActiveSkills.Contains(Spell.Thrusting)) throw new Exception("未主动开启的原技能被启用。");
        thrust.EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => State.ActiveSkills.Contains(Spell.Thrusting));
        await Screenshot("skills-enabled");
        await VerifySkillServerRestore();
    }

    private async Task VerifySkillServerRestore()
    {
        var id = State.Characters.Single(character => character.Name == State.User!.Name).Index;
        await Until(() => Environment.TickCount64 - State.User!.Magics.Max(magic => magic.CastTime) > Globals.LogDelay + 100);
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = id });
        await Until(() => State.User != null && !Client.World.Loading && State.ActiveSkills.Contains(Spell.Thrusting));
        if (State.User!.Magics.Single(magic => magic.Spell == Spell.FireBall).Key != 28 || State.DisabledSkills.Count != 0)
            throw new Exception("原服务器未保留快捷键，或本地禁用状态泄漏到重登。");
        OpenSkills();
        var row = SkillRow(Spell.Thrusting);
        if (!row.GetChildren().OfType<Label>().First().TooltipText.Contains("已开启")) throw new Exception("原服务器技能开关未回显。");
        row.GetChildren().OfType<SkillIconButton>().Single().EmitSignal(BaseButton.SignalName.Pressed);
        if (State.ActiveSkills.Contains(Spell.Thrusting) || !State.DisabledSkills.Contains(Spell.Thrusting)) throw new Exception("关闭切换技能未阻止本次挂机重新开启。");
        SelectSetting(SkillRow(Spell.FireBall).GetChildren().OfType<OptionButton>().Single(), 1);
        await Screenshot("skills-shortcuts");
        Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "关闭角色").EmitSignal(BaseButton.SignalName.Pressed);
        GD.Print("PASS native skills: no auto-cast checkboxes, passive skills have no key selector, original icons/two-line rows, 12 skill cells, actual F12/icon casting and TCP shortcut/toggle persistence.");
    }
}
