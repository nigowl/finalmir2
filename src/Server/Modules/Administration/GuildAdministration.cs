using Mir2.Management;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public sealed class GuildAdministration(AdminChanges changes)
{
    private static Envir World => Envir.Main;
    private static GuildObject Guild(int id) => World.Guilds.SingleOrDefault(g => g.Guildindex == id) ?? throw new FileNotFoundException("未找到行会。");
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
        var items = info.StoredItems.Select((item, slot) => (item, slot)).Where(p => p.item?.Item != null).Select(p => new ManagedItem("GuildStorage", p.slot, p.item.Item.UniqueID.ToString(), p.item.Item.ItemIndex, p.item.Item.Info?.Name ?? "未知物品", p.item.Item.Count, p.item.Item.CurrentDura, p.item.Item.MaxDura)).ToArray();
        var value = new GuildDetails(id, info.Name, info.Level, info.Experience, info.Gold, info.SparePoints, string.Join('\n', info.Notice), info.MemberCap, members, g.Ranks.Select(r => new GuildRankRow(r.Index, r.Name, (int)r.Options)).ToArray(), items, "");
        return value with { Version = AdminChanges.Version(value) };
    }
    public GuildDetails Create(GuildCreate request)
    {
        var name = AdminChanges.Name(request.Name, 20); var leader = PlayerAdministration.Character(request.LeaderId); PlayerAdministration.Offline(leader.AccountInfo);
        if (Settings.Guild_ExperienceList.Count == 0 || Settings.Guild_MembercapList.Count == 0 || Settings.Guild_MembercapList[0] < 1) throw new InvalidOperationException("创建行会前，请先配置行会经验和人数上限。");
        if (leader.Deleted || leader.GuildIndex != -1 || World.Guilds.Any(g => g.Ranks.Any(r => r.Members.Any(m => m.Id == leader.Index)))) throw new InvalidOperationException("会长角色已删除，或已属于其他行会。");
        if (name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase) || World.Guilds.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("行会名称被保留或已被使用。");
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
        if (before.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("引擎管理的新手行会不能在这里编辑。");
        if (edit.Level < 0 || edit.Level > maxLevel || edit.Experience < 0 || edit.SparePoints is < 0 or > 255) throw new InvalidDataException("行会等级、经验或积分无效。");
        if (edit.Level < maxLevel && edit.Experience >= Settings.Guild_ExperienceList[edit.Level]) throw new InvalidDataException("行会经验超过下一级所需经验。");
        if (edit.Notice == null || edit.Notice.Length > Globals.MaxGuildNoticeLength || edit.Notice.Contains('\0') || edit.Notice.Split('\n').Length > Globals.MaxGuildNoticeLines) throw new InvalidDataException("行会公告不能超过200行或20,000个字符。");
        if (Settings.Guild_MembercapList[edit.Level] < before.Members.Length) throw new InvalidDataException("新行会等级的人数上限不足以容纳现有成员。");
        if (name != before.Name)
        {
            if (guild.HasGT || guild.Conquest != null || guild.AllyGuilds.Count != 0 || guild.WarringGuilds.Count != 0) throw new InvalidOperationException("改名前，请先处理领地、联盟和战争关系。");
            if (name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase) || before.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase) || World.Guilds.Any(g => g != guild && g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("行会名称被保留或已被使用。");
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
        if (request.Operation is not ("add" or "remove" or "leader" or "rank")) throw new InvalidDataException("未知的行会成员操作。");
        var guild = Guild(id); Offline(guild); var before = Read(id); AdminChanges.Match(before.Version, request.Version); AdminChanges.Confirm(request.Confirmation, guild.Name);
        if (guild.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("新手行会的成员由引擎管理。");
        var character = PlayerAdministration.Character(request.CharacterId); PlayerAdministration.Offline(character.AccountInfo);
        if (character.Deleted) throw new InvalidOperationException("已删除的角色不能加入行会。");
        var member = guild.Ranks.SelectMany(r => r.Members).SingleOrDefault(m => m.Id == character.Index);
        var rank = guild.Ranks.SingleOrDefault(r => r.Members.Contains(member!));
        if (request.Operation == "add")
        {
            if (member != null || character.GuildIndex != -1 || World.Guilds.Any(g => g.Ranks.Any(r => r.Members.Any(m => m.Id == character.Index)))) throw new InvalidOperationException("角色已经属于某个行会。");
            if (before.Members.Length >= guild.Info.MemberCap) throw new InvalidOperationException("行会人数已满。");
        }
        else
        {
            if (member == null || rank == null || character.GuildIndex != id) throw new InvalidOperationException("行会成员关系不一致或不存在。");
            if (request.Operation == "remove" && rank.Index == 0 && rank.Members.Count == 1) throw new InvalidOperationException("移除最后一名会长前，请先移交会长职位。");
            if (request.Operation is not ("remove" or "leader" or "rank")) throw new InvalidDataException("未知的行会成员操作。");
            if (request.Operation == "rank" && (request.RankId <= 0 || !guild.Ranks.Any(r => r.Index == request.RankId))) throw new InvalidDataException("请选择现有的非会长职位。");
            if (request.Operation == "rank" && rank.Index == 0 && rank.Members.Count == 1) throw new InvalidOperationException("降低最后一名会长的职位前，请先移交会长职位。");
        }
        var ranks = guild.Ranks.ToList(); var memberships = ranks.Select(r => (rank: r, members: r.Members.ToList())).ToArray(); var oldGuild = character.GuildIndex;
        changes.Commit(() =>
        {
            if (guild.Ranks.Count < 2) guild.Ranks.Add(new GuildRank { Name = "会员", Index = 1 });
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
        if (guild.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("新手行会的职位由引擎管理。");
        var name = AdminChanges.Name(request.Name, 20);
        if (request.Permissions is < 0 or > 255) throw new InvalidDataException("权限数值必须为0至255。");
        if (request.RankId == -1)
        {
            if (guild.Ranks.Count >= 254) throw new InvalidOperationException("行会职位数量已达上限。");
            var ranks = guild.Ranks.ToList(); var indices = ranks.Select(r => r.Index).ToArray();
            changes.Commit(() =>
            {
                if (guild.Ranks.Count == 1) guild.Ranks.Add(new GuildRank { Index = 1, Name = "会员" });
                guild.Ranks.Insert(guild.Ranks.Count - 1, new GuildRank { Name = name, Options = (GuildRankOptions)request.Permissions });
                for (var i = 0; i < guild.Ranks.Count; i++) guild.Ranks[i].Index = i;
                guild.NeedSave = true;
            }, () => { guild.Ranks = ranks; for (var i = 0; i < ranks.Count; i++) ranks[i].Index = indices[i]; guild.NeedSave = true; });
            return;
        }
        var rank = guild.Ranks.SingleOrDefault(r => r.Index == request.RankId) ?? throw new InvalidDataException("职位不存在。");
        if (request.Permissions is < 0 or > 255 || (rank.Index == 0 && request.Permissions != 255)) throw new InvalidDataException("会长必须保留全部权限，权限数值必须为0至255。");
        var oldName = rank.Name; var options = rank.Options;
        changes.Commit(() => { rank.Name = name; rank.Options = (GuildRankOptions)request.Permissions; guild.NeedSave = true; },
            () => { rank.Name = oldName; rank.Options = options; guild.NeedSave = true; });
    }
    public void Disband(int id, AdminCommand command)
    {
        var guild = Guild(id); Offline(guild); AdminChanges.Match(Read(id).Version, command.Version); AdminChanges.Confirm(command.Confirmation, guild.Name);
        if (guild.Name.Equals(Settings.NewbieGuild, StringComparison.OrdinalIgnoreCase) || guild.HasGT || guild.Conquest != null || guild.AllyGuilds.Count != 0 || guild.WarringGuilds.Count != 0 || guild.Gold != 0 || guild.Info.StoredItems.Any(i => i?.Item != null))
            throw new InvalidOperationException("解散前，请先处理领地、联盟、战争、行会资金和仓库。新手行会不能解散。");
        var affected = World.CharacterList.Where(c => c.GuildIndex == id).ToArray();
        foreach (var character in affected) PlayerAdministration.Offline(character.AccountInfo);
        var infoPosition = World.GuildList.IndexOf(guild.Info); var livePosition = World.Guilds.IndexOf(guild); var refresh = World.GuildRefreshNeeded;
        if (infoPosition < 0) throw new InvalidOperationException("行会数据不一致。");
        changes.Commit(() => { foreach (var character in affected) character.GuildIndex = -1; World.GuildList.Remove(guild.Info); World.Guilds.Remove(guild); World.GuildRefreshNeeded = true; },
            () => { foreach (var character in affected) character.GuildIndex = id; World.GuildList.Insert(infoPosition, guild.Info); World.Guilds.Insert(livePosition, guild); World.GuildRefreshNeeded = refresh; });
    }
}
