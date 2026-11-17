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
        var submit = Client.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button is not OriginalButton && button.Text == "创建角色");
        var form = submit.GetParent<Control>(); var name = form.GetChildren().OfType<LineEdit>().Single();
        var jobs = form.GetChildren().OfType<OptionButton>().Single(option => option.ItemCount == 5);
        var gender = form.GetChildren().OfType<OptionButton>().Single(option => option.ItemCount == 2);
        if (!Enumerable.Range(0, 5).Select(jobs.GetItemText).SequenceEqual(Enum.GetValues<MirClass>().Select(role => role.ToLocalizedString())))
            throw new Exception("创建职业名称或编号不匹配。");
        foreach (var (role, sex) in new[] { (MirClass.Assassin, MirGender.Male), (MirClass.Archer, MirGender.Female) })
        {
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
        GD.Print("PASS native class selection: actual Assassin/Archer creation, sex-correct original 16-frame preview, delete confirmation/cancel, duplicate/timeout lock and authoritative list update.");
        await Client.Quit();
    }
}
