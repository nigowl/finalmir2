using System.Reflection;
using System.Text.Json;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyGuildCreation()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-guild-create.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("建会检查只允许独立版本。");
        var restored = OS.GetCmdlineUserArgs().Contains("--verify-guild-creation-save");
        await Until(() => Client.HandshakeReady);
        if (!restored) { await RejectGuildApplicant("CreatePoor"); await RejectGuildApplicant("CreateYoung"); }
        stage = "original guild creation login";
        await ToSignal(GetTree().CreateTimer(5.2), SceneTreeTimer.SignalName.Timeout);
        Client.Login("CreateFounder", "Native123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        var user = State.User!; var token = user.Inventory.Single(item => item != null); var id = token.UniqueID;
        string Snapshot() => JsonSerializer.Serialize(new { user.RealId, user.Gold, Guild = State.Guild?.GuildName,
            Items = user.Inventory.Where(item => item != null).Select(item => new { item.UniqueID, item.Count, item.CurrentDura }) });
        async Task Barrier()
        {
            var time = Environment.TickCount64; packets.RemoveAll(packet => packet is S.KeepAlive);
            Client.Send(new C.KeepAlive { Time = time }); await Until(() => packets.OfType<S.KeepAlive>().Any(packet => packet.Time == time));
        }
        if (restored)
        {
            await Until(() => State.InGuild);
            if (Snapshot() != File.ReadAllText(Argument("--guild-creation-record")) || user.Gold != 4000 || token.Count != 1)
                throw new Exception("建会身份、金币、会籍或材料编号/数量未跨完整重启保存。");
        }
        else
        {
            var before = Snapshot();
            var npc = State.Actors.Values.Single(actor => actor.Name == "行会创建管理员");
            var dialog = (ConfirmationDialog)Client.FindChild("NpcInputDialog", true, false);
            var input = (LineEdit)dialog.FindChild("NpcInputValue", true, false);
            async Task Open()
            {
                packets.RemoveAll(packet => packet is S.NPCResponse or S.GuildNameRequest);
                Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" }); await Packet<S.NPCResponse>();
                var link = Client.FindChild("NpcText", true, false).FindChildren("*", "LinkButton", true, false).Cast<LinkButton>().Single(button => button.Text == "创建行会");
                link.EmitSignal(BaseButton.SignalName.Pressed); await Packet<S.GuildNameRequest>(); await Until(() => dialog.Visible);
                if (dialog.Title != "创建行会" || input.MaxLength != 20 || input.Text.Length != 0) throw new Exception("建会输入框或旧草稿清理错误。");
            }
            stage = "guild creation cancellation and refusal";
            Client.Send(new C.GuildNameReturn { Name = "未授权行会" }); await Barrier();
            if (Snapshot() != before) throw new Exception("未经NPC授权直接建会改变资产。");
            await Open(); input.Text = "取消的行会"; dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); await Barrier();
            if (dialog.Visible || Snapshot() != before) throw new Exception("取消建会仍扣费或创建。");
            await Open(); input.Text = "ab"; dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Barrier();
            if (!dialog.Visible || Snapshot() != before) throw new Exception("无效名称未阻止提交。");
            input.Text = "已有行会"; dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Barrier();
            if (dialog.Visible || Snapshot() != before) throw new Exception("重名建会改变资产。");
            var handle = typeof(Main).GetMethod("NpcInputPacket", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (Packet reset in new Packet[] { new S.MapChanged(), new S.Death(), new S.NPCUpdate { NPCID = npc.Id + 100 } })
            {
                await Open(); input.Text = "失效的行会"; handle.Invoke(Client, [reset]);
                dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Barrier();
                if (dialog.Visible || Snapshot() != before) throw new Exception("生命周期改变后仍能提交旧建会名称。");
            }
            stage = "real NPC guild creation and exact costs";
            await Open(); input.Text = "微端建会验证"; await Screenshot("guild-creation-input");
            input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            await Until(() => State.InGuild && user.Gold == 4000 && token.Count == 1);
            Client.Send(new C.GuildNameReturn { Name = "重复的行会" }); await Barrier();
            if (State.Guild!.GuildName != "微端建会验证" || State.Guild.MyRankId != 0 || user.Gold != 4000 || token.Count != 1 || token.UniqueID != id || dialog.Visible ||
                packets.OfType<S.LoseGold>().Count(packet => packet.Gold == 1000) != 1 || packets.OfType<S.DeleteItem>().Count(packet => packet.UniqueID == id && packet.Count == 2) != 1)
                throw new Exception("建会会籍、原费用/材料回执或重复提交保护错误。");
            File.WriteAllText(Argument("--guild-creation-record"), Snapshot()); await Screenshot("guild-creation-created");
        }
        Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
        GD.Print($"PASS native guild creation{(restored ? " saved" : "")}: original NPC, rejection/cancel, one fee/material debit, guild identity and normal logout.");
        await Client.Quit();
    }

    private async Task RejectGuildApplicant(string name)
    {
        stage = "guild creation original refusal " + name;
        await ToSignal(GetTree().CreateTimer(5.2), SceneTreeTimer.SignalName.Timeout);
        using var connection = new GameConnection(); var state = new GameState(); var seen = new List<Packet>();
        void Drain() { while (connection.Received.TryDequeue(out var packet)) { state.Apply(packet); seen.Add(packet); } }
        async Task<T> Next<T>() where T : Packet
        { await Until(() => { Drain(); return seen.Any(packet => packet is T); }); var packet = seen.OfType<T>().First(); seen.Remove(packet); return packet; }
        await connection.Connect(Client.Assets.Host, Client.Assets.GamePort); await Next<S.Connected>();
        await connection.Send(new C.ClientVersion { VersionHash = [] }); await Next<S.ClientVersion>();
        await connection.Send(new C.Login { AccountID = name, Password = "Native123" });
        await connection.Send(new C.StartGame { CharacterIndex = (await Next<S.LoginSuccess>()).Characters.Single().Index }); await Next<S.UserInformation>();
        await Until(() => { Drain(); return state.Actors.Values.Any(actor => actor.Name == "行会创建管理员"); });
        var npc = state.Actors.Values.Single(actor => actor.Name == "行会创建管理员");
        var gold = state.User!.Gold; var item = state.User.Inventory.Single(item => item != null); var id = item.UniqueID;
        await connection.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" }); await Next<S.NPCResponse>();
        await connection.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@CREATEGUILD]" });
        if (name == "CreatePoor") await Next<S.GuildNameRequest>();
        await connection.Send(new C.GuildNameReturn { Name = "不应成立的行会" });
        var time = Environment.TickCount64; await connection.Send(new C.KeepAlive { Time = time });
        await Until(() => { Drain(); return seen.OfType<S.KeepAlive>().Any(packet => packet.Time == time); });
        if (state.InGuild || state.User.Gold != gold || item.UniqueID != id || item.Count != 3) throw new Exception("资金/等级不足仍建会或扣资产。");
        await connection.Send(new C.LogOut()); await Next<S.LogOutSuccess>();
        GD.Print($"PASS native guild creation refusal: {name}, gold {gold}, original material #{id}/3 unchanged.");
    }
}
