using System.Reflection;
using System.Text.Json;
using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyMovementState()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-movement.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("移动状态检查仅允许隔离版本。");
        using var record = JsonDocument.Parse(File.ReadAllText(Argument("--movement-record")));
        Point Read(string key) { var p = record.RootElement.GetProperty(key); return new(p.GetProperty("X").GetInt32(), p.GetProperty("Y").GetInt32()); }
        var door = Read("Door"); var before = Read("Before"); var after = Read("After");
        var doorId = record.RootElement.GetProperty("DoorIndex").GetByte();
        await Until(() => Client.HandshakeReady); Client.Login("NativeGM", "Native123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        if (State.User!.Location != before) throw new Exception("移动夹具角色位置不符。");
        var cell = Client.World.Map!.MapCells[door.X, door.Y];
        var library = WorldView.MapLibrary(cell.FrontIndex); var closedIndex = (cell.FrontImage & 0x7fff) - 1;
        await Until(() => Client.Assets.Sprite(library, closedIndex) != null && Client.Assets.Sprite(library, closedIndex + cell.DoorOffset) != null);
        var terrain = typeof(WorldView).GetMethod("TerrainSprite", BindingFlags.NonPublic | BindingFlags.Instance)!;
        SpriteImage DoorImage()
        { var args = new object[] { cell, true, 0L, false }; return (SpriteImage)terrain.Invoke(Client.World, args)!; }
        var closed = DoorImage();
        if (State.OpenDoors.Contains(doorId) || closed.Texture != Client.Assets.Sprite(library, closedIndex)!.Texture) throw new Exception("门初始图不是关闭帧。");
        stage = "original TCP door open, crossing, auto-close and original image offset";
        var order = new List<string>();
        void DoorPacket(Packet p)
        {
            if (p is S.Opendoor d && d.DoorIndex == doorId) order.Add(d.Close ? "close" : "open");
            if (p is S.UserLocation u && u.Location != before) order.Add("move");
        }
        Client.PacketReceived += DoorPacket;
        try
        {
            Client.SetDestination(door);
            await Until(() => State.User.Location == door && State.OpenDoors.Contains(doorId));
            if (order.IndexOf("open") < 0 || order.IndexOf("open") > order.IndexOf("move") || DoorImage().Texture != Client.Assets.Sprite(library, closedIndex + cell.DoorOffset)!.Texture)
                throw new Exception("没有先收开门回执再移动，或门没有使用DoorOffset素材。");
            await Screenshot("door-open");
            Client.SetDestination(after); await Until(() => State.User.Location == after);
            await Until(() => !State.OpenDoors.Contains(doorId));
            if (!order.Contains("close") || DoorImage().Texture != closed.Texture) throw new Exception("原服五秒关门没有同步原关闭贴图。");
            await Screenshot("door-closed");
            Client.SetDestination(before); await Until(() => State.User.Location == before);
            if (order.Count(value => value == "open") < 2) throw new Exception("返回路线没有重新开门。");
        }
        finally { Client.PacketReceived -= DoorPacket; }
        GD.Print("PASS native original TCP door: open acknowledgment, original frame offset, crossing, automatic close and reopening.");

        stage = "client trap gate and release (injected original packets)";
        var handle = typeof(Main).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Inject(Packet p) { State.Apply(p); handle.Invoke(Client, [p]); }
        var direction = Main.Direction(before, door);
        Inject(new S.InTrapRock { Trapped = true });
        Client.SetDestination(door, true);
        await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
        if (State.User.Location != before || (bool)typeof(Main).GetField("moving", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!)
            throw new Exception("困魔期间仍发送了走跑。");
        Inject(new S.InTrapRock { Trapped = false });
        await Until(() => State.User.Location == door);
        Client.SetDestination(before); await Until(() => State.User.Location == before);
        GD.Print("PASS native injected trap/release: no movement while trapped, original TCP movement resumes after release.");

        stage = "second TCP player casts repulsion";
        using var peer = new GameConnection(); var other = new GameState(); var received = new List<Packet>();
        void Drain()
        {
            while (peer.Received.TryDequeue(out var p)) { received.Add(p); other.Apply(p); }
            if (peer.Errors.TryDequeue(out var error)) throw new IOException(error);
        }
        async Task<T> Next<T>() where T : Packet
        { await Until(() => { Drain(); return received.Any(p => p is T); }); var p = received.OfType<T>().First(); received.Remove(p); return p; }
        await peer.Connect(Client.Assets.Host, Client.Assets.GamePort); await Next<S.Connected>();
        await peer.Send(new C.ClientVersion { VersionHash = [] }); await Next<S.ClientVersion>();
        await peer.Send(new C.Login { AccountID = "NativePush", Password = "Native123" });
        await peer.Send(new C.StartGame { CharacterIndex = (await Next<S.LoginSuccess>()).Characters.Single().Index });
        await Next<S.UserInformation>(); await peer.Send(new C.ChangeAMode { Mode = AttackMode.All }); await Next<S.ChangeAMode>();
        await Until(() => State.Actors.Values.Any(actor => actor.Name == "NativePush"));
        await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
        var pushes = new List<S.Pushed>();
        void Pushed(Packet p) { if (p is S.Pushed push) pushes.Add(push); }
        Client.PacketReceived += Pushed;
        try
        {
            Client.SetDestination(before);
            // 模拟已有移动在途，验证受推回执会解除等待并从新坐标继续原目标。
            typeof(Main).GetField("moving", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Client, true);
            typeof(Main).GetField("moveTime", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Client, Environment.TickCount64);
            await peer.Send(new C.Magic { ObjectID = other.User!.ObjectID, Spell = Spell.Repulsion, Direction = Main.Direction(other.User.Location, before), Location = other.User.Location });
            await Until(() => pushes.Count > 0);
            var last = pushes[^1];
            if (State.User.Location != last.Location || State.Me!.Action != MirAction.Pushed || State.Me.Direction != last.Direction ||
                (bool)typeof(Main).GetField("moving", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!)
                throw new Exception("真实受推未同步终点/朝向或未解除旧移动等待。");
            var motion = State.Me.MotionTime; var duration = Client.World.ActionDuration(State.Me);
            var halfway = Client.World.Visual(State.Me, motion + duration / 2);
            if (halfway == new Vector2(last.Location.X, last.Location.Y) || duration != State.PlayerTiming.Scale(500))
                throw new Exception("真实受推没有平滑插值或动作时长错误。");
            await Until(() => State.User.Location == before);
            if (State.Me.Location != before) throw new Exception("受推后人物和相机坐标未恢复一致。");
            GD.Print($"PASS native original TCP repulsion: {pushes.Count} pushes, backward facing, smooth displacement and pending-move recovery.");
        }
        finally { Client.PacketReceived -= Pushed; }
        stage = "door timeout and all run cells need acknowledgment";
        var secondCell = Client.World.Map.MapCells[after.X, after.Y]; var secondDoor = secondCell.DoorIndex;
        var ensure = typeof(Main).GetMethod("EnsureDoorsOpen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var requests = (Dictionary<byte, long>)typeof(Main).GetField("doorRequests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client)!;
        try
        {
            cell.DoorIndex = 250; secondCell.DoorIndex = 251;
            bool Ready() => (bool)ensure.Invoke(Client, [Main.Direction(before, door), 2, Environment.TickCount64])!;
            if (Ready() || requests.Count != 2) throw new Exception("两格跑没有逐格请求开门。");
            Inject(new S.Opendoor { DoorIndex = 250 });
            if (Ready()) throw new Exception("只收到第一扇门确认就允许穿过第二扇门。");
            Inject(new S.Opendoor { DoorIndex = 251 });
            if (!Ready()) throw new Exception("完整开门确认后仍阻止跑步。");
            State.OpenDoors.Remove(250); State.OpenDoors.Remove(251);
            Client.SetDestination(door);
            await Until(() => requests.ContainsKey(250));
            await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
            if (State.User.Location != before) throw new Exception("未确认开门仍发起移动。");
            await Until(() => !requests.ContainsKey(250) && typeof(Main).GetField("destination", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client) == null);
            if (!((RichTextLabel)Client.FindChild("ChatMessages", true, false)).GetParsedText().Contains("未收到开门确认") || State.User.Location != before)
                throw new Exception("开门超时后没有停止/提示，或移动了角色。");
        }
        finally { cell.DoorIndex = doorId; secondCell.DoorIndex = secondDoor; State.OpenDoors.Remove(250); State.OpenDoors.Remove(251); }
        GD.Print("PASS native door gate: every run cell requires acknowledgment; refused open stops once with visible timeout and unchanged position.");
        await Screenshot("movement-final");
        await Client.Quit();
    }
}
