using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task ClickSelectionControl(Control control)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var point = control.GetGlobalRect().GetCenter();
        await MoveMouse(point);
        MouseButtonAt(point, true); MouseButtonAt(point, false);
    }

    private async Task VerifyCharacterCreation(string name)
    {
        stage = "visible character creation validation and server acknowledgements";
        var submit = Client.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button is not OriginalButton && button.Text == "创建角色");
        var form = submit.GetParent<Control>();
        var input = form.GetChildren().OfType<LineEdit>().Single();
        var jobs = form.GetChildren().OfType<OptionButton>().Single(option => option.ItemCount == 5);
        var controls = form.GetChildren().OfType<Control>().Where(control => control is LineEdit or OptionButton or Button).ToArray();
        if (controls.Any(control => Math.Abs(control.GetRect().GetCenter().Y - input.GetRect().GetCenter().Y) > .5f))
            throw new Exception("建角名称、职业、性别及操作按钮的水平中心线不一致：" + string.Join(" / ", controls.Select(control => control.GetRect())));
        if (!Enumerable.Range(0, jobs.ItemCount).Select(jobs.GetItemText).SequenceEqual(new[] { "战士", "法师", "道士", "刺客", "弓箭手" }))
            throw new Exception("创建角色入口没有完整五职业。");
        var notice = (Label)Client.FindChild("SelectionNotice", true, false);
        if (!form.IsVisibleInTree() || !notice.IsVisibleInTree()) throw new Exception("选角页没有显示建角表单和反馈区域。");
        using (var background = ((StyleBoxTexture)form.GetThemeStylebox("panel")).Texture.GetImage())
            if (background.GetData().Where((_, index) => index % 4 == 3).Any(alpha => alpha != 255))
                throw new Exception("新建角色框的角区或右边线存在透明空洞。");
        foreach (var invalid in new[] { "", "ab", "bad!" })
        {
            input.Text = invalid; await ClickSelectionControl(submit);
            if (!notice.Text.Contains("3–15位") || submit.Disabled || State.Characters.Count != 0)
                throw new Exception("非法角色名没有立即可见的校验反馈。");
        }
        input.Text = " " + name + " "; await ClickSelectionControl(submit);
        if (!submit.Disabled || input.Editable || !notice.Text.Contains("正在创建")) throw new Exception("建角请求未显示提交中或阻止重复提交。");
        submit.EmitSignal(BaseButton.SignalName.Pressed);
        Client.ProcessCharacterCreation(Environment.TickCount64 + 10000);
        if (!notice.Text.Contains("超时") || !submit.Disabled) throw new Exception("超时未提示核对结果或放开了重复提交。");
        var created = await Packet<S.NewCharacterSuccess>();
        if (created.CharInfo.Name != name || form.IsVisibleInTree() || !notice.Text.Contains("创建成功") || input.Text.Length != 0)
            throw new Exception("鼠标点击没有完成真实TCP建角或回执未关闭表单。");
        await VerifyCharacterCreationRejection(name, submit, form, input, notice);
        await VerifyCharacterCreationKeyboard(name, form, input, notice);
        GD.Print("PASS native character creation: actual mouse/Enter, local validation, pending/duplicate/timeout protection, visible server errors, corrected retry, cancel and new-character selection.");
    }

    private async Task VerifyCharacterCreationRejection(string name, Button submit, Control form, LineEdit input, Label notice)
    {
        var open = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "创建角色");
        await ClickSelectionControl(open); input.Text = name;
        await ClickSelectionControl(submit);
        if ((await Packet<S.NewCharacter>()).Result != 5 || !notice.IsVisibleInTree() || !notice.Text.Contains("已被使用") || !form.IsVisibleInTree() || submit.Disabled || !input.Editable)
            throw new Exception("真实服务端重名拒绝仍然没有可见说明或无法改名重试。");
        await Screenshot("character-name-rejected");
        foreach (var (code, expected) in new (byte, string)[] { (0, "暂不允许"), (1, "保留名称"), (2, "性别无效"), (3, "未开放"), (4, "数量上限"), (255, "代码 255") })
        {
            Client.Connection.Received.Enqueue(new S.NewCharacter { Result = code });
            await Until(() => notice.Text.Contains(expected));
            if (!notice.IsVisibleInTree() || submit.Disabled) throw new Exception("建角失败回执没有在当前页面恢复操作。");
        }
        var before = State.Characters.Count;
        await ClickSelectionControl(form.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "取消"));
        if (form.IsVisibleInTree() || State.Characters.Count != before) throw new Exception("取消建角修改了角色列表。");
        await ClickSelectionControl(open);
        if (notice.Text.Length != 0) throw new Exception("重新打开建角仍显示旧错误。");
    }

    private async Task VerifyCharacterCreationKeyboard(string primary, Control form, LineEdit input, Label notice)
    {
        var choices = form.GetChildren().OfType<OptionButton>().OrderBy(choice => choice.Position.X).ToArray();
        choices[0].Select((int)MirClass.Wizard); choices[1].Select((int)MirGender.Female);
        input.Text = "小号测试"; input.GrabFocus();
        foreach (var pressed in new[] { true, false })
        {
            using var key = new InputEventKey { PhysicalKeycode = Key.Enter, Keycode = Key.Enter, Pressed = pressed };
            GetViewport().PushInput(key, true);
        }
        var created = await Packet<S.NewCharacterSuccess>();
        var preview = Client.FindChildren("*", "Control", true, false).OfType<OriginalArt>().Single(art => art.Page == "select");
        if (created.CharInfo.Name != "小号测试" || created.CharInfo.Class != MirClass.Wizard || created.CharInfo.Gender != MirGender.Female || preview.Character?.Index != created.CharInfo.Index)
            throw new Exception("回车建角丢失中文/职业/性别或未选中新角色。");
        await Screenshot("character-created");
        if (!notice.GetGlobalRect().HasArea() || notice.GetGlobalRect().End.Y > 736) throw new Exception("选角提示遮挡了底部操作。");
        var row = Client.FindChildren("*", "Label", true, false).OfType<Label>().Single(label => label.Text == primary).GetParent<Control>();
        packets.RemoveAll(packet => packet is S.CharacterLocation);
        await ClickSelectionControl(row.GetChildren().OfType<Button>().Single());
    }
}
