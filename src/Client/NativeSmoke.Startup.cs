using Godot;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyStartupLogin()
    {
        stage = "launcher automatic login stops at character selection";
        var failure = OS.GetCmdlineUserArgs().Contains("--expect-login-failure");
        await Until(() => packets.Any(packet => packet is S.LoginSuccess or S.Login));
        if (failure)
        {
            if (packets.OfType<S.Login>().Single().Result != 3 || State.User != null || State.Characters.Count != 0)
                throw new Exception("自动登录的账号错误没有保留在登录页。");
            await Screenshot("startup-login-rejected");
            if (packets.OfType<S.Login>().Count() != 1) throw new Exception("失败后自动重复提交了登录。");
        }
        else
        {
            if (packets.OfType<S.LoginSuccess>().Count() != 1 || State.User != null || State.Characters.Count == 0)
                throw new Exception("启动参数未完成原TCP登录，或自动进入了游戏。");
            var selection = Client.FindChildren("*", "Control", true, false).OfType<OriginalArt>().Single(art => art.Page == "select").GetParent<Control>();
            if (!selection.Visible) throw new Exception("自动登录成功后没有停在选角页。");
            await Screenshot("startup-selection");
        }
        if (Environment.GetEnvironmentVariable("MIR2_LOGIN_ACCOUNT") != null || Environment.GetEnvironmentVariable("MIR2_LOGIN_PASSWORD") != null)
            throw new Exception("客户端读取启动登录资料后没有清除环境变量。");
        GD.Print(failure ? "PASS native startup login: visible original account rejection, no automatic retry." : "PASS native startup login: actual launcher arguments, original TCP login, character selection only, no automatic game entry.");
        await Client.Quit();
    }
}
