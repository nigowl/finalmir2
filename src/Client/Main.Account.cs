using Crystal.Native;
using Godot;

public partial class Main
{
    private ConfirmationDialog registration = null!, passwordChange = null!;
    private LineEdit registerAccount = null!, registerPassword = null!, registerRepeat = null!, registerEmail = null!;
    private LineEdit passwordAccount = null!, currentLoginPassword = null!, newLoginPassword = null!, repeatLoginPassword = null!;
    private Label registrationNotice = null!, passwordNotice = null!;

    private void BuildAccountDialogs()
    {
        registration = AccountDialog("注册账号", "RegistrationDialog", "注册并登录", out var registerFields);
        registerAccount = AccountField(registerFields, "游戏账号", "RegisterAccount", "3–15位英文字母或数字", Globals.MaxAccountIDLength);
        registerPassword = AccountField(registerFields, "登录密码", "RegisterPassword", "5–15位英文字母或数字", Globals.MaxPasswordLength, true);
        registerRepeat = AccountField(registerFields, "确认密码", "RegisterRepeat", "请再次输入登录密码", Globals.MaxPasswordLength, true);
        registerEmail = AccountField(registerFields, "电子邮箱", "RegisterEmail", "选填，用于记录账号联系邮箱", 50);
        registrationNotice = AccountNotice(registerFields, "RegistrationNotice");
        registration.Confirmed += SubmitRegistration;
        foreach (var field in new[] { registerAccount, registerPassword, registerRepeat, registerEmail }) field.TextSubmitted += _ => SubmitRegistration();

        passwordChange = AccountDialog("修改登录密码", "PasswordChangeDialog", "确认修改", out var passwordFields);
        passwordAccount = AccountField(passwordFields, "游戏账号", "PasswordAccount", "请输入需要修改密码的账号", Globals.MaxAccountIDLength);
        currentLoginPassword = AccountField(passwordFields, "当前密码", "CurrentPassword", "请输入当前登录密码", Globals.MaxPasswordLength, true);
        newLoginPassword = AccountField(passwordFields, "新密码", "NewPassword", "5–15位英文字母或数字", Globals.MaxPasswordLength, true);
        repeatLoginPassword = AccountField(passwordFields, "确认新密码", "ConfirmPassword", "请再次输入新密码", Globals.MaxPasswordLength, true);
        passwordNotice = AccountNotice(passwordFields, "PasswordChangeNotice");
        passwordChange.Confirmed += SubmitPasswordChange;
        foreach (var field in new[] { passwordAccount, currentLoginPassword, newLoginPassword, repeatLoginPassword }) field.TextSubmitted += _ => SubmitPasswordChange();
        Button(login, "修改登录密码", 437, 560, 150, () => OpenPasswordChange());
    }

    private ConfirmationDialog AccountDialog(string title, string name, string submit, out VBoxContainer fields)
    {
        var dialog = new ConfirmationDialog { Name = name, Title = title, DialogHideOnOk = false, Unresizable = true,
            Size = new Vector2I(420, 252), Exclusive = true, Transient = true };
        login.AddChild(dialog);
        fields = new VBoxContainer { CustomMinimumSize = new Vector2(388, 212) };
        fields.AddThemeConstantOverride("separation", 10); dialog.AddChild(fields);
        dialog.GetOkButton().Text = submit; dialog.GetCancelButton().Text = "取消";
        // AcceptDialog布局会重设按钮最小尺寸，须覆盖其主题常量而不是只改按钮。
        dialog.AddThemeConstantOverride("buttons_min_width", 100); dialog.AddThemeConstantOverride("buttons_min_height", ClientUi.ButtonHeight);
        dialog.Canceled += () => { dialog.Hide(); ClearAccountSecrets(dialog); };
        dialog.CloseRequested += () => { dialog.Hide(); ClearAccountSecrets(dialog); };
        return dialog;
    }

    private LineEdit AccountField(VBoxContainer fields, string title, string name, string placeholder, int maxLength, bool secret = false)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); fields.AddChild(row);
        var label = new Label { Text = title, CustomMinimumSize = new Vector2(80, 28), VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeColorOverride("font_color", new Color("eadbb6")); row.AddChild(label);
        var edit = new LineEdit { Name = name, PlaceholderText = placeholder, MaxLength = maxLength, Secret = secret,
            CustomMinimumSize = new Vector2(0, 28), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var normal = new StyleBoxFlat { BgColor = new Color("15120f"), BorderColor = new Color("74613e"),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3 };
        var focus = (StyleBoxFlat)normal.Duplicate(); focus.BorderColor = new Color("c4aa65");
        edit.AddThemeStyleboxOverride("normal", normal); edit.AddThemeStyleboxOverride("focus", focus); edit.AddThemeStyleboxOverride("read_only", normal);
        row.AddChild(edit); return edit;
    }

    private static Label AccountNotice(VBoxContainer fields, string name)
    {
        var notice = new Label { Name = name, CustomMinimumSize = new Vector2(388, 40), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        notice.AddThemeColorOverride("font_color", new Color("e6b879")); notice.AddThemeFontSizeOverride("font_size", ClientUi.Small);
        fields.AddChild(notice); return notice;
    }

    private void OpenRegistration()
    {
        if (scene != "login") return;
        passwordChange.Hide(); ClearAccountSecrets(passwordChange);
        if (accountRequest == null)
        {
            registerAccount.Text = account.Text; registerEmail.Text = "";
            registrationNotice.Text = "请牢记账号与密码；邮箱选填，无需填写角色名称。";
        }
        registration.PopupCentered(); registerAccount.GrabFocus();
    }

    private void OpenPasswordChange(string notice = "")
    {
        if (scene != "login") return;
        registration.Hide(); ClearAccountSecrets(registration);
        if (accountRequest == null)
        {
            passwordAccount.Text = account.Text;
            passwordNotice.Text = notice.Length > 0 ? notice : "填写需要修改的账号和当前密码，修改成功后重新登录。";
        }
        passwordChange.PopupCentered(); (passwordAccount.Text.Length > 0 ? currentLoginPassword : passwordAccount).GrabFocus();
    }

    private static void ClearAccountSecrets(ConfirmationDialog dialog)
    {
        foreach (var field in dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().Where(field => field.Secret)) field.Text = "";
    }
}
