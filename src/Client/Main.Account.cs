using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Control passwordChange = null!;
    private LineEdit newLoginPassword = null!, repeatLoginPassword = null!;
    private string submittedLoginPassword = "";
    private void BuildPasswordChange()
    {
        Button(login, "修改登录密码", 437, 560, 150, () => passwordChange.Show());
        passwordChange = Panel(login, 338, 284, 348, 242);
        Text(passwordChange, "修改登录密码", 18, 12, 300);
        Text(passwordChange, "使用登录框内的账号和当前密码", 18, 43, 315);
        newLoginPassword = Edit(passwordChange, 18, 78, 312, "新密码（5–15 位字母数字）", true); newLoginPassword.MaxLength = 15;
        repeatLoginPassword = Edit(passwordChange, 18, 116, 312, "再次输入新密码", true); repeatLoginPassword.MaxLength = 15;
        Button(passwordChange, "提交修改", 18, 166, 145, () =>
        {
            if (newLoginPassword.Text != repeatLoginPassword.Text) { status.Text = "两次新密码不一致。"; return; }
            ChangeLoginPassword(newLoginPassword.Text);
        });
        Button(passwordChange, "取消", 183, 166, 145, () => { newLoginPassword.Text = repeatLoginPassword.Text = ""; passwordChange.Hide(); });
        passwordChange.Hide();
    }
    public void ChangeLoginPassword(string next)
    {
        if (!HandshakeReady || submittedLoginPassword.Length > 0) return;
        if (!ValidateCredentials(account.Text, password.Text) || !ValidateCredentials(account.Text, next)) return;
        submittedLoginPassword = next;
        Send(new C.ChangePassword { AccountID = account.Text, CurrentPassword = password.Text, NewPassword = next });
    }
    private void LoginPasswordResult(S.ChangePassword result)
    {
        if (submittedLoginPassword.Length == 0) return;
        if (result.Result == 6)
        {
            password.Text = submittedLoginPassword; passwordChange.Hide(); status.Text = "密码修改成功，请重新登录。";
        }
        else status.Text = $"修改密码失败：{result.Result}（0 未开放，5 当前密码错误）";
        newLoginPassword.Text = repeatLoginPassword.Text = "";
        submittedLoginPassword = "";
    }
    private void ResetLoginPassword() { submittedLoginPassword = newLoginPassword.Text = repeatLoginPassword.Text = ""; passwordChange.Hide(); }
}
