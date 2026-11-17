using System.Text.RegularExpressions;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Packet? accountRequest;
    private long accountRequestSince;

    public void Register(string id, string secret)
    {
        if (accountRequest != null || scene != "login") return;
        OpenRegistration(); registerAccount.Text = id; registerPassword.Text = registerRepeat.Text = secret;
        SubmitRegistration();
    }

    private void SubmitRegistration()
    {
        if (accountRequest != null || scene != "login") return;
        var id = registerAccount.Text.Trim(); var secret = registerPassword.Text; var email = registerEmail.Text.Trim();
        if (!ValidateCredentials(id, secret, registrationNotice)) return;
        if (secret != registerRepeat.Text) { registrationNotice.Text = "两次密码不一致，请重新确认。"; registerRepeat.GrabFocus(); return; }
        if (email.Length > 0 && !Regex.IsMatch(email, @"^\w+([-+.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*$"))
        { registrationNotice.Text = "电子邮箱格式不正确；没有邮箱可留空。"; registerEmail.GrabFocus(); return; }
        registerAccount.Text = id; registerEmail.Text = email;
        StartAccountRequest(new C.NewAccount { AccountID = id, Password = secret, EMailAddress = email }, registrationNotice, "正在注册，请稍候…");
    }

    public void ChangeLoginPassword(string next)
    {
        if (accountRequest != null || scene != "login") return;
        OpenPasswordChange(); currentLoginPassword.Text = password.Text;
        newLoginPassword.Text = repeatLoginPassword.Text = next; SubmitPasswordChange();
    }

    private void SubmitPasswordChange()
    {
        if (accountRequest != null || scene != "login") return;
        var id = passwordAccount.Text.Trim(); var current = currentLoginPassword.Text; var next = newLoginPassword.Text;
        if (!ValidateCredentials(id, current, passwordNotice) || !ValidateCredentials(id, next, passwordNotice)) return;
        if (next != repeatLoginPassword.Text) { passwordNotice.Text = "两次新密码不一致，请重新确认。"; repeatLoginPassword.GrabFocus(); return; }
        if (next == current) { passwordNotice.Text = "新密码不能与当前密码相同。"; newLoginPassword.GrabFocus(); return; }
        passwordAccount.Text = id;
        StartAccountRequest(new C.ChangePassword { AccountID = id, CurrentPassword = current, NewPassword = next }, passwordNotice, "正在修改密码，请稍候…");
    }

    private void StartAccountRequest(Packet request, Label notice, string message)
    {
        accountRequest = request; accountRequestSince = Environment.TickCount64;
        SetAccountRequestBusy(true); registrationNotice.Text = passwordNotice.Text = "已有账号操作正在等待回执，请稍候。";
        notice.Text = message; Send(request);
    }

    private void SetAccountRequestBusy(bool busy)
    {
        foreach (var dialog in new[] { registration, passwordChange })
        {
            dialog.GetOkButton().Disabled = busy;
            foreach (var field in dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>()) field.Editable = !busy;
        }
    }

    internal void ProcessAccountRequests(long now)
    {
        if (accountRequest == null || now - accountRequestSince < 10000) return;
        const string message = "等待服务端回执超时，不会自动重复提交。请重新连接后核对账号或密码结果。";
        var notice = accountRequest is C.NewAccount ? registrationNotice : passwordNotice;
        if (notice.Text != message) { notice.Text = message; status.Text = message; }
    }

    private void RegistrationResult(S.NewAccount result)
    {
        if (accountRequest is not C.NewAccount request) return;
        accountRequest = null; SetAccountRequestBusy(false);
        if (result.Result == 8)
        {
            var autoLogin = registration.Visible;
            account.Text = request.AccountID; password.Text = autoLogin ? request.Password : "";
            registration.Hide(); ClearAccountSecrets(registration);
            status.Text = autoLogin ? "注册成功，正在登录…" : "注册成功，请使用新账号登录。";
            if (autoLogin) Login(request.AccountID, request.Password);
            return;
        }
        registrationNotice.Text = result.Result switch
        {
            0 => "服务器暂未开放注册，或当前 IP 注册过于频繁，请稍后再试。",
            1 => "账号不符合规则，请使用3–15位英文字母或数字。",
            2 => "密码不符合规则，请使用5–15位英文字母或数字。",
            3 => "电子邮箱格式不正确，请修改或留空。",
            4 => "账号资料中的姓名不符合服务器规则。",
            5 => "密保问题不符合服务器规则。",
            6 => "密保答案不符合服务器规则。",
            7 => "账号已存在，请更换账号或直接登录。",
            _ => $"注册失败（代码 {result.Result}），请联系管理员。"
        };
        status.Text = registrationNotice.Text;
    }

    private void LoginPasswordResult(S.ChangePassword result)
    {
        if (accountRequest is not C.ChangePassword request) return;
        accountRequest = null; SetAccountRequestBusy(false);
        if (result.Result == 6)
        {
            account.Text = request.AccountID; password.Text = request.NewPassword;
            passwordChange.Hide(); ClearAccountSecrets(passwordChange); status.Text = "密码修改成功，请重新登录。"; return;
        }
        passwordNotice.Text = result.Result switch
        {
            0 => "服务器暂未开放修改密码，请联系管理员。",
            1 => "账号格式不正确，请使用3–15位英文字母或数字。",
            2 => "当前密码格式不正确，请使用5–15位英文字母或数字。",
            3 => "新密码格式不正确，请使用5–15位英文字母或数字。",
            4 => "账号不存在，请检查填写的账号。",
            5 => "当前密码错误，请重新输入。",
            _ => $"修改密码失败（代码 {result.Result}），请联系管理员。"
        };
        status.Text = passwordNotice.Text;
    }

    private void LoginPasswordBanned(S.ChangePasswordBanned result)
    {
        if (accountRequest is not C.ChangePassword) return;
        accountRequest = null; SetAccountRequestBusy(false); ClearAccountSecrets(passwordChange);
        passwordNotice.Text = status.Text = "账号暂时无法修改密码：" + result.Reason;
    }

    private void ResetAccountRequests()
    {
        accountRequest = null; accountRequestSince = 0; SetAccountRequestBusy(false);
        foreach (var dialog in new[] { registration, passwordChange }) { dialog.Hide(); ClearAccountSecrets(dialog); }
        registrationNotice.Text = passwordNotice.Text = "";
    }
}
