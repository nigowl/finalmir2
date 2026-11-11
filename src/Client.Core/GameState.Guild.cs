using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    public S.GuildStatus? Guild;
    public readonly List<string> GuildNotice = new();
    public readonly List<GuildRank> GuildRanks = new();
    public string GuildInvitation = "";
    public bool AllowGuildInvitations, GuildNoticeDirty, GuildMembersDirty;
    public int GuildRevision;
    public bool InGuild => Guild?.GuildName.Length > 0;
    public bool HasGuildPermission(GuildRankOptions permission) => InGuild && Guild!.MyOptions.HasFlag(permission);

    private void ClearGuild()
    {
        ClearGuildStorage();
        Guild = null; GuildNotice.Clear(); GuildRanks.Clear(); GuildInvitation = "";
        AllowGuildInvitations = GuildNoticeDirty = GuildMembersDirty = false;
        GuildRevision++;
    }
    private void ApplyGuild(Packet packet)
    {
        switch (packet)
        {
            case S.UserInformation: ClearGuild(); break;
            case S.GuildInvite p: GuildInvitation = p.Name; GuildRevision++; break;
            case S.GuildStatus p:
                var changed = Guild?.GuildName != p.GuildName;
                var allowInvites = AllowGuildInvitations;
                if (changed) ClearGuild();
                Guild = p.GuildName.Length == 0 ? null : p;
                if (User != null) { User.GuildName = p.GuildName; User.GuildRank = p.GuildRankName; }
                AllowGuildInvitations = InGuild && changed ? false : allowInvites;
                if (InGuild) { GuildInvitation = ""; if (changed) GuildNoticeDirty = GuildMembersDirty = true; }
                GuildRevision++; break;
            case S.GuildNoticeChange p:
                if (!InGuild) break;
                if (p.update < 0) GuildNoticeDirty = true;
                else { GuildNotice.Clear(); GuildNotice.AddRange(p.notice); GuildNoticeDirty = false; }
                GuildRevision++; break;
            case S.GuildMemberChange p:
                if (!InGuild) break;
                if (p.Status is 0 or 1)
                {
                    foreach (var member in GuildRanks.SelectMany(r => r.Members).Where(m => m.Name == p.Name)) member.Online = p.Status == 1;
                    if (!GuildRanks.Any(r => r.Members.Any(m => m.Name == p.Name))) GuildMembersDirty = true;
                }
                else if (p.Status == 255)
                {
                    GuildRanks.Clear(); GuildRanks.AddRange(p.Ranks.OrderBy(r => r.Index)); GuildMembersDirty = false;
                    if (GuildRanks.FirstOrDefault(r => r.Members.Any(m => m.Name == User?.Name)) is { } ownRank) ApplyGuildRank(ownRank);
                }
                else
                {
                    GuildMembersDirty = true;
                    if (p.Status is 2 or 3 or 4) Guild!.MemberCount = Math.Max(0, Guild.MemberCount + (p.Status == 2 ? 1 : -1));
                    var rank = p.Status == 8 ? p.Ranks.FirstOrDefault() : p.Status == 7 ? p.Ranks.FirstOrDefault(r => r.Index == Guild!.MyRankId) : null;
                    if (rank != null) ApplyGuildRank(rank);
                }
                GuildRevision++; break;
            case S.GuildExpGain p: if (Guild != null) { Guild.Experience += p.Amount; GuildRevision++; } break;
        }
    }
    private void ApplyGuildRank(GuildRank rank)
    {
        Guild!.MyRankId = rank.Index; Guild.MyOptions = rank.Options; Guild.GuildRankName = rank.Name;
        if (User != null) User.GuildRank = rank.Name;
    }
}
