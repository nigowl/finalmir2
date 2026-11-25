using System.Text.Json;
using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;
using Colour = System.Drawing.Color;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private long warBarrier;
    private async Task GuildWarBarrier()
    {
        var token = --warBarrier; Client.Send(new C.KeepAlive { Time = token });
        await Until(() => packets.OfType<S.KeepAlive>().Any(packet => packet.Time == token));
    }

    private sealed class WarPeer(NativeSmoke owner) : IDisposable
    {
        public readonly GameConnection Connection = new();
        public readonly GameState State = new();
        public readonly List<Packet> Packets = [];
        public void Drain()
        {
            while (Connection.Received.TryDequeue(out var packet)) { Packets.Add(packet); State.Apply(packet); }
            if (Connection.Errors.TryDequeue(out var error)) throw new IOException(error);
        }
        public async Task<T> Next<T>() where T : Packet
        {
            await owner.Until(() => { Drain(); return Packets.Any(packet => packet is T); });
            var result = Packets.OfType<T>().First(); Packets.Remove(result); return result;
        }
        public async Task Login(int number)
        {
            await Connection.Connect(owner.Client.Assets.Host, owner.Client.Assets.GamePort); await Next<S.Connected>();
            await Connection.Send(new C.ClientVersion { VersionHash = [] }); await Next<S.ClientVersion>();
            await Connection.Send(new C.Login { AccountID = $"WarClient{number:00}", Password = "Native123" });
            await Connection.Send(new C.StartGame { CharacterIndex = (await Next<S.LoginSuccess>()).Characters.Single().Index });
            await Next<S.UserInformation>(); await owner.Until(() => { Drain(); return State.InGuild; });
        }
        public void Dispose() => Connection.Dispose();
    }

    private async Task VerifyGuildWar()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-guild-war.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("行会战争检查仅允许隔离版本。");
        stage = "original guild war login";
        await Until(() => Client.HandshakeReady); Client.Login("WarClient00", "Native123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.InGuild && !Client.World.Loading);
        var restored = OS.GetCmdlineUserArgs().Contains("--verify-guild-war-save");
        if (State.Guild!.Gold != (restored ? 7000U : 10000U) || State.Me!.Colour.ToArgb() != Colour.White.ToArgb())
            throw new Exception("原行会资金未跨重启保存，或运行时战争状态被错误恢复。");
        if (restored)
        {
            using var saved = JsonDocument.Parse(File.ReadAllText(Argument("--guild-war-record") + ".first.json"));
            var items = State.User!.Inventory.Concat(State.User.Equipment).Where(item => item != null).Select(item => new { item.UniqueID, item.Count, item.CurrentDura });
            if (saved.RootElement.GetProperty("RealId").GetUInt32() != State.User.RealId || saved.RootElement.GetProperty("Gold").GetUInt32() != State.User.Gold ||
                saved.RootElement.GetProperty("Items").GetRawText() != JsonSerializer.Serialize(items))
                throw new Exception("宣战完整重启后角色身份、个人金币或物品编号/数量/耐久改变。");
        }
        using var enemy = new WarPeer(this); using var friend = new WarPeer(this); using var neutral = new WarPeer(this);
        foreach (var (peer, number) in new[] { (enemy, 6), (friend, 1), (neutral, 12) })
        { await ToSignal(GetTree().CreateTimer(5.2), SceneTreeTimer.SignalName.Timeout); await peer.Login(number); }
        void Drain() { enemy.Drain(); friend.Drain(); neutral.Drain(); }
        await Until(() => State.Actors.ContainsKey(enemy.State.User!.ObjectID));
        var bank = State.Guild.Gold;
        var dialog = (ConfirmationDialog)Client.FindChild("NpcInputDialog", true, false);
        var input = (LineEdit)dialog.FindChild("NpcInputValue", true, false);
        var npc = State.Actors.Values.Single(actor => actor.Name == "行会战争管理员");
        async Task Open()
        {
            stage = "original NPC guild war request";
            packets.RemoveAll(packet => packet is S.NPCResponse or S.GuildRequestWar);
            Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" }); await Packet<S.NPCResponse>();
            var link = Client.FindChild("NpcText", true, false).FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().Single(button => button.Text == "行会宣战");
            link.EmitSignal(BaseButton.SignalName.Pressed); await Packet<S.GuildRequestWar>(); await Until(() => dialog.Visible);
            if (input.MaxLength != 20 || dialog.Title != "行会宣战" || input.Text.Length != 0) throw new Exception("宣战未复用原输入框或遗留旧名称。");
        }
        if (!restored)
        {
            await Open(); input.Text = "WarGuildB"; await Screenshot("guild-war-input");
            dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); await GuildWarBarrier();
            if (dialog.Visible || State.Guild.Gold != bank) throw new Exception("取消宣战仍扣款或残留弹窗。");
            await Open(); input.Text = "ab"; dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await GuildWarBarrier();
            if (!dialog.Visible || State.Guild.Gold != bank) throw new Exception("非法名称未留在输入框或提前扣款。");
            input.Text = "WarGuildA"; dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await GuildWarBarrier();
            if (dialog.Visible || State.Guild.Gold != bank) throw new Exception("向自己行会宣战改变了资金。");
            foreach (var peer in new[] { friend, neutral })
            {
                peer.Packets.Clear(); await peer.Connection.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" }); await peer.Next<S.NPCResponse>();
                await peer.Connection.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@REQUESTWAR]" }); await peer.Next<S.NPCResponse>();
                await peer.Connection.Send(new C.GuildWarReturn { Name = "WarGuildB" });
                await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout); peer.Drain();
                if (peer.State.Guild!.Gold != (peer == neutral ? 2999U : bank) || peer.State.Me!.Colour.ToArgb() != Colour.White.ToArgb())
                    throw new Exception("非会长或资金不足仍完成宣战。");
            }
        }
        await Open(); input.Text = "WarGuildB";
        packets.RemoveAll(packet => packet is S.GuildStorageGoldChange);
        input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        var charged = await Packet<S.GuildStorageGoldChange>();
        await Until(() => { Drain(); return State.Guild.Gold == bank - 3000 && State.Me.Colour.ToArgb() == Colour.Blue.ToArgb() &&
            State.Actors[enemy.State.User!.ObjectID].Colour.ToArgb() == Colour.Orange.ToArgb(); });
        Client.Send(new C.GuildWarReturn { Name = "WarGuildC" }); await GuildWarBarrier();
        if (charged.Type != 2 || charged.Amount != 3000 || State.Guild.Gold != bank - 3000 ||
            friend.State.Me!.Colour.ToArgb() != Colour.Blue.ToArgb() || neutral.State.Me!.Colour.ToArgb() != Colour.White.ToArgb() || neutral.State.Guild!.Gold != 2999)
            throw new Exception("原战争扣款、重复提交保护或敌我/中立颜色错误。");
        Client.World.Selected = enemy.State.User!.ObjectID;
        await VerifyNamePixels(State.Me, Colour.Blue); await VerifyNamePixels(State.Actors[Client.World.Selected], Colour.Orange);
        await Screenshot("guild-war-active");
        friend.Drain(); friend.Packets.Clear(); var members = State.Guild.MemberCount;
        await friend.Connection.Send(new C.Chat { Message = "@LEAVEGUILD" });
        var leaveBarrier = --warBarrier; await friend.Connection.Send(new C.KeepAlive { Time = leaveBarrier });
        await Until(() => { friend.Drain(); return friend.Packets.OfType<S.KeepAlive>().Any(packet => packet.Time == leaveBarrier); });
        if (!friend.State.InGuild || friend.State.Guild!.GuildName != State.Guild.GuildName || State.Guild.MemberCount != members ||
            !friend.Packets.OfType<S.Chat>().Any(packet => packet.Type == ChatType.System))
            throw new Exception("战争期间退会未按原规则拒绝，或丢失会籍/成员。");
        GD.Print("PASS native guild war leave: original member command rejected during active war; membership and count unchanged.");
        await VerifyGuildWarCombat(enemy, friend, neutral, Drain, !restored);
        if (restored)
        {
            stage = "original guild war expiry"; var deadline = Environment.TickCount64 + 130000;
            while (State.Me.Colour.ToArgb() != Colour.White.ToArgb())
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException("原战争定时结束未刷新颜色。");
                Drain(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Until(() => { Drain(); return State.Actors[enemy.State.User!.ObjectID].Colour.ToArgb() == Colour.White.ToArgb(); });
            await GuildWarAttack(enemy, false, Drain); await Screenshot("guild-war-ended");
            GD.Print("PASS native guild war expiry: original timer ends both relations, names return white and enemy-guild mode no longer harms the former enemy.");
        }
        File.WriteAllText(Argument("--guild-war-record") + (restored ? ".restored.json" : ".first.json"), JsonSerializer.Serialize(new
        { State.User!.RealId, State.User.Gold, GuildGold = State.Guild.Gold, Items = State.User.Inventory.Concat(State.User.Equipment).Where(item => item != null).Select(item => new { item.UniqueID, item.Count, item.CurrentDura }) }));
        await ToSignal(GetTree().CreateTimer(Globals.LogDelay / 1000d + .5), SceneTreeTimer.SignalName.Timeout);
        foreach (var peer in new[] { enemy, friend, neutral }) { await peer.Connection.Send(new C.LogOut()); await peer.Next<S.LogOutSuccess>(); }
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        GD.Print($"PASS native guild war{(restored ? " saved" : "")}: real NPC input/cancel, original fee, single submission, party colours, attack permissions and normal logout.");
        await Client.Quit();
    }

    private async Task GuildWarAttack(WarPeer target, bool allowed, Action drain)
    {
        stage = "guild war attack permission " + allowed; drain(); var before = target.State.User!.HP;
        Client.Send(new C.Attack { Direction = Main.Direction(State.User!.Location, target.State.User.Location), Spell = Spell.None });
        var until = Environment.TickCount64 + 1600; var damaged = false;
        while (Environment.TickCount64 < until)
        { drain(); damaged |= target.State.User.HP < before; await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        if (damaged != allowed) throw new Exception($"原战争攻击权限错误：{target.State.User.Name}，允许{allowed}，生命{before}→{target.State.User.HP}。");
        GD.Print($"PASS native guild war hit: {target.State.User.Name}, allowed={allowed}, HP={before}->{target.State.User.HP}.");
    }

    private async Task VerifyGuildWarCombat(WarPeer enemy, WarPeer friend, WarPeer neutral, Action drain, bool safeZone)
    {
        stage = "original guild war attack modes";
        Client.Send(new C.ChangeAMode { Mode = AttackMode.Peace }); await Until(() => State.CurrentAttackMode == AttackMode.Peace);
        await GuildWarAttack(enemy, false, drain);
        Client.Send(new C.ChangeAMode { Mode = AttackMode.EnemyGuild }); await Until(() => State.CurrentAttackMode == AttackMode.EnemyGuild);
        await GuildWarAttack(friend, false, drain); await GuildWarAttack(neutral, false, drain); await GuildWarAttack(enemy, true, drain);
        if (!safeZone) return;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Argument("--guild-war-record")));
        var start = fixture.RootElement.GetProperty("Start"); var x = start.GetProperty("X").GetInt32(); var y = start.GetProperty("Y").GetInt32();
        async Task Step(Point point)
        {
            drain(); var from = enemy.State.User!.Location;
            await enemy.Connection.Send(new C.Walk { Direction = Main.Direction(from, point) });
            await Until(() => { drain(); return enemy.State.User.Location == point; });
            await ToSignal(GetTree().CreateTimer(.6), SceneTreeTimer.SignalName.Timeout);
        }
        foreach (var point in new[] { new Point(x + 1, y - 1), new Point(x, y - 2), new Point(x - 1, y - 3), new Point(x - 2, y - 3), new Point(x - 3, y - 3) }) await Step(point);
        Client.SetDestination(new(x - 2, y - 3)); await Until(() => State.User!.Location == new Point(x - 2, y - 3));
        await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout); await GuildWarAttack(enemy, false, drain);
        foreach (var point in new[] { new Point(x - 3, y - 2), new Point(x - 2, y - 2), new Point(x - 1, y - 1), new Point(x, y - 1), new Point(x + 1, y) }) await Step(point);
        Client.SetDestination(new(x, y)); await Until(() => State.User!.Location == new Point(x, y));
        await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
    }
}
