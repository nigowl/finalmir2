using Mir2.Companions;
using Server.Helpers;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionGuildDevelopment
{
    // 登出残留引用不等于在线，必须从原引擎当前玩家名单确认。
    internal static PlayerObject? Player(CompanionRecord record) =>
        Envir.Main.CharacterList.FirstOrDefault(c=>c.Index==record.CharacterId)?.Player is { Node:not null } player &&
        Envir.Main.Players.Contains(player) ? player : null;
    internal static bool Active(CompanionRecord record)
    {
        var now = DateTimeOffset.UtcNow;
        // 日程上线不要求手动启动，人工暂停、下线抑制和原保护仍须排除。
        return record.Profile.Enabled && record.Error.Length == 0 && record.Desired != "pause" &&
            (record.Profile.Scheduled || record.Desired == "run") && !(record.SuppressedUntil > now) &&
            !(record.RestUntil > now) && !(record.SessionUntil <= now) && Player(record) is { Dead:false };
    }
    private static bool Joining(CompanionProfile profile) => profile.Guilds.Mode is "join" or "auto";
    private static bool Named(CompanionProfile profile, GuildObject guild) => profile.Guilds.Name.Length == 0 || profile.Guilds.Name.Equals(guild.Name, StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<PlayerObject> Officers(GuildObject guild) => guild.GetOnlinePlayers().Where(p => !p.Dead && p.MyGuildRank.Options.HasFlag(GuildRankOptions.CanRecruit));
    private static string FoundingName(CompanionProfile profile) => profile.Guilds.Name.Length > 0 ? profile.Guilds.Name : profile.Name[..Math.Min(18, profile.Name.Length)] + "同盟";

    private static int Group(CompanionProfile profile) => profile.Id.ToByteArray()[0] % 3;
    private static int? OwnerGroup(GuildObject guild, CompanionRecord[] records) =>
        records.FirstOrDefault(r => r.CharacterId > 0 && guild.Ranks.FirstOrDefault()?.Members.FirstOrDefault()?.Id == r.CharacterId) is { } leader ? Group(leader.Profile) : null;
    private static IEnumerable<GuildObject> SelectGuilds(CompanionProfile profile, CompanionRecord[] records)
    {
        var companions = records.Select(Player).Where(p => p != null).ToHashSet();
        return Envir.Main.Guilds.Where(g => Named(profile, g) && g.HasRoom() && !g.IsAtWar() && Officers(g).Any())
            .Where(g => profile.Guilds.Mode != "auto" || profile.Guilds.Name.Length > 0 ||
                OwnerGroup(g, records) is not { } group || group == Group(profile))
            .OrderByDescending(g => Officers(g).Any(p => companions.Contains(p))).ThenBy(g => g.Info.Membercount).ThenBy(g => g.Guildindex);
    }

    public static string ChoiceKey(KnownGuildPlan plan) => plan.Action == "join" ? "join:" + Envir.Main.GetGuild(plan.Name)?.Guildindex :
        plan.Action is "create" or "siege" or "register" or "rally" ? plan.Action : "develop";
    public static KnownGuildPlan? Read(PlayerObject player, CompanionRecord record, CompanionRecord[] records, ISet<string>? blocked, bool allowPk, string preference = "", IReadOnlyDictionary<Guid,string>? preferences = null)
    {
        var choices = Choices(player, record, records, blocked, allowPk, preferences);
        return choices.FirstOrDefault(plan => ChoiceKey(plan) == preference) ?? choices.FirstOrDefault();
    }
    public static KnownGuildPlan[] Choices(PlayerObject player, CompanionRecord record, CompanionRecord[] records, ISet<string>? blocked, bool allowPk, IReadOnlyDictionary<Guid,string>? preferences = null)
    {
        var profile = record.Profile;
        if (profile.Guilds.Mode == "off" || player.Dead) return [];
        var active = records.Where(Active).ToArray();
        if (player.MyGuild is { } own)
        {
            var siege = profile.Guilds.Siege && allowPk ? CompanionGuildSieges.Plan(player, blocked) ?? CompanionGuildSieges.Organize(player,records,blocked) : null;
            KnownGuildPlan normal = new("member", own.Name, "", profile.Guilds.Siege && !allowPk ? "攻城等待开启陪玩PK总开关" : "随正常战斗发展行会，等待招募或攻城", player.EnableGuildInvite);
            if (!own.IsAtWar() && own.HasRoom() && player.MyGuildRank.Options.HasFlag(GuildRankOptions.CanRecruit))
            {
                var candidate = active.Where(r => Joining(r.Profile) && Player(r) is { MyGuild:null, EnableGuildInvite:true })
                    .Where(r => preferences?.GetValueOrDefault(r.Profile.Id) != "develop" &&
                        (SelectGuilds(r.Profile, records).FirstOrDefault(g => "join:"+g.Guildindex == preferences?.GetValueOrDefault(r.Profile.Id)) ??
                         SelectGuilds(r.Profile, records).FirstOrDefault()) == own).OrderBy(r => r.Profile.Id).FirstOrDefault();
                if (candidate != null) normal = new("invite", own.Name, candidate.Profile.Name, "招收主动申请的陪玩成员", player.EnableGuildInvite);
            }
            return siege == null ? [normal] : [siege, normal];
        }
        var guilds = Joining(profile) ? SelectGuilds(profile, records).Take(3).ToArray() : [];
        if (guilds.Length > 0)
        {
            return guilds.Select(guild => new KnownGuildPlan("join", guild.Name, Officers(guild).OrderBy(p => p.Info.Index).First().Name,
                "主动申请加入行会：" + guild.Name, player.EnableGuildInvite)).Append(new("develop", "", "", "暂缓入会，先积累装备与补给", player.EnableGuildInvite)).ToArray();
        }
        if (profile.Guilds.Mode == "join") return [new("wait", profile.Guilds.Name, "", "等待目标行会有可招收的在线成员和名额", player.EnableGuildInvite)];
        if (profile.Guilds.Mode == "auto" && profile.Guilds.Name.Length == 0 &&
            Envir.Main.Guilds.Any(g => OwnerGroup(g, records) == Group(profile) && g.HasRoom()))
            return [new("wait", "", "", "等待同阵营行会可招收的成员上线或战争结束，继续正常发育", player.EnableGuildInvite)];
        // 自动策略按稳定编号分三股发展，离线、重启和临时名额变化不会把所有人重新并到首会。
        var found = Found(player, record, active, blocked);
        return found.Action == "create" ? [found, new("develop", "", "", "暂缓建会，先积累装备与补给", player.EnableGuildInvite)] : [found];
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
        if (player.Level < Server.Settings.Guild_RequiredLevel) return Wait($"建会需{Server.Settings.Guild_RequiredLevel}级，继续正常发育");
        if (Envir.Main.GetGuild(name) != null) return Wait("拟建行会名称已存在，等待招收或调整名称");
        bool Invested(CompanionRecord r) => Envir.Main.CharacterList.First(c=>c.Index==r.CharacterId) is { } c &&
            c.Inventory.Concat(c.QuestInventory).Any(i=>i != null && Server.Settings.Guild_CreationCostList.Any(cost=>cost.Item != null && cost.Item==i.Info));
        var founder = active.Where(r => r.Profile.Guilds.Mode is "create" or "auto" &&
            (FoundingName(r.Profile).Equals(name, StringComparison.OrdinalIgnoreCase) || profile.Guilds.Mode == "auto" && r.Profile.Guilds.Mode == "auto" &&
                profile.Guilds.Name.Length == 0 && r.Profile.Guilds.Name.Length == 0 && Group(r.Profile) == Group(profile)))
            .Where(r => Envir.Main.CharacterList.FirstOrDefault(c => c.Index == r.CharacterId) is { GuildIndex:<=0 } candidate &&
                candidate.Level >= Server.Settings.Guild_RequiredLevel)
            .OrderByDescending(Invested)
            .ThenByDescending(r=>Envir.Main.CharacterList.First(c=>c.Index==r.CharacterId).AccountInfo.Gold).ThenBy(r=>r.Profile.Id).FirstOrDefault();
        if (founder != null && founder.Profile.Id != profile.Id) return Wait("等待同组"+founder.Profile.Name+"筹建行会，继续正常发育");
        var routes = CompanionPlanning.Routes(player.CurrentMap.Info.Index, Envir.Main.MapInfoList, player.GroupMembers?.Count ?? 0);
        var npc = Envir.Main.NPCs.Where(n => n.Visible && n.CurrentMap != null && routes.ContainsKey(n.CurrentMap.Info.Index) &&
                (!n.VisibleLog.TryGetValue(player.Info.Index, out var visible) || visible) && blocked?.Contains($"guild/create/{n.ObjectID}") != true &&
                NPCScript.Get(n.ScriptID).NPCPages.Any(page => page.Key.Equals(NPCScript.GuildCreateKey, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n.CurrentMap == player.CurrentMap ? Functions.MaxDistance(n.CurrentLocation, player.CurrentLocation) : 10000).FirstOrDefault();
        if (npc == null) return Wait("当前版本没有公开可达的建会NPC，继续正常发育");
        var offers = player.PublicShopOffers().ToArray();
        var missing = Server.Settings.Guild_CreationCostList.Where(c => c.Item != null)
            .Select(c => (Item:c.Item, Count:GuildCreationRules.Missing(player,c.Item,c.Amount))).Where(c => c.Count > 0).ToArray();
        var purchases = missing.Select(c => (c.Item,c.Count,Offer:offers.FirstOrDefault(o => o.ItemIndex == c.Item.Index))).ToArray();
        if (purchases.Any(c => c.Item.Type != ItemType.CraftingMaterial || c.Offer == null || c.Offer.Price == 0))
            return Wait("缺少建会材料且当前商城无合适货架，保留原建会条件继续发育");
        var reserve = GuildCreationRules.GoldCost * 2;
        var target = reserve + purchases.Sum(c => (long)c.Offer!.Price * c.Count);
        if (target > uint.MaxValue) return Wait("当前建会费用超过可安全积累的金币上限");
        if (player.Account.Gold < target)
            return new("prepare",name,"",$"建会筹资：{player.Account.Gold:N0}/{target:N0}金币，保留建会费与补给预算",player.EnableGuildInvite,
                new("guild-fund",player.CurrentMap.Info.Index,player.CurrentLocation.X,player.CurrentLocation.Y,"积累建会资金",Key:"guild/fund"));
        if (purchases.FirstOrDefault() is { Offer:not null } purchase)
        {
            if (blocked?.Contains("public-guild-material") == true)
                return new("prepare",name,"","建会材料采购暂不可用，保留资金等待重新核对",player.EnableGuildInvite,
                    new("wait",player.CurrentMap.Info.Index,player.CurrentLocation.X,player.CurrentLocation.Y,"保留资金，等待重新核对建会材料采购",Key:"fund/guild-material-wait"));
            return new("prepare",name,"","原商城购买建会材料：" + purchase.Item.FriendlyName,player.EnableGuildInvite,
                new("service",player.CurrentMap.Info.Index,player.CurrentLocation.X,player.CurrentLocation.Y,"购买建会材料",Key:"public-guild-material",
                    PublicItemId:purchase.Item.Index,PublicCount:purchase.Count+player.CarriedItems.Where(i=>i.ItemIndex==purchase.Item.Index).Sum(i=>(int)i.Count),GoldReserve:reserve));
        }
        if (CreationFailure(player, profile) is { } failure) return Wait(failure);
        var goal = new KnownGoal("guild-create", npc.CurrentMap.Info.Index, npc.CurrentLocation.X, npc.CurrentLocation.Y,
            "前往建会NPC：" + npc.Name, NpcId:npc.ObjectID, NextExit:routes[npc.CurrentMap.Info.Index], Key:$"guild/create/{npc.ObjectID}");
        return new("create", name, "", "筹备创建行会：" + name, player.EnableGuildInvite, goal);
    }
}
