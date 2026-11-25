using Godot;
using C = ClientPackets;
using S = ServerPackets;
using NameColour = System.Drawing.Color;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyNameColours()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-name-colours.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("名称颜色检查仅允许隔离版本。");
        stage = "name colours login";
        await Until(() => Client.HandshakeReady); Client.Login("NativeGM", "Native123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var restored = OS.GetCmdlineUserArgs().Contains("--verify-name-colours-save");
        var initial = restored ? NameColour.Red : NameColour.White;
        if (State.User!.NameColour.ToArgb() != initial.ToArgb() || State.Me!.Colour.ToArgb() != initial.ToArgb())
            throw new Exception("登录初始名称颜色与原服不符。");

        stage = "second original TCP observer";
        await ToSignal(GetTree().CreateTimer(5.2), SceneTreeTimer.SignalName.Timeout);
        using var peer = new GameConnection(); var other = new GameState(); var received = new List<Packet>();
        void Drain()
        {
            while (peer.Received.TryDequeue(out var packet)) { received.Add(packet); other.Apply(packet); }
            if (peer.Errors.TryDequeue(out var error)) throw new IOException(error);
        }
        async Task<T> Next<T>() where T : Packet
        {
            await Until(() => { Drain(); return received.Any(packet => packet is T); });
            var packet = received.OfType<T>().First(); received.Remove(packet); return packet;
        }
        await peer.Connect(Client.Assets.Host, Client.Assets.GamePort); await Next<S.Connected>();
        await peer.Send(new C.ClientVersion { VersionHash = [] }); await Next<S.ClientVersion>();
        await peer.Send(new C.Login { AccountID = "NativeGuild", Password = "Native123" });
        await peer.Send(new C.StartGame { CharacterIndex = (await Next<S.LoginSuccess>()).Characters.Single().Index });
        await Next<S.UserInformation>();
        await Until(() => State.Actors.ContainsKey(other.User!.ObjectID));
        var me = State.Me!; var remote = State.Actors[other.User!.ObjectID];
        if (remote.Colour.ToArgb() != initial.ToArgb() || other.Me!.Colour.ToArgb() != initial.ToArgb())
            throw new Exception("场景初始名称颜色与原服不符。");
        if (!restored)
        {
            var destination = new System.Drawing.Point(me.Location.X - 2, me.Location.Y + 1);
            Client.SetDestination(destination);
            await Until(() => State.User.Location == destination && Environment.TickCount64 - me.MotionTime > me.MoveDelay + Actor.MovementBuffer);
        }
        Client.World.Selected = remote.Id;
        await VerifyNamePixels(me, initial); await VerifyNamePixels(remote, initial);
        await Screenshot(restored ? "restored-red" : "initial-white");

        async Task Change(bool self, int points, NameColour expected)
        {
            stage = $"original TCP name colour {self}/{points}";
            Drain(); received.RemoveAll(packet => packet is S.ColourChanged or S.ObjectColourChanged);
            packets.RemoveAll(packet => packet is S.ColourChanged or S.ObjectColourChanged);
            var unchanged = (self ? remote : me).Colour.ToArgb();
            Client.Send(new C.Chat { Message = $"@设置PK值 {(self ? "NativeGM" : "NativeGuildLead")} {points}" });
            var own = self ? await Packet<S.ColourChanged>() : await Next<S.ColourChanged>();
            var seen = self ? await Next<S.ObjectColourChanged>() : await Packet<S.ObjectColourChanged>();
            var actor = self ? me : remote; var observer = self ? other : State;
            if (own.NameColour.ToArgb() != expected.ToArgb() || seen.ObjectID != actor.Id || seen.NameColour.ToArgb() != expected.ToArgb() ||
                actor.Colour.ToArgb() != expected.ToArgb() || observer.Actors[actor.Id].Colour.ToArgb() != expected.ToArgb() ||
                (self ? State : other).User!.NameColour.ToArgb() != expected.ToArgb() || (self ? remote : me).Colour.ToArgb() != unchanged)
                throw new Exception("原服本人/旁观者颜色回执没有隔离更新。");
            await VerifyNamePixels(actor, expected);
        }
        if (!restored)
        {
            // 原引擎立即开始自然减PK值；取区间中值，避免测试恰好跌过颜色阈值。
            await Change(true, 150, NameColour.Yellow); await Change(false, 150, NameColour.Yellow);
            await Screenshot("yellow");
            await Change(true, 250, NameColour.Red); await Change(false, 250, NameColour.Red);
            await Screenshot("red");
        }
        else
        {
            await Change(true, 0, NameColour.White); await Change(false, 0, NameColour.White);
            await Screenshot("restored-white");
        }
        await peer.Send(new C.LogOut()); await Next<S.LogOutSuccess>();
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        GD.Print($"PASS native name colours{(restored ? " saved" : "")}: original TCP self/observer replies, server-provided initial colours, visible name pixels and normal logout.");
        await Client.Quit();
    }

    private async Task VerifyNamePixels(Actor actor, NameColour expected)
    {
        stage = "visible server name colour " + actor.Name;
        await Until(() => Client.World.NameBody(actor) != null);
        if (!Client.World.NameVisible(actor) || actor.Hidden) throw new Exception("姓名颜色取样前对象未显示名称。");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var font = ThemeDB.FallbackFont; var anchor = Client.World.NameAnchor(actor, Environment.TickCount64);
        anchor.Y += (font.GetAscent(12) - font.GetDescent(12)) / 2;
        var width = font.GetStringSize(actor.Name, fontSize: 12).X;
        using var image = GetViewport().GetTexture().GetImage(); var scale = image.GetWidth() / 1024f;
        var matches = 0;
        for (var y = (int)((anchor.Y - font.GetAscent(12)) * scale); y < (anchor.Y + font.GetDescent(12)) * scale; y++)
        for (var x = (int)((anchor.X - width / 2) * scale); x < (anchor.X + width / 2) * scale; x++)
        {
            var pixel = image.GetPixel(x, y);
            if (Math.Abs(pixel.R8 - expected.R) <= 2 && Math.Abs(pixel.G8 - expected.G) <= 2 && Math.Abs(pixel.B8 - expected.B) <= 2) matches++;
        }
        if (matches < 10) throw new Exception($"姓名没有实际绘制原服颜色：{actor.Name}/{expected.Name}，匹配像素{matches}。");
        GD.Print($"PASS native name colour pixels: {actor.Name}/{expected.Name}/{matches}.");
    }
}
