using Godot;
using Client.MirObjects;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyClassSelectionOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("建角删角检查必须使用隔离fixture。");
        stage = "five-class creation, original preview and confirmed deletion";
        await Until(() => Client.HandshakeReady);
        var account = "Class" + Environment.TickCount64 % 1000000;
        Client.Register(account, "Native123"); await Packet<S.LoginSuccess>();
        await VerifySelectionFrames();
        await Screenshot("selection-empty-frames");
        var submit = Client.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button is not OriginalButton && button.Text == "创建角色");
        var form = submit.GetParent<Control>(); var name = form.GetChildren().OfType<LineEdit>().Single();
        var jobs = form.GetChildren().OfType<OptionButton>().Single(option => option.ItemCount == 5);
        var gender = form.GetChildren().OfType<OptionButton>().Single(option => option.ItemCount == 2);
        if (!Enumerable.Range(0, 5).Select(jobs.GetItemText).SequenceEqual(Enum.GetValues<MirClass>().Select(role => role.ToLocalizedString())))
            throw new Exception("创建职业名称或编号不匹配。");
        foreach (var (role, sex) in new[] { (MirClass.Assassin, MirGender.Male), (MirClass.Archer, MirGender.Female), (MirClass.Warrior, MirGender.Male), (MirClass.Wizard, MirGender.Female), (MirClass.Taoist, MirGender.Male) })
        {
            stage = "original five role creation: " + role;
            if (!form.Visible) GameArt("创建角色").EmitSignal(BaseButton.SignalName.Pressed);
            jobs.Select((int)role); gender.Select((int)sex); name.Text = account + (int)role;
            await ClickSelectionControl(submit);
            var created = await Packet<S.NewCharacterSuccess>();
            if (created.CharInfo.Class != role || created.CharInfo.Gender != sex) throw new Exception("原TCP返回的职业/性别错误。");
            var preview = Client.FindChildren("*", "Control", true, false).OfType<OriginalArt>().Single(art => art.Page == "select");
            var start = OriginalPlayerArt.SelectionStart(role, sex);
            await Until(() => preview.PreviewReady && preview.PreviewFrame >= start && preview.PreviewFrame < start + 16);
            await CombatScreenshot("selection-" + role);
        }
        if (State.Characters.Count != 5) throw new Exception("未能实际创建五个角色。");
        var slots = Client.FindChildren("CharacterSlot*", "Control", true, false).OfType<Control>().OrderBy(slot => slot.Name.ToString()).ToArray();
        if (slots.Length != 5 || slots.Any(slot => slot.Size.Y != 78) || slots.Zip(slots.Skip(1)).Any(pair => pair.Second.GlobalPosition.Y - pair.First.GlobalPosition.Y != 78))
            throw new Exception("选角未保持五个等高等距位置。");
        foreach (var index in new[] { 0, 2, 4, 1, 3, 4 })
        {
            var choices = Client.FindChildren("CharacterChoice", "Button", true, false).Cast<Button>().ToArray();
            await ClickSelectionControl(choices[index]);
            await Until(() => Client.FindChildren("CharacterChoice", "Button", true, false).Cast<Button>().Count(b => b.ButtonPressed) == 1);
            choices = Client.FindChildren("CharacterChoice", "Button", true, false).Cast<Button>().ToArray();
            var preview = Client.FindChildren("*", "Control", true, false).OfType<OriginalArt>().Single(art => art.Page == "select");
            if (!choices[index].ButtonPressed || choices[index].Size != new Vector2(288, 54) || preview.Character?.Index != State.Characters[index].Index ||
                choices[index].GetThemeStylebox("pressed") is not StyleBoxFlat { BorderWidthLeft: 1, BorderWidthTop: 1, BgColor.A: > .2f })
                throw new Exception("鼠标选角没有唯一持久高亮或人物预览未同步。");
            await MoveMouse(choices[(index + 1) % choices.Length].GetGlobalRect().GetCenter());
            if (!choices[index].ButtonPressed || choices[(index + 1) % choices.Length].ButtonPressed) throw new Exception("悬停改变了当前选中角色。");
        }
        GameArt("创建角色").EmitSignal(BaseButton.SignalName.Pressed);
        if (form.Visible || !((Label)Client.FindChild("SelectionNotice", true, false)).Text.Contains("最多可创建5个")) throw new Exception("第五角色后仍允许继续创建。");
        Client.Send(new C.NewCharacter { Name = account + "Six", Class = MirClass.Warrior });
        if ((await Packet<S.NewCharacter>()).Result is not (0 or 4) || State.Characters.Count != 5) throw new Exception("服务端未拒绝第六角色。");
        await Screenshot("selection-five-slots");
        var count = State.Characters.Count; var selected = State.Characters.Last();
        var delete = (Button)Client.FindChild("DeleteCharacter", true, false);
        var dialog = (ConfirmationDialog)Client.FindChild("DeleteCharacterDialog", true, false);
        await ClickSelectionControl(delete); await Until(() => dialog.Visible);
        if (!dialog.DialogText.Contains(selected.Name)) throw new Exception("删角确认没有绑定当前角色。");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide();
        if (State.Characters.Count != count) throw new Exception("取消删角修改了角色列表。");
        await ClickSelectionControl(delete); await Until(() => dialog.Visible);
        packets.Clear(); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide();
        Client.ProcessCharacterCreation(Environment.TickCount64 + 10001);
        if (!delete.Disabled || !((Label)Client.FindChild("SelectionNotice", true, false)).Text.Contains("超时")) throw new Exception("删角未决没有锁定/超时保护。");
        var deleted = await Packet<S.DeleteCharacterSuccess>();
        if (deleted.CharacterIndex != selected.Index || State.Characters.Count != count - 1 || State.Characters.Any(character => character.Index == selected.Index))
            throw new Exception("原删角回执没有移除指定角色。");
        await CombatScreenshot("selection-deleted");
        GD.Print("PASS native class selection: five complete empty frames, actual five-class creation, unique persistent mouse selection/hover and original preview, limit/deletion/timeout authority.");
        await Client.Quit();
    }

    private async Task VerifySelectionFrames()
    {
        await Until(() => Client.Assets.Sprite("Prguse", 65) != null);
        await Until(() => Client.FindChildren("*", "TextureRect", true, false).OfType<TextureRect>()
            .Any(rect => rect.Texture != null && rect.Texture.GetSize() == new Vector2(1024, 768) && rect.IsVisibleInTree()));
        var form = Client.FindChildren("*", "TextureRect", true, false).OfType<TextureRect>()
            .Single(rect => rect.Texture != null && rect.Texture.GetSize() == new Vector2(1024, 768) && rect.IsVisibleInTree());
        using var skin = form.Texture.GetImage();
        using var original = Client.Assets.Sprite("Prguse", 65)!.Texture.GetImage();
        for (var row = 0; row < 5; row++)
        foreach (var point in new[] { new Vector2I(0, 0), new(100, 0), new(286, 0), new(0, 26), new(0, 53), new(100, 53), new(286, 53) })
            if (skin.GetPixel(637 + point.X, 194 + row * 78 + point.Y) != original.GetPixel(636 + point.X, 193 + point.Y))
                throw new Exception($"空角色位{row + 1}的顶边、左边或底边仍有裁切缺口。");
        if (Client.FindChildren("CharacterChoice", "Button", true, false).Any()) throw new Exception("空角色位出现选中效果。");
    }
}
