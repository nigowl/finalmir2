using Godot;
using System.Net.Http.Headers;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyGuild(GameConnection bot, GameState other, List<Packet> received, Func<Func<bool>, Task> botUntil)
    {
        var frame = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "关闭行会 H").GetParent<Control>();
        var background = frame.GetChildren().OfType<TextureRect>().Single();
        if (frame.GetThemeStylebox("panel") is not StyleBoxEmpty || frame.Size != new Vector2(590, 432) || background.Size != frame.Size)
            throw new Exception("行会原图和面板尺寸不一致，底部仍会叠画第二条边框。");
        stage = "native guild leader fixture login";
        using var leader = new GameConnection(); var lead = new GameState(); var leadPackets = new List<Packet>();
        async Task LeadUntil(Func<bool> condition)
        {
            await Until(() =>
            {
                while (leader.Received.TryDequeue(out var packet)) { lead.Apply(packet); leadPackets.Add(packet); }
                if (leader.Errors.TryDequeue(out var error)) throw new IOException(error);
                return condition();
            });
        }
        async Task<T> LeadPacket<T>() where T : Packet
        {
            await LeadUntil(() => leadPackets.Any(p => p is T)); var packet = leadPackets.OfType<T>().First(); leadPackets.Remove(packet); return packet;
        }
        await ToSignal(GetTree().CreateTimer(5.2), SceneTreeTimer.SignalName.Timeout);
        await leader.Connect(Client.Assets.Host, Client.Assets.GamePort); await LeadPacket<S.Connected>();
        await leader.Send(new C.ClientVersion { VersionHash = [] }); await LeadPacket<S.ClientVersion>();
        await leader.Send(new C.Login { AccountID = "NativeGuild", Password = "Native123" }); await LeadPacket<S.LoginSuccess>();
        await leader.Send(new C.StartGame { CharacterIndex = lead.Characters.Single().Index }); await LeadPacket<S.UserInformation>();
        await LeadUntil(() => lead.InGuild);
        stage = "native guild invitation consent";
        Client.OpenGuild(); Client.ToggleGuildInvitations();
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        await leader.Send(new C.EditGuildMember { ChangeType = 0, Name = "NativeWarrior" }); await Until(() => State.GuildInvitation == "NativeGuild");
        Client.RespondGuildInvitation(false); await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        if (State.InGuild) throw new Exception("Declined guild invitation changed membership.");
        await leader.Send(new C.EditGuildMember { ChangeType = 0, Name = "NativeWarrior" }); await Until(() => State.GuildInvitation == "NativeGuild");
        Client.RespondGuildInvitation(true); await Until(() => State.InGuild && State.GuildRanks.Sum(r => r.Members.Count) == 2 && State.GuildNotice.Count > 0);
        if (State.Guild!.MyRankId != 2 || State.Guild.MyOptions != 0 || State.GuildNotice[0] != "Native guild fixture.") throw new Exception("Guild join lost rank, permissions or notice.");
        stage = "native guild permission rejection and promotion";
        packets.RemoveAll(p => p is S.Chat);
        Client.Send(new C.EditGuildNotice { notice = new() { "unauthorized change" } }); await Packet<S.Chat>();
        if (State.GuildNotice[0] != "Native guild fixture.") throw new Exception("Member overwrote the notice without permission.");
        await leader.Send(new C.EditGuildMember { ChangeType = 2, Name = "NativeWarrior", RankIndex = 0 });
        await Until(() => State.Guild.MyRankId == 0 && State.HasGuildPermission(GuildRankOptions.CanChangeNotice));
        await Until(() => !State.GuildMembersDirty && State.GuildRanks.Single(r => r.Index == 0).Members.Any(m => m.Name == "NativeWarrior"));
        stage = "native guild notice editing and propagation";
        Client.SelectGuildPage(0); Client.BeginGuildNoticeEdit(); Client.SetGuildNoticeDraft("Native guild saved notice.\n原协议行会公告"); Client.SaveGuildNotice();
        await Until(() => State.GuildNotice.SequenceEqual(new[] { "Native guild saved notice.", "原协议行会公告" }));
        await leader.Send(new C.RequestGuildInfo { Type = 0 }); await LeadUntil(() => lead.GuildNotice.SequenceEqual(State.GuildNotice));
        Client.BeginGuildNoticeEdit(); Client.SetGuildNoticeDraft("Preserved draft");
        await leader.Send(new C.EditGuildNotice { notice = new() { "Updated elsewhere" } });
        await Until(() => State.GuildNotice.SequenceEqual(new[] { "Updated elsewhere" }));
        Client.SaveGuildNotice();
        if (Client.GuildNoticeDraft != "Preserved draft") throw new Exception("External announcement update discarded the in-progress draft.");
        Client.ReloadGuildNotice(); Client.BeginGuildNoticeEdit(); Client.SetGuildNoticeDraft("Native guild saved notice.\n原协议行会公告"); Client.SaveGuildNotice();
        await Until(() => State.GuildNotice.Count == 2);
        await Screenshot("guild-notice");
        stage = "native guild recruitment, ranks and chat";
        await bot.Send(new C.Chat { Message = "@ALLOWGUILD" }); await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        Client.InviteGuildMember("NativeMate"); await botUntil(() => other.GuildInvitation == "NativeGuild");
        await bot.Send(new C.GuildInvite { AcceptInvite = true }); await botUntil(() => other.InGuild);
        await Until(() => State.GuildRanks.Sum(r => r.Members.Count) == 3);
        Client.ChangeGuildMemberRank("NativeMate", 1); await botUntil(() => other.Guild!.MyRankId == 1 && other.HasGuildPermission(GuildRankOptions.CanChangeNotice));
        await Until(() => State.GuildRanks.Single(r => r.Index == 1).Members.Any(m => m.Name == "NativeMate"));
        Client.SetGuildRankPermission(1, 6, false); await botUntil(() => !other.HasGuildPermission(GuildRankOptions.CanChangeNotice));
        Client.SetGuildRankPermission(1, 6, false);
        received.RemoveAll(p => p is S.GuildStatus); await botUntil(() => received.OfType<S.GuildStatus>().Any());
        if (other.HasGuildPermission(GuildRankOptions.CanChangeNotice)) throw new Exception("Repeated permission removal restored notice rights.");
        Client.SetGuildRankPermission(1, 6, true); await botUntil(() => other.HasGuildPermission(GuildRankOptions.CanChangeNotice));
        Client.Send(new C.Chat { Message = "!~Native guild chat" }); await botUntil(() => received.OfType<S.Chat>().Any(p => p.Type == ChatType.Guild && p.Message.Contains("Native guild chat")));
        Client.SelectGuildPage(1); await Screenshot("guild-members"); Client.SelectGuildPage(2); await Screenshot("guild-ranks");
        var tabs = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Where(button => button.Library == "Title" && button.Position.Y == 34 && button.TooltipText is "公告" or "成员" or "职位与权限" or "行会仓库与资金（仅安全区）").ToArray();
        if (tabs.Length != 4 || tabs.Any(button => button.HoverIndex != button.Index || button.PressedIndex != (button.TooltipText switch { "公告" => 94, "成员" => 100, "职位与权限" => 102, _ => 106 })) || tabs.Single(button => button.TooltipText == "职位与权限").Index != 102)
            throw new Exception("Guild tabs selected adjacent unrelated art instead of the original two-state images.");
        await VerifyGuildStorage(bot, other, received, botUntil);
        using (var http = new System.Net.Http.HttpClient())
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("MIR2_RUNTIME_ROOT")!, "admin.key")).Trim());
            using var status = JsonDocument.Parse(await http.GetStringAsync(Argument("--server").TrimEnd('/') + "/management/status"));
            var players = status.RootElement.GetProperty("players").EnumerateArray().Where(p => p.GetProperty("name").GetString() is "NativeMate" or "NativeWarrior").ToArray();
            if (players.Length != 2 || players.Any(p => p.GetProperty("guildName").GetString() != "NativeGuild") || players.Single(p => p.GetProperty("name").GetString() == "NativeMate").GetProperty("guildRank").GetString() != "Officers") throw new Exception("Management guild/rank monitor diverged from gameplay.");
        }
        stage = "native guild kick, leave and rejoin";
        Client.KickGuildMember("NativeMate"); await botUntil(() => !other.InGuild); await Until(() => State.GuildRanks.Sum(r => r.Members.Count) == 2);
        Client.LeaveGuild(); await Until(() => !State.InGuild && State.GuildRanks.Count == 0 && State.GuildNotice.Count == 0);
        Client.ToggleGuildInvitations(); await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        await leader.Send(new C.EditGuildMember { ChangeType = 0, Name = "NativeWarrior" }); await Until(() => State.GuildInvitation == "NativeGuild");
        Client.RespondGuildInvitation(true); await Until(() => State.InGuild && State.GuildRanks.Sum(r => r.Members.Count) == 2 && State.GuildNotice.Count == 2);
        await leader.Send(new C.EditGuildMember { ChangeType = 5, RankIndex = 2, RankName = "3", Name = "true" });
        await Until(() => State.HasGuildPermission(GuildRankOptions.CanStoreItem));
        stage = "native guild online status cleanup";
        await leader.Send(new C.Disconnect()); await Until(() => State.GuildRanks.SelectMany(r => r.Members).Any(m => m.Name == "NativeGuildLead" && !m.Online));
        using var close = new InputEventKey { PhysicalKeycode = Key.H, Pressed = true }; Client._UnhandledInput(close);
        GD.Print("PASS native guilds: original art, consent/decline, notice permission and save/propagation, promotion and member list, recruitment/kick, guild chat, leave/rejoin, online/offline cleanup, management guild/rank monitoring.");
    }
}
