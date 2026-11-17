using System.Text.RegularExpressions;
using Mir2.Companions;
using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private bool guildNameRequested;
    private long guildPromptAt;
    private string guildPromptName = "";
    private string pendingGuildName = "";
    private long nextGuildAction, nextGuildRequest, nextGuildInvite;

    private void GuildPacket(Packet packet)
    {
        if (packet is S.GuildNameRequest && Knowledge?.Goal is { Kind:"guild-create" } goal && npcId == goal.NpcId &&
            goal.MapId == Knowledge.MapId && Knowledge.Guild is { Action:"create" } plan)
        { guildNameRequested = true; guildPromptAt = Environment.TickCount64; guildPromptName = plan.Name; pendingNpc = 0; }
        if (packet is S.MapInformation or S.MapChanged or S.Death) guildNameRequested = false;
    }

    private async Task<bool> CreateGuild(long now)
    {
        if (Knowledge?.Goal is not { Kind:"guild-create" } goal || Knowledge.Guild is not { Action:"create" } plan ||
            Profile.Guilds.Mode is not ("create" or "auto") || state.InGuild || state.Trading || state.StorageActive) return false;
        if (goal.MapId != Knowledge.MapId || Distance(state.User!.Location, new(goal.X, goal.Y)) > 4) return false;
        if (guildNameRequested)
        {
            guildNameRequested = false;
            if (now-guildPromptAt>5000 || guildPromptName!=plan.Name)
            { await connection.Send(new C.GuildNameReturn { Name = "" }); return true; }
            pendingGuildName = plan.Name;
            await Status("创建行会", plan.Reason);
            await Asset(new C.GuildNameReturn { Name = plan.Name }, "guild-create", 0); return true;
        }
        if (page.Count > 0 && npcId == goal.NpcId)
        {
            var link = page.SelectMany(line => Regex.Matches(line, @"<[^<>]+/@([^<>]+)>").Select(m => m.Groups[1].Value))
                .FirstOrDefault(value => value.Equals("CREATEGUILD", StringComparison.OrdinalIgnoreCase));
            page = [];
            if (link != null) { await Call(goal.NpcId, "[@" + link + "]"); nextGuildAction = now + 3000; return true; }
            await report(new("goal-blocked", Profile.Id, Reason:"当前NPC没有实际可用的建会链接", Text:goal.Key));
            nextGuildAction = now + 60000; npcId = 0; return true;
        }
        if (now < nextGuildAction) return false;
        nextGuildAction = now + 3000;
        await Status("筹备建会", "读取建会NPC实际对话"); await Call(goal.NpcId, "[@MAIN]"); return true;
    }

    private async Task<bool> GuildActivity(long now)
    {
        if (Profile.Guilds.Mode == "off" || Knowledge?.Guild is not { } plan || state.Trading || state.StorageActive || pendingNpc != 0) return false;
        if (!state.InGuild && Profile.Guilds.Mode is "join" or "auto" && plan.Action == "join")
        {
            if (state.GuildInvitation.Length > 0)
            {
                var accept = state.GuildInvitation.Equals(plan.Name, StringComparison.OrdinalIgnoreCase);
                state.GuildInvitation = "";
                if (accept)
                {
                    pendingGuildName = plan.Name;
                    await Status("加入行会", "接受原服邀请：" + plan.Name);
                    await Asset(new C.GuildInvite { AcceptInvite = true }, "guild-join", 0);
                }
                else await connection.Send(new C.GuildInvite { AcceptInvite = false });
                return true;
            }
            if (!plan.InvitationsEnabled && now >= nextGuildAction)
            {
                nextGuildAction = now + 5000; await connection.Send(new C.Chat { Message = "@ALLOWGUILD" });
                await Status("申请入会", "启用原服行会邀请：" + plan.Name); return true;
            }
            if (plan.InvitationsEnabled && Profile.Chat && now >= nextGuildRequest && plan.Member.Length > 0)
            {
                nextGuildRequest = now + 600000;
                await connection.Send(new C.Chat { Message = "/" + plan.Member + " 想和大家一起发展，可以邀请我加入“" + plan.Name + "”吗？" });
                await report(new("guild", Profile.Id, Reason:"已向有招收权限的在线成员申请入会", Text:plan.Name));
                return true;
            }
        }
        if (state.InGuild && plan.Action == "invite" && state.HasGuildPermission(GuildRankOptions.CanRecruit) && now >= nextGuildInvite)
        {
            nextGuildInvite = now + 20000;
            await connection.Send(new C.EditGuildMember { ChangeType = 0, Name = plan.Member });
            await report(new("guild", Profile.Id, Reason:"按原权限邀请申请者入会", Text:plan.Member)); return true;
        }
        if (plan.Action != "siege" || !Profile.Guilds.Siege || Knowledge.AllowPk != true || plan.Goal == null || Knowledge.Goal?.Key != plan.Goal.Key || !state.InGuild) return false;
        if (state.CurrentAttackMode != AttackMode.Guild)
        {
            if (now >= nextGuildAction) { nextGuildAction = now + 3000; await connection.Send(new C.ChangeAMode { Mode = AttackMode.Guild }); }
            return true;
        }
        var target = (plan.Targets ?? []).Select(id => state.Actors.GetValueOrDefault(id)).FirstOrDefault(actor => actor is { Dead:false, Hidden:false });
        if (target != null && !Knowledge.NoFight && !Knowledge.InSafeZone)
        { await Status("行会作战", plan.Reason + "，目标：" + target.Name); return await Attack(target, now, state.CalculateStats()); }
        if (plan.Goal.MapId == Knowledge.MapId && state.User!.Location == new System.Drawing.Point(plan.Goal.X, plan.Goal.Y))
        {
            await Status("行会驻守", plan.Reason); nextAction = now + 1500;
            await connection.Send(new C.Turn { Direction = state.User.Direction }); return true;
        }
        return await FollowObjective(now);
    }
}
