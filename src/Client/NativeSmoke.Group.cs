using Godot;
using System.Net.Http.Headers;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyGroup()
    {
        stage = "native group bot login";
        using var bot = new GameConnection(); var other = new GameState(); var received = new List<Packet>();
        async Task BotUntil(Func<bool> condition)
        {
            await Until(() =>
            {
                while (bot.Received.TryDequeue(out var packet)) { other.Apply(packet); received.Add(packet); }
                if (bot.Errors.TryDequeue(out var error)) throw new IOException(error);
                return condition();
            });
        }
        async Task<T> BotPacket<T>() where T : Packet
        {
            await BotUntil(() => received.Any(p => p is T)); var packet = received.OfType<T>().First(); received.Remove(packet); return packet;
        }
        await ToSignal(GetTree().CreateTimer(5.2), SceneTreeTimer.SignalName.Timeout);
        await bot.Connect(Client.Assets.Host, Client.Assets.GamePort);
        await BotPacket<S.Connected>(); await bot.Send(new C.ClientVersion { VersionHash = [] }); await BotPacket<S.ClientVersion>();
        await bot.Send(new C.NewAccount { AccountID = "NativeMate", Password = "Native123" });
        if ((await BotPacket<S.NewAccount>()).Result != 8) throw new Exception("Group bot registration failed.");
        await bot.Send(new C.Login { AccountID = "NativeMate", Password = "Native123" }); await BotPacket<S.LoginSuccess>();
        await bot.Send(new C.NewCharacter { Name = "NativeMate", Class = MirClass.Warrior, Gender = MirGender.Male });
        var created = await BotPacket<S.NewCharacterSuccess>();
        await bot.Send(new C.StartGame { CharacterIndex = created.CharInfo.Index }); await BotPacket<S.UserInformation>();
        stage = "native group invite/consent";
        Client.SetGroupMode(true); await Until(() => State.AllowGroup);
        await bot.Send(new C.SwitchGroup { AllowGroup = true }); await BotUntil(() => other.AllowGroup);
        Client.OpenGroup(); Client.InviteGroup("NativeMate"); await BotUntil(() => other.GroupInvitation == State.User!.Name);
        await bot.Send(new C.GroupInvite { AcceptInvite = true });
        await Until(() => State.GroupMembers.Count == 2); await BotUntil(() => other.GroupMembers.Count == 2);
        if (State.GroupMembers[0] != "NativeWarrior" || !State.GroupMembers.SequenceEqual(other.GroupMembers) || State.GroupMaps["NativeMate"] != State.Map!.Title) throw new Exception("Group membership or member maps diverged.");
        stage = "native group chat and management monitoring";
        Client.Send(new C.Chat { Message = "!!Native party chat" });
        await BotUntil(() => received.OfType<S.ObjectChat>().Any(p => p.Type == ChatType.Group && p.Text.Contains("Native party chat")));
        var runtime = Environment.GetEnvironmentVariable("MIR2_RUNTIME_ROOT")!;
        using (var http = new System.Net.Http.HttpClient())
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(Path.Combine(runtime, "admin.key")).Trim());
            using var status = JsonDocument.Parse(await http.GetStringAsync(Argument("--server").TrimEnd('/') + "/management/status"));
            var players = status.RootElement.GetProperty("players").EnumerateArray().Where(p => p.GetProperty("name").GetString() is "NativeMate" or "NativeWarrior").ToArray();
            if (players.Length != 2 || players.Any(p => p.GetProperty("groupLeader").GetString() != "NativeWarrior" || p.GetProperty("groupSize").GetInt32() != 2)) throw new Exception("Management did not report the original engine's party.");
        }
        await Screenshot("group");
        Client.RemoveGroupMember("NativeMate"); await Until(() => State.GroupMembers.Count == 0); await BotUntil(() => other.GroupMembers.Count == 0);
        stage = "native incoming invitation and decline";
        received.RemoveAll(p => p is S.Chat);
        await bot.Send(new C.AddMember { Name = "NativeWarrior" }); await Until(() => State.GroupInvitation == "NativeMate");
        Client.RespondGroupInvitation(false); await BotUntil(() => received.OfType<S.Chat>().Any(p => p.Message.Contains("NativeWarrior")));
        if (State.GroupMembers.Count != 0) throw new Exception("Declining a group invitation formed a party.");
        await ToSignal(GetTree().CreateTimer(2.2), SceneTreeTimer.SignalName.Timeout);
        await bot.Send(new C.AddMember { Name = "NativeWarrior" }); await Until(() => State.GroupInvitation == "NativeMate");
        Client.RespondGroupInvitation(true); await Until(() => State.GroupMembers.Count == 2); await BotUntil(() => other.GroupMembers.Count == 2);
        if (State.GroupMembers[0] != "NativeMate") throw new Exception("Incoming invitation lost leader identity.");
        stage = "native party disconnect cleanup";
        await VerifyGuild(bot, other, received, BotUntil);
        await VerifyTrade(bot, other, received, BotUntil);
        await Until(() => State.GroupMembers.Count == 0);
        if (!Client.Connection.Connected || !State.AllowGroup) throw new Exception("Partner disconnect broke the remaining connection or grouping preference.");
        Client.SetGroupMode(false); await Until(() => !State.AllowGroup);
        GD.Print("PASS native groups: original art panel, consent/decline, leader order, maps, party chat, removal/disband, incoming invite, partner disconnect, management party monitoring.");
    }
}
