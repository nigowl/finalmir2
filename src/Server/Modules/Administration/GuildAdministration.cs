using Mir2.Management;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public sealed class GuildAdministration(AdminChanges changes)
{
    private static Envir World => Envir.Main;
    private static GuildObject Guild(int id) => World.Guilds.SingleOrDefault(g => g.Guildindex == id) ?? throw new FileNotFoundException("Guild not found.");
    private static void Offline(GuildObject guild)
    {
        foreach (var member in guild.Ranks.SelectMany(r => r.Members))
        {
            var character = World.CharacterList.SingleOrDefault(c => c.Index == member.Id);
            if (character != null) PlayerAdministration.Offline(character.AccountInfo);
        }
    }
    public GuildRow[] List(string? search) => World.Guilds.Where(g => string.IsNullOrWhiteSpace(search) || g.Guildindex.ToString() == search || g.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).OrderBy(g => g.Guildindex)
        .Select(g => new GuildRow(g.Guildindex, g.Name, g.Info.Level, g.Gold, g.Ranks.Sum(r => r.Members.Count), g.GetOnlinePlayers().Count, string.Join(", ", g.Ranks.FirstOrDefault()?.Members.Select(m => m.Name) ?? []))).ToArray();
    public GuildDetails Read(int id)
    {
        var g = Guild(id); var info = g.Info;
        var members = g.Ranks.SelectMany(r => r.Members.Select(m => new GuildMemberRow(m.Id, m.Name, r.Index, r.Name, World.CharacterList.Any(c => c.Index == m.Id && c.Player != null)))).ToArray();
        var items = info.StoredItems.Select((item, slot) => (item, slot)).Where(p => p.item?.Item != null).Select(p => new ManagedItem("GuildStorage", p.slot, p.item.Item.UniqueID.ToString(), p.item.Item.ItemIndex, p.item.Item.Info?.Name ?? "Unknown", p.item.Item.Count, p.item.Item.CurrentDura, p.item.Item.MaxDura)).ToArray();
        var value = new GuildDetails(id, info.Name, info.Level, info.Experience, info.Gold, info.SparePoints, string.Join('\n', info.Notice), info.MemberCap, members, g.Ranks.Select(r => new GuildRankRow(r.Index, r.Name, (int)r.Options)).ToArray(), items, "");
        return value with { Version = AdminChanges.Version(value) };
    }
    public GuildDetails Create(GuildCreate request)
    {
        var name = AdminChanges.Name(request.Name, 20); var leader = PlayerAdministration.Character(request.LeaderId); PlayerAdministration.Offline(leader.AccountInfo);
        if (Settings.Guild_ExperienceList.Count == 0 || Settings.Guild_MembercapList.Count == 0 || Settings.Guild_MembercapList[0] < 1) throw new InvalidOperationException("Configure guild experience and capacity before creating a guild.");
        if (leader.Deleted || leader.GuildIndex != -1 || World.Guilds.Any(g => g.Ranks.Any(r => r.Members.Any(m => m.Id == leader.Index)))) throw new InvalidOperationException("Leader is deleted or already belongs to a guild.");
        if (name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase) || World.Guilds.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Guild name is reserved or already used.");
        // The legacy constructor takes a player; this administrative leader stays offline.
        var info = new GuildInfo(new PlayerObject { Info = leader }, name) { GuildIndex = AdminChanges.Next(World.NextGuildID, World.GuildList.Select(g => g.GuildIndex)) };
        info.Ranks[0].Members[0].Player = null; info.Ranks[0].Members[0].Online = false;
        var counter = World.NextGuildID; GuildObject? created = null;
        changes.Commit(() => { World.GuildList.Add(info); created = new GuildObject(info); leader.GuildIndex = info.GuildIndex; World.NextGuildID = info.GuildIndex; },
            () => { World.GuildList.Remove(info); if (created != null) World.Guilds.Remove(created); leader.GuildIndex = -1; World.NextGuildID = counter; });
        return Read(info.GuildIndex);
    }
    public void Save(GuildEdit edit)
    {
        var guild = Guild(edit.Id); Offline(guild); var before = Read(edit.Id);
        AdminChanges.Match(before.Version, edit.Version); AdminChanges.Confirm(edit.Confirmation, guild.Name); var name = AdminChanges.Name(edit.Name, 20);
        var maxLevel = Math.Min(Settings.Guild_ExperienceList.Count, Settings.Guild_MembercapList.Count) - 1;
        if (before.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The engine-managed newbie guild cannot be edited here.");
        if (edit.Level < 0 || edit.Level > maxLevel || edit.Experience < 0 || edit.SparePoints is < 0 or > 255) throw new InvalidDataException("Invalid guild level, experience or points.");
        if (edit.Level < maxLevel && edit.Experience >= Settings.Guild_ExperienceList[edit.Level]) throw new InvalidDataException("Guild experience exceeds the next-level threshold.");
        if (edit.Notice == null || edit.Notice.Length > Globals.MaxGuildNoticeLength || edit.Notice.Contains('\0') || edit.Notice.Split('\n').Length > Globals.MaxGuildNoticeLines) throw new InvalidDataException("Guild notice exceeds 200 lines or 20000 characters.");
        if (Settings.Guild_MembercapList[edit.Level] < before.Members.Length) throw new InvalidDataException("New guild level cannot hold all current members.");
        if (name != before.Name)
        {
            if (guild.HasGT || guild.Conquest != null || guild.AllyGuilds.Count != 0 || guild.WarringGuilds.Count != 0) throw new InvalidOperationException("Resolve guild territory, alliances and wars before renaming.");
            if (name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase) || before.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase) || World.Guilds.Any(g => g != guild && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Guild name is reserved or already used.");
        }
        changes.Commit(() => Apply(guild.Info, name, edit.Level, edit.Experience, edit.Gold, edit.SparePoints, edit.Notice),
            () => Apply(guild.Info, before.Name, before.Level, before.Experience, before.Gold, before.SparePoints, before.Notice));
    }
    private static void Apply(GuildInfo info, string name, int level, long experience, uint gold, int points, string notice)
    {
        info.Name = name; info.Level = (byte)level; info.Experience = experience; info.Gold = gold; info.SparePoints = (byte)points; info.Notice = notice.Replace("\r", "").Split('\n').ToList();
        info.MaxExperience = Settings.Guild_ExperienceList[level]; info.MemberCap = name == Settings.NewbieGuild ? Settings.NewbieGuildMaxSize : Settings.Guild_MembercapList[level]; info.NeedSave = true;
    }
    public void Member(int id, GuildMemberEdit request)
    {
        if (request.Operation is not ("add" or "remove" or "leader" or "rank")) throw new InvalidDataException("Unknown guild member operation.");
        var guild = Guild(id); Offline(guild); var before = Read(id); AdminChanges.Match(before.Version, request.Version); AdminChanges.Confirm(request.Confirmation, guild.Name);
        if (guild.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The engine manages newbie guild membership.");
        var character = PlayerAdministration.Character(request.CharacterId); PlayerAdministration.Offline(character.AccountInfo);
        if (character.Deleted) throw new InvalidOperationException("Deleted characters cannot join a guild.");
        var member = guild.Ranks.SelectMany(r => r.Members).SingleOrDefault(m => m.Id == character.Index);
        var rank = guild.Ranks.SingleOrDefault(r => r.Members.Contains(member!));
        if (request.Operation == "add")
        {
            if (member != null || character.GuildIndex != -1 || World.Guilds.Any(g => g.Ranks.Any(r => r.Members.Any(m => m.Id == character.Index)))) throw new InvalidOperationException("Character already belongs to a guild.");
            if (before.Members.Length >= guild.Info.MemberCap) throw new InvalidOperationException("Guild is full.");
        }
        else
        {
            if (member == null || rank == null || character.GuildIndex != id) throw new InvalidOperationException("Guild membership is inconsistent or missing.");
            if (request.Operation == "remove" && rank.Index == 0 && rank.Members.Count == 1) throw new InvalidOperationException("Transfer leadership before removing the last leader.");
            if (request.Operation is not ("remove" or "leader" or "rank")) throw new InvalidDataException("Unknown guild member operation.");
            if (request.Operation == "rank" && (request.RankId <= 0 || !guild.Ranks.Any(r => r.Index == request.RankId))) throw new InvalidDataException("Select an existing non-leader rank.");
            if (request.Operation == "rank" && rank.Index == 0 && rank.Members.Count == 1) throw new InvalidOperationException("Transfer leadership before demoting the last leader.");
        }
        var ranks = guild.Ranks.ToList(); var memberships = ranks.Select(r => (rank: r, members: r.Members.ToList())).ToArray(); var oldGuild = character.GuildIndex;
        changes.Commit(() =>
        {
            if (guild.Ranks.Count < 2) guild.Ranks.Add(new GuildRank { Name = "Members", Index = 1 });
            if (request.Operation == "add")
            {
                guild.Ranks[^1].Members.Add(new GuildMember { Id = character.Index, Name = character.Name, LastLogin = World.Now }); character.GuildIndex = id;
            }
            else if (request.Operation == "remove") { rank!.Members.Remove(member!); character.GuildIndex = -1; }
            else if (request.Operation == "leader")
            {
                rank!.Members.Remove(member!); var leaders = guild.Ranks[0];
                foreach (var former in leaders.Members.ToArray()) { leaders.Members.Remove(former); guild.Ranks[1].Members.Add(former); }
                leaders.Members.Add(member!);
            }
            else { rank!.Members.Remove(member!); guild.Ranks.Single(r => r.Index == request.RankId).Members.Add(member!); }
            guild.Info.Membercount = guild.Ranks.Sum(r => r.Members.Count); guild.NeedSave = true;
        }, () => { guild.Ranks = ranks; foreach (var snapshot in memberships) snapshot.rank.Members = snapshot.members; guild.Info.Membercount = before.Members.Length; character.GuildIndex = oldGuild; guild.NeedSave = true; });
    }
    public void Rank(int id, GuildRankEdit request)
    {
        var guild = Guild(id); Offline(guild); var before = Read(id); AdminChanges.Match(before.Version, request.Version); AdminChanges.Confirm(request.Confirmation, guild.Name);
        if (guild.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The engine manages newbie guild ranks.");
        var name = AdminChanges.Name(request.Name, 20);
        if (request.Permissions is < 0 or > 255) throw new InvalidDataException("Permission flags must be between 0 and 255.");
        if (request.RankId == -1)
        {
            if (guild.Ranks.Count >= 254) throw new InvalidOperationException("Guild rank limit reached.");
            var ranks = guild.Ranks.ToList(); var indices = ranks.Select(r => r.Index).ToArray();
            changes.Commit(() =>
            {
                if (guild.Ranks.Count == 1) guild.Ranks.Add(new GuildRank { Index = 1, Name = "Members" });
                guild.Ranks.Insert(guild.Ranks.Count - 1, new GuildRank { Name = name, Options = (GuildRankOptions)request.Permissions });
                for (var i = 0; i < guild.Ranks.Count; i++) guild.Ranks[i].Index = i;
                guild.NeedSave = true;
            }, () => { guild.Ranks = ranks; for (var i = 0; i < ranks.Count; i++) ranks[i].Index = indices[i]; guild.NeedSave = true; });
            return;
        }
        var rank = guild.Ranks.SingleOrDefault(r => r.Index == request.RankId) ?? throw new InvalidDataException("Rank does not exist.");
        if (request.Permissions is < 0 or > 255 || (rank.Index == 0 && request.Permissions != 255)) throw new InvalidDataException("Leader must retain all permissions; flags must be between 0 and 255.");
        var oldName = rank.Name; var options = rank.Options;
        changes.Commit(() => { rank.Name = name; rank.Options = (GuildRankOptions)request.Permissions; guild.NeedSave = true; },
            () => { rank.Name = oldName; rank.Options = options; guild.NeedSave = true; });
    }
    public void Disband(int id, AdminCommand command)
    {
        var guild = Guild(id); Offline(guild); AdminChanges.Match(Read(id).Version, command.Version); AdminChanges.Confirm(command.Confirmation, guild.Name);
        if (guild.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase) || guild.HasGT || guild.Conquest != null || guild.AllyGuilds.Count != 0 || guild.WarringGuilds.Count != 0 || guild.Gold != 0 || guild.Info.StoredItems.Any(i => i?.Item != null))
            throw new InvalidOperationException("Resolve territory, alliances, wars, guild funds and storage before disbanding. The newbie guild cannot be disbanded.");
        var affected = World.CharacterList.Where(c => c.GuildIndex == id).ToArray();
        foreach (var character in affected) PlayerAdministration.Offline(character.AccountInfo);
        var infoPosition = World.GuildList.IndexOf(guild.Info); var livePosition = World.Guilds.IndexOf(guild); var refresh = World.GuildRefreshNeeded;
        if (infoPosition < 0) throw new InvalidOperationException("Guild data is inconsistent.");
        changes.Commit(() => { foreach (var character in affected) character.GuildIndex = -1; World.GuildList.Remove(guild.Info); World.Guilds.Remove(guild); World.GuildRefreshNeeded = true; },
            () => { foreach (var character in affected) character.GuildIndex = id; World.GuildList.Insert(infoPosition, guild.Info); World.Guilds.Insert(livePosition, guild); World.GuildRefreshNeeded = refresh; });
    }
}
