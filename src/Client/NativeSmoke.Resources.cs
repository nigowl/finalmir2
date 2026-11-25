using System.Reflection;
using Client.MirObjects;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyConnectionFailures()
    {
        stage = "five real connection attempts and manual retry";
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var dialog = (ConfirmationDialog)Client.FindChild("ConnectionDialog", true, false);
        var notice = (Label)dialog.FindChild("ConnectionNotice", true, false);
        var deadline = Environment.TickCount64 + 155000;
        while ((bool)typeof(Main).GetField("connecting", flags)!.GetValue(Client)!)
        {
            if (!dialog.Visible || Environment.TickCount64 >= deadline) throw new Exception("连接弹窗提前消失或自动重试未按预算停止。");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (!notice.Text.Contains("已尝试 5 次") || dialog.GetOkButton().Text != "重试" || dialog.GetCancelButton().Text != "退出" ||
            (int)typeof(Main).GetField("connectionAttempt", flags)!.GetValue(Client)! != 5)
            throw new Exception("五次失败后没有停止，或缺少重试/退出。");
        // 核对自动重试已结束，并让服务端同 IP 接入限流与其他检查错开。
        await ToSignal(GetTree().CreateTimer(6), SceneTreeTimer.SignalName.Timeout);
        if ((bool)typeof(Main).GetField("connecting", flags)!.GetValue(Client)!) throw new Exception("五次失败后仍在自动连接。");
        dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => Client.HandshakeReady && !dialog.Visible);
        var reconnect = Client.Connect(Argument("--server"));
        if (!dialog.Visible) throw new Exception("再次连接时没有弹窗。");
        GD.Print("PASS native connection retry: five 30-second attempts stop, manual retry connects and closes modal, exit cancels new attempt.");
        dialog.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
        await reconnect;
    }

    private async Task VerifyResourceStartup()
    {
        stage = "offline login art, selection frames, HUD and reconnect";
        var hud = (TextureRect)Client.FindChild("HudBackground", true, false);
        var pictures = (List<(TextureRect Rect, string Library, int Index)>)typeof(Main)
            .GetField("pictures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!;
        var original = hud.Texture;
        var originalClose = Client.Theme.GetIcon("close", "Window");
        if (Client.Assets.Ready || Client.Assets.Downloads != 0 ||
            Client.Assets.Sprite("ChrSel", 0) == null || Client.Assets.Sprite("Prguse", 65) == null ||
            original?.GetSize() != new Vector2(1024, 152) || pictures.Any(picture => picture.Rect.Texture == null) ||
            Client.Theme.GetStylebox("normal", "Button") is not StyleBoxTexture || originalClose == null)
            throw new Exception("未连接时登录/选角背景、底栏或窗口主题未从内置包加载。");
        var startupSounds = Task.WhenAll(new[] { 10103, 10146, 10147 }.Select(Client.Assets.Sound));
        if (Argument("--screenshot") is { Length: > 0 } path)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (Client.Assets.Ready) throw new Exception("离线截图前资源服务已就绪。");
            Capture(Path.ChangeExtension(path, "offline-login.png"));
        }
        if ((await startupSounds).Any(sound => sound == null || sound.GetLength() <= 0))
            throw new Exception("未连接时无法解码内置启动音效。");
        foreach (var job in Enum.GetValues<MirClass>())
        foreach (var gender in Enum.GetValues<MirGender>())
        {
            var start = OriginalPlayerArt.SelectionStart(job, gender);
            if (Enumerable.Range(start, 16).Any(index => Client.Assets.Sprite("ChrSel", index) == null))
                throw new Exception($"内置造型帧不全：{job}/{gender}");
        }
        for (var connection = 0; connection < 2; connection++)
        {
            if (connection > 0)
            {
                await ToSignal(GetTree().CreateTimer(6), SceneTreeTimer.SignalName.Timeout);
                var connect = Client.Connect(Argument("--server"));
                if (Client.Assets.Ready || hud.Texture != original || Client.Theme.GetIcon("close", "Window") != originalClose)
                    throw new Exception("重连开始时 HUD 或窗口主题闪空。");
                await connect;
            }
            await Until(() => Client.HandshakeReady);
            var libraries = (Dictionary<string, Task<LibraryFrames>>)typeof(ResourceCache)
                .GetField("libraries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client.Assets)!;
            if (libraries.Values.Any(task => task.IsFaulted))
                throw new Exception("初始化期间素材请求提前失败：" + libraries.Values.First(task => task.IsFaulted).Exception);
            try
            {
                await Until(() => Client.HandshakeReady && pictures.All(picture => picture.Rect.Texture != null) &&
                    (bool)typeof(Main).GetField("originalButtonThemeReady", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)! &&
                    Client.Assets.PendingDownloads == 0);
            }
            catch (TimeoutException timeout)
            {
                throw new Exception($"资源未就绪：theme={typeof(Main).GetField("originalButtonThemeReady", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)}，等待={Client.Assets.PendingDownloads}，缺图={string.Join(',', pictures.Where(picture => picture.Rect.Texture == null).Select(picture => $"{picture.Library}/{picture.Index}"))}", timeout);
            }
            if (!GodotObject.IsInstanceValid(original) || hud.Texture != original)
                throw new Exception("重连没有保留原 HUD 纹理。");
        }
        var music = Client.Assets.Sound(10109);
        await Until(() => music.IsCompleted);
        if (!music.IsCompletedSuccessfully || music.Result == null || music.Result.GetLength() <= 0)
            throw new Exception("SoundList的109.wav缺失时未能加载实际109.mp3。");
        if (Client.Assets.Errors.TryPeek(out var error)) throw new Exception(error);
        GD.Print($"PASS native resource startup: offline UI and 160 selection frames, all {pictures.Count} UI pictures, three packaged sounds, HUD retained on reconnect, remote 109.mp3 fallback.");
        if (OS.GetCmdlineUserArgs().Contains("--fixture-world")) await VerifyStartupWorld(hud, original!);
        await Client.Quit();
    }

    private async Task VerifyStartupWorld(TextureRect hud, Texture2D original)
    {
        stage = "cold-cache character creation, selection animation and first HUD";
        var name = "Art" + Environment.TickCount64 % 100000000;
        Client.Register(name, "Native123");
        await Packet<S.LoginSuccess>();
        var submit = Client.FindChildren("*", "Button", true, false).OfType<Button>()
            .Single(button => button is not OriginalButton && button.Text == "创建角色");
        var form = submit.GetParent<Control>();
        if (!form.IsVisibleInTree() || form.GetThemeStylebox("panel") is not StyleBoxTexture)
            throw new Exception("建角框首次出现时未使用内置皮肤。");
        form.GetChildren().OfType<LineEdit>().Single().Text = name;
        await ClickSelectionControl(submit);
        var created = (await Packet<S.NewCharacterSuccess>()).CharInfo;
        var preview = Client.FindChildren("*", "Control", true, false).OfType<OriginalArt>().Single(art => art.Page == "select");
        await Until(() => preview.PreviewReady);
        var firstFrame = preview.PreviewFrame;
        await Until(() => preview.PreviewFrame != firstFrame);
        await Screenshot("selection");
        Client.Send(new C.StartGame { CharacterIndex = created.Index });
        await Packet<S.UserInformation>();
        if (!hud.IsVisibleInTree() || hud.Texture != original) throw new Exception("进入世界的第一帧没有 HUD 底图。");
        await Until(() => Client.World.Map != null && !Client.World.Loading);
        await Screenshot("world");
        GD.Print("PASS native resource world: styled creation, animated preview, HUD on world entry, map loaded from server.");
    }
}
