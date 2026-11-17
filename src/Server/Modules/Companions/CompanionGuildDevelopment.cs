using Mir2.Companions;
using Server.Helpers;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionGuildDevelopment
{
    private static PlayerObject? Player(CompanionRecord record) => Envir.Main.CharacterList.FirstOrDefault(c => c.Index == record.CharacterId)?.Player;
    private static bool Joining(CompanionProfile profile) => profile.Guilds.Mode is "join" or "auto";
    private static bool Named(CompanionProfile profile, GuildObject guild) => profile.Guilds.Name.Length == 0 || profile.Guilds.Name.Equals(guild.Name, StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<PlayerObject> Officers(GuildObject guild) => guild.GetOnlinePlayers().Where(p => !p.Dead && p.MyGuildRank.Options.HasFlag(GuildRankOptions.CanRecruit));
    private static string FoundingName(CompanionProfile profile) => profile.Guilds.Name.Length > 0 ? profile.Guilds.Name : profile.Name[..Math.Min(18, profile.Name.Length)] + "同盟";

    private static GuildObject? SelectGuild(CompanionProfile profile, CompanionRecord[] records)
    {
        var companions = records.Select(Player).Where(p => p != null).ToHashSet();
        return Envir.Main.Guilds.Where(g => Named(profile, g) && g.HasRoom() && !g.IsAtWar() && Officers(g).Any())
            .OrderByDescending(g => Officers(g).Any(p => companions.Contains(p))).ThenBy(g => g.Guildindex).FirstOrDefault();
    }

    public static KnownGuildPlan? Read(PlayerObject player, CompanionRecord record, CompanionRecord[] records, ISet<string>? blocked, bool allowPk)
    {
        var profile = record.Profile;
        if (profile.Guilds.Mode == "off" || player.Dead) return null;
        var active = records.Where(r => r.Profile.Enabled && r.Desired == "run" && Player(r) is { Dead:false }).ToArray();
        if (player.MyGuild is { } own)
        {
            if (profile.Guilds.Siege && allowPk && CompanionGuildSieges.Plan(player, blocked) is { } siege) return siege;
            if (!own.IsAtWar() && own.HasRoom() && player.MyGuildRank.Options.HasFlag(GuildRankOptions.CanRecruit))
            {
                var candidate = active.Where(r => Joining(r.Profile) && Player(r) is { MyGuild:null, EnableGuildInvite:true })
                    .Where(r => SelectGuild(r.Profile, active) == own).OrderBy(r => r.Profile.Id).FirstOrDefault();
                if (candidate != null) return new("invite", own.Name, candidate.Profile.Name, "招收主动申请的陪玩成员", player.EnableGuildInvite);
            }
            return new("member", own.Name, "", profile.Guilds.Siege && !allowPk ? "攻城等待开启陪玩PK总开关" : "随正常战斗发展行会，等待招募或攻城", player.EnableGuildInvite);
        }
        if (Joining(profile) && SelectGuild(profile, active) is { } guild)
        {
            var officer = Officers(guild).OrderBy(p => p.Info.Index).First();
            return new("join", guild.Name, officer.Name, "主动申请加入行会：" + guild.Name, player.EnableGuildInvite);
        }
        if (profile.Guilds.Mode == "join") return new("wait", profile.Guilds.Name, "", "等待目标行会有可招收的在线成员和名额", player.EnableGuildInvite);
        return Found(player, record, active, blocked);
    }

    private static string? CreationFailure(PlayerObject player, CompanionProfile profile)
    {
        var name = FoundingName(profile);
        if (Envir.Main.GetGuild(name) != null) return "拟建行会名称已存在，等待招收或调整名称";
        if (GuildCreationRules.Failure(player, name) is { } failure) return failure;
        var cost = Server.Settings.Guild_CreationCostList.Where(c => c.Item == null).Sum(c => (long)c.Amount);
        return cost > player.Account.Gold / 2L ? "建会预算不足：需保留至少半数金币用于发育与补给" : null;
    }

    private static KnownGuildPlan Found(PlayerObject player, CompanionRecord record, CompanionRecord[] active, ISet<string>? blocked)
    {
        var profile = record.Profile; var name = FoundingName(profile);
        KnownGuildPlan Wait(string reason) => new("wait", name, "", reason, player.EnableGuildInvite);
        if (CreationFailure(player, profile) is { } failure) return Wait(failure);
        var founder = active.Where(r => r.Profile.Guilds.Mode is "create" or "auto" && r.Profile.Guilds.Name.Equals(profile.Guilds.Name, StringComparison.OrdinalIgnoreCase))
            .Where(r => Player(r) is { MyGuild:null } candidate && CreationFailure(candidate, r.Profile) == null).OrderBy(r => r.Profile.Id).FirstOrDefault();
        if (founder != null && founder.Profile.Id != profile.Id) return Wait("等待同一发展组的建会发起者完成建会");
        var routes = CompanionPlanning.Routes(player.CurrentMap.Info.Index, Envir.Main.MapInfoList, player.GroupMembers?.Count ?? 0);
        var npc = Envir.Main.NPCs.Where(n => n.Visible && n.CurrentMap != null && routes.ContainsKey(n.CurrentMap.Info.Index) &&
                (!n.VisibleLog.TryGetValue(player.Info.Index, out var visible) || visible) && blocked?.Contains($"guild/create/{n.ObjectID}") != true &&
                NPCScript.Get(n.ScriptID).NPCPages.Any(page => page.Key.Equals(NPCScript.GuildCreateKey, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n.CurrentMap == player.CurrentMap ? Functions.MaxDistance(n.CurrentLocation, player.CurrentLocation) : 10000).FirstOrDefault();
        if (npc == null) return Wait("当前版本没有公开可达的建会NPC，继续正常发育");
        var goal = new KnownGoal("guild-create", npc.CurrentMap.Info.Index, npc.CurrentLocation.X, npc.CurrentLocation.Y,
            "前往建会NPC：" + npc.Name, NpcId:npc.ObjectID, NextExit:routes[npc.CurrentMap.Info.Index], Key:$"guild/create/{npc.ObjectID}");
        return new("create", name, "", "筹备创建行会：" + name, player.EnableGuildInvite, goal);
    }
}
