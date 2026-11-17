using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private static LineEdit AccountInput(ConfirmationDialog dialog, string name) => (LineEdit)dialog.FindChild(name, true, false);
    private static Label AccountFeedback(ConfirmationDialog dialog) => dialog.FindChildren("*", "Label", true, false).OfType<Label>().Single(label => label.Name.ToString().EndsWith("Notice"));
    private static void AccountSubmit(ConfirmationDialog dialog) => dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
    private static void AccountKey(Key key)
    {
        foreach (var pressed in new[] { true, false })
        {
            using var input = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed };
            Input.ParseInputEvent(input);
        }
        Input.FlushBufferedEvents();
    }

    private async Task VerifyAccountForms()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-accounts-", StringComparison.Ordinal))
            throw new InvalidOperationException("账号表单检查必须使用专用隔离版本。");
        await Until(() => Client.HandshakeReady);
        await VerifyLoginPresentation();
        var entry = GameArt("打开账号注册"); var login = entry.GetParent().GetParent<Control>();
        var loginFields = entry.GetParent().GetChildren().OfType<LineEdit>().ToArray();
        var outsideAccount = loginFields.Single(field => !field.Secret); var outsidePassword = loginFields.Single(field => field.Secret);
        outsideAccount.Text = "Outside"; outsidePassword.Text = "Outside123";
        var register = (ConfirmationDialog)Client.FindChild("RegistrationDialog", true, false);
        var change = (ConfirmationDialog)Client.FindChild("PasswordChangeDialog", true, false);
        var openPassword = login.GetChildren().OfType<Button>().Single(button => button.Text == "修改登录密码");
        await ClickSelectionControl(entry); await CheckAccountGeometry(register, 2);
        stage = "independent registration validation and modal focus";
        AccountInput(register, "RegisterAccount").Text = "NativeAccount";
        AccountInput(register, "RegisterPassword").Text = "Native123";
        AccountInput(register, "RegisterRepeat").Text = "Different123";
        AccountSubmit(register);
        if (!AccountFeedback(register).Text.Contains("不一致") || register.GetOkButton().Disabled) throw new Exception("注册密码确认没有在弹窗内校验。");
        AccountInput(register, "RegisterRepeat").Text = "Native123"; AccountInput(register, "RegisterEmail").Text = "bad@"; AccountSubmit(register);
        if (!AccountFeedback(register).Text.Contains("邮箱格式")) throw new Exception("注册邮箱没有校验。");
        AccountInput(register, "RegisterAccount").Text = "ab"; AccountSubmit(register);
        if (!AccountFeedback(register).Text.Contains("3–15")) throw new Exception("账号长度没有在弹窗内校验。");
        var connect = login.GetChildren().OfType<Button>().Single(button => button.Text == "连接");
        await ClickSelectionControl(connect);
        if (!Client.HandshakeReady || !register.Visible || outsideAccount.Text != "Outside" || outsidePassword.Text != "Outside123")
            throw new Exception("注册弹窗没有阻挡底层操作，或提交借用了登录框字段。");
        register.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        if (register.Visible || AccountInput(register, "RegisterPassword").Text.Length > 0) throw new Exception("取消注册未关闭或未清除密码。");

        await ClickSelectionControl(entry);
        AccountInput(register, "RegisterAccount").Text = "NativeAccount"; AccountInput(register, "RegisterPassword").Text = "Native123";
        AccountInput(register, "RegisterRepeat").Text = "Native123"; AccountInput(register, "RegisterEmail").Text = "native@example.com";
        await Screenshot("account-registration"); packets.Clear(); AccountSubmit(register); AccountSubmit(register);
        Client.ProcessAccountRequests(Environment.TickCount64 + 10001);
        if (!register.GetOkButton().Disabled || AccountInput(register, "RegisterAccount").Editable || !AccountFeedback(register).Text.Contains("超时"))
            throw new Exception("注册提交/超时后没有阻止重复请求。");
        await Packet<S.LoginSuccess>();
        if (packets.OfType<S.NewAccount>().Count() != 1 || packets.OfType<S.NewAccount>().Single().Result != 8 || register.Visible ||
            AccountInput(register, "RegisterPassword").Text.Length > 0 || outsideAccount.Text != "NativeAccount" || State.User != null)
            throw new Exception("独立注册未成功登录到选角、重复注册或密码未清除。");

        await Client.Connect(Argument("--server")); await Until(() => Client.HandshakeReady);
        outsideAccount.Text = "Outside"; outsidePassword.Text = "Outside123";
        await ClickSelectionControl(entry);
        AccountInput(register, "RegisterAccount").Text = "NativeAccount"; AccountInput(register, "RegisterPassword").Text = AccountInput(register, "RegisterRepeat").Text = "Native123";
        packets.Clear(); AccountSubmit(register);
        if ((await Packet<S.NewAccount>()).Result != 7 || !register.Visible || !AccountFeedback(register).Text.Contains("已存在") || register.GetOkButton().Disabled)
            throw new Exception("重复账号的原服拒绝没有在注册弹窗内恢复操作。");
        await Screenshot("account-registration-rejected"); AccountKey(Key.Escape);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (register.Visible || AccountInput(register, "RegisterPassword").Text.Length > 0) throw new Exception("Esc没有关闭注册并清除密码。");

        await ClickSelectionControl(openPassword); await CheckAccountGeometry(change, 3);
        await VerifyPasswordForm(change, outsideAccount, outsidePassword);
        await Client.Connect(Argument("--server")); await Until(() => Client.HandshakeReady);
        await ClickSelectionControl(entry);
        AccountInput(register, "RegisterAccount").Text = "NativeClosed"; AccountInput(register, "RegisterPassword").Text = AccountInput(register, "RegisterRepeat").Text = "Native123";
        packets.Clear(); AccountSubmit(register); register.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => packets.OfType<S.NewAccount>().Any());
        if (packets.OfType<S.NewAccount>().Single().Result != 8 || packets.OfType<S.LoginSuccess>().Any() || register.Visible || outsidePassword.Text.Length > 0)
            throw new Exception("关闭后迟到的注册结果未释放锁，或擅自自动登录。");
        await ClickSelectionControl(openPassword);
        if (change.GetOkButton().Disabled || !AccountInput(change, "CurrentPassword").Editable) throw new Exception("迟到结果没有释放表单锁。");
        AccountInput(change, "CurrentPassword").Text = "Native123"; change.EmitSignal(Window.SignalName.CloseRequested);
        if (change.Visible || AccountInput(change, "CurrentPassword").Text.Length > 0) throw new Exception("右上关闭未清除当前密码。");
        Client.Connection.Received.Enqueue(new S.Login { Result = 5 });
        await Until(() => change.Visible);
        if (!AccountFeedback(change).Text.Contains("要求修改密码") || AccountInput(change, "CurrentPassword").Text.Length > 0)
            throw new Exception("强制改密没有打开独立表单，或借用了登录框密码。");
        AccountInput(change, "CurrentPassword").Text = "Native123";
        Client.Connection.Received.Enqueue(new S.Disconnect { Reason = 0 });
        await Until(() => !Client.HandshakeReady);
        if (change.Visible || AccountInput(change, "CurrentPassword").Text.Length > 0 || change.GetOkButton().Disabled)
            throw new Exception("断线没有关闭表单、清除密码和释放旧请求状态。");
        GD.Print("PASS native account forms: independent modal fields, original skins/alignment, local validation, cancel/Esc/close, pending/timeout/late reply, duplicate account, wrong current password, actual registration/change/login and no game entry.");
        await Client.Quit();
    }

    private async Task CheckAccountGeometry(ConfirmationDialog dialog, int secretCount)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Screenshot(dialog.Name.ToString());
        var fields = dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().ToArray();
        if (!dialog.Visible || !dialog.Exclusive || !dialog.Transient || fields.Length != 4 || fields.Count(field => field.Secret) != secretCount ||
            dialog.GetThemeStylebox("panel", "AcceptDialog") is not StyleBoxEmpty || dialog.GetThemeStylebox("embedded_border", "Window") is not StyleBoxTexture)
            throw new Exception("账号弹窗没有完整独立字段、密码遮掩或原版单层边框。");
        foreach (var field in fields)
        {
            var label = field.GetParent().GetChildren().OfType<Label>().Single();
            if (Math.Abs(field.GetGlobalRect().GetCenter().Y - label.GetGlobalRect().GetCenter().Y) > .5f || field.Size.Y != 28 ||
                field.GetGlobalRect().End.X > dialog.Size.X || field.GetGlobalRect().End.Y > dialog.GetOkButton().GetGlobalRect().Position.Y ||
                field.GetThemeFontSize("font_size") != ClientUi.Body)
                throw new Exception($"账号字段未对齐或溢出：{field.Name}={field.GetGlobalRect()}，window={dialog.Size}。");
        }
        if (fields.Select(field => field.GetGlobalRect().Position.X).Distinct().Count() != 1 ||
            dialog.GetOkButton().Size.Y != ClientUi.ButtonHeight || dialog.GetCancelButton().Size.Y != ClientUi.ButtonHeight)
            throw new Exception($"账号输入列或提交/取消按钮没有对齐：{string.Join(';', fields.Select(field => field.GetGlobalRect()))}；ok={dialog.GetOkButton().GetRect()}，cancel={dialog.GetCancelButton().GetRect()}。");
    }

    private async Task VerifyPasswordForm(ConfirmationDialog change, LineEdit outsideAccount, LineEdit outsidePassword)
    {
        stage = "independent password form and original password acknowledgements";
        AccountInput(change, "PasswordAccount").Text = "NativeAccount";
        AccountInput(change, "CurrentPassword").Text = "Native123";
        AccountInput(change, "NewPassword").Text = "Native456"; AccountInput(change, "ConfirmPassword").Text = "Native789";
        AccountSubmit(change);
        if (!AccountFeedback(change).Text.Contains("不一致")) throw new Exception("修改密码没有校验确认字段。");
        AccountInput(change, "NewPassword").Text = AccountInput(change, "ConfirmPassword").Text = "Native123"; AccountSubmit(change);
        if (!AccountFeedback(change).Text.Contains("不能与当前密码相同")) throw new Exception("没有拒绝相同新旧密码。");
        AccountInput(change, "NewPassword").Text = AccountInput(change, "ConfirmPassword").Text = "Native456";
        AccountInput(change, "PasswordAccount").Text = "NoSuchAccount"; packets.Clear(); AccountSubmit(change);
        if ((await Packet<S.ChangePassword>()).Result != 4 || !AccountFeedback(change).Text.Contains("不存在")) throw new Exception("不存在账号的反馈不准确。");
        AccountInput(change, "PasswordAccount").Text = "NativeAccount"; AccountInput(change, "CurrentPassword").Text = "Wrong123";
        packets.Clear(); AccountSubmit(change);
        if ((await Packet<S.ChangePassword>()).Result != 5 || !change.Visible || !AccountFeedback(change).Text.Contains("当前密码错误") || change.GetOkButton().Disabled)
            throw new Exception("当前密码错误没有在独立弹窗内提示并恢复操作。");
        await Screenshot("account-password-rejected");
        AccountInput(change, "CurrentPassword").Text = "Native123"; await Screenshot("account-password-change");
        packets.Clear(); AccountInput(change, "ConfirmPassword").GrabFocus(); AccountKey(Key.Enter);
        if (!change.GetOkButton().Disabled) throw new Exception("回车没有提交独立修改密码表单。");
        Client.ProcessAccountRequests(Environment.TickCount64 + 10001);
        if (!AccountFeedback(change).Text.Contains("超时") || !change.GetOkButton().Disabled) throw new Exception("改密超时放开了重复请求。");
        await Until(() => packets.OfType<S.ChangePassword>().Any());
        if (packets.OfType<S.ChangePassword>().Count() != 1 || packets.OfType<S.ChangePassword>().Single().Result != 6 || change.Visible ||
            AccountInput(change, "NewPassword").Text.Length > 0 || outsideAccount.Text != "NativeAccount" || outsidePassword.Text != "Native456")
            throw new Exception("原服改密失败、重复提交或未使用弹窗自己的账号与当前密码。");
        packets.Clear(); Client.Login("NativeAccount", "Native123");
        if ((await Packet<S.Login>()).Result != 4) throw new Exception("改密后旧密码仍然有效。");
        packets.Clear(); Client.Login("NativeAccount", "Native456"); await Packet<S.LoginSuccess>();
        if (State.User != null) throw new Exception("改密登录自动进入游戏。");
    }
}
