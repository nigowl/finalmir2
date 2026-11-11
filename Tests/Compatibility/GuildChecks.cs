using Crystal.Native;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class GuildChecks
{
    public static void Run(Action<bool, string> check)
    {
        var world = Envir.Main;
        var leader = new GuildPlayer { Info = new CharacterInfo { Index = 88001, Name = "GuildLeader", Level = 100 } };
        var officer = new GuildPlayer { Info = new CharacterInfo { Index = 88002, Name = "GuildOfficer", Level = 100 } };
        var member = new CharacterInfo { Index = 88003, Name = "GuildMember", Level = 100 };
        var guild = new GuildObject(new GuildInfo(leader, "GuildRegression") { GuildIndex = 88000 });
        var higher = new GuildRank { Index = 1, Name = "Officers", Options = GuildRankOptions.CanChangeRank | GuildRankOptions.CanChangeNotice };
        var lower = new GuildRank { Index = 2, Name = "Members" }; var lowest = new GuildRank { Index = 3, Name = "Recruits" };
        higher.Members.Add(new GuildMember { Id = officer.Info.Index, Name = officer.Name, Player = officer }); lower.Members.Add(new GuildMember { Id = member.Index, Name = member.Name });
        guild.Ranks.AddRange(new[] { higher, lower, lowest }); guild.Info.Membercount = 3;
        leader.MyGuild = officer.MyGuild = guild; leader.MyGuildRank = guild.Ranks[0]; officer.MyGuildRank = higher;
        world.CharacterList.AddRange(new[] { leader.Info, officer.Info, member });
        try
        {
            check(!guild.ChangeRank(officer, officer.Name, 0) && higher.Members.Any(m => m.Id == officer.Info.Index), "An officer promoted themselves to leader");
            check(!guild.ChangeRank(officer, member.Name, 0) && !guild.ChangeRank(officer, member.Name, 1), "Officer assigned equal or higher authority");
            check(!guild.ChangeRank(officer, leader.Name, 3), "Officer changed a higher ranked member");
            check(guild.ChangeRank(officer, member.Name, 3) && lowest.Members.Any(m => m.Id == member.Index) && lower.Members.Count == 0, "Officer could not move a subordinate between lower ranks");
            check(guild.ChangeRank(officer, member.Name, 3) && lowest.Members.Count == 1, "Repeated assignment duplicated a member");
            check(!guild.ChangeRankOption(leader, 2, -1, "true") && lower.Options == 0, "Negative permission bit was accepted");
            guild.ChangeRankOption(leader, 2, 1, "true"); guild.ChangeRankOption(leader, 2, 1, "false"); guild.ChangeRankOption(leader, 2, 1, "false");
            check(lower.Options == 0, "Repeated permission removal enabled the permission again");
            check(!guild.ChangeRankOption(officer, 1, 1, "true") && higher.Options == (GuildRankOptions.CanChangeRank | GuildRankOptions.CanChangeNotice), "Officer altered their own permission bits");
            var outsider = new GuildPlayer { Info = new CharacterInfo { Name = "Outsider" } };
            check(!guild.ChangeRankName(outsider, "Forged", 0) && guild.Ranks[0].Name == "Leader", "Unlisted actor renamed a guild rank");
            officer.GuildMembersChanged = true; officer.RequestGuildInfo(1); var count = officer.Packets.OfType<S.GuildMemberChange>().Count(p => p.Status == 255); officer.RequestGuildInfo(1);
            check(!officer.GuildMembersChanged && officer.Packets.OfType<S.GuildMemberChange>().Count(p => p.Status == 255) == count, "Unchanged roster was repeatedly retransmitted");
            guild.NewRank(leader); check(officer.GuildMembersChanged, "Rank insertion did not invalidate an already-read roster");
            guild.NewNotice(new() { "Original" }); officer.EditGuildNotice(new() { new string('x', Globals.MaxGuildNoticeLength + 1) });
            check(guild.Info.Notice.SequenceEqual(new[] { "Original" }), "Oversized guild notice was accepted");
            officer.EditGuildNotice(new() { "valid", "notice" }); check(guild.Info.Notice.SequenceEqual(new[] { "valid", "notice" }), "Authorized notice was rejected");
            officer.GuildMembersChanged = false;
            check(guild.ChangeRank(leader, officer.Name, 2) && officer.GuildMembersChanged && officer.MyGuildRank == lower, "Changed member could not request their new complete roster");
            officer.EditGuildNotice(new() { "forged" }); check(guild.Info.Notice.SequenceEqual(new[] { "valid", "notice" }), "Member without notice rights overwrote the announcement");
            foreach (var invalid in new Action<GuildPlayer>[] { p => p.EnableGuildInvite = false, p => p.Info.GuildIndex = 17, p => p.MyGuild = guild, p => world.Guilds.Remove(guild) })
            {
                var invitee = new GuildPlayer { Info = new CharacterInfo { Name = "Invitee" }, PendingGuildInvite = guild, EnableGuildInvite = true };
                invalid(invitee); invitee.GuildInvite(true);
                check(invitee.PendingGuildInvite == null && guild.Info.Membercount == 3, "Stale, disabled, duplicate or deleted-guild invitation added a member");
                if (!world.Guilds.Contains(guild)) world.Guilds.Add(guild);
            }
            member.GuildIndex = guild.Guildindex; guild.MemberDeleted(member.Name, null!, true);
            check(member.GuildIndex == -1, "Offline guild removal retained a stale character guild ID");
            var state = new GameState(); state.Apply(new S.UserInformation { Inventory = [], Equipment = new UserItem[14], QuestInventory = [] });
            state.Apply(new S.GuildInvite { Name = "GuildRegression" }); check(state.GuildInvitation == "GuildRegression", "Native guild invitation ignored");
            state.Apply(new S.GuildStatus { GuildName = "GuildRegression", GuildRankName = "Member", MemberCount = 3, MyRankId = 2 });
            state.Apply(new S.GuildMemberChange { Status = 255, Ranks = new() { new GuildRank { Index = 0, Members = new() { new GuildMember { Name = "Leader", Online = true } } } } });
            state.Apply(new S.GuildNoticeChange { notice = new() { "Hello" } });
            check(state.InGuild && state.User!.GuildName == "GuildRegression" && !state.GuildNoticeDirty && !state.GuildMembersDirty && state.GuildInvitation == "", "Native status, roster or notice failed to synchronize");
            state.AllowGuildInvitations = true;
            state.Apply(new S.GuildStatus { GuildName = "GuildRegression", GuildRankName = "Member", MemberCount = 3, MyRankId = 2 });
            check(!state.GuildNoticeDirty && !state.GuildMembersDirty, "Status-only update caused unfulfillable repeated roster requests");
            check(state.AllowGuildInvitations, "Guild status-only update changed the runtime invitation preference");
            state.Apply(new S.GuildMemberChange { Status = 0, Name = "Leader" }); check(!state.GuildRanks[0].Members[0].Online, "Guild logout did not update member presence");
            state.Apply(new S.GuildMemberChange { Status = 8, Ranks = new() { new GuildRank { Index = 1, Name = "Officer", Options = GuildRankOptions.CanRecruit } } });
            check(state.Guild!.MyRankId == 1 && state.HasGuildPermission(GuildRankOptions.CanRecruit) && state.User!.GuildRank == "Officer", "Promotion did not update native permissions or own rank");
            state.Apply(new S.GuildNoticeChange { update = -1 }); check(state.GuildNoticeDirty && state.GuildNotice.Single() == "Hello", "Notice invalidation erased the previous announcement");
            state.Apply(new S.GuildStatus()); check(!state.InGuild && state.GuildRanks.Count == 0 && state.GuildNotice.Count == 0 && state.User!.GuildName == "", "Leaving guild leaked metadata or permissions");
            state.Apply(new S.GuildInvite { Name = "Other" }); state.Apply(new S.Disconnect()); check(state.GuildInvitation == "" && !state.InGuild, "Disconnect leaked a guild invitation");
        }
        finally { world.Guilds.Remove(guild); world.CharacterList.Remove(leader.Info); world.CharacterList.Remove(officer.Info); world.CharacterList.Remove(member); }
    }
    private sealed class GuildPlayer : PlayerObject
    {
        public readonly List<Packet> Packets = new();
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void ReceiveChat(string text, ChatType type) { }
        public override void BroadcastInfo() { }
    }
}
