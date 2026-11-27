using Server.MirEnvir;
using S = ServerPackets;

namespace Server.MirObjects;

public static class ConquestRegistration
{
    public const string Page = "[@SIEGE]", Prefix = "[@SIEGEREGISTER_";
    public static string Receipt(int id, bool success) => $"攻城报名{(success ? "成功" : "拒绝")}：{id}";

    public static bool Available(PlayerObject player, ConquestObject war) => player.MyGuild != null && player.MyGuildRank?.Index == 0 &&
        !player.Dead && player.TradePartner == null && player.ItemRentalPartner == null && !player.Fishing &&
        player.MyGuild.Conquest == null && war.Info.Type == ConquestType.Request && !war.WarIsOn &&
        war.GuildInfo.AttackerID == -1 && war.GuildInfo.Owner != player.MyGuild.Guildindex &&
        (war.Guild == null || !player.MyGuild.AllyGuilds.Contains(war.Guild)) &&
        (war.Info.Monday || war.Info.Tuesday || war.Info.Wednesday || war.Info.Thursday || war.Info.Friday || war.Info.Saturday || war.Info.Sunday);

    public static bool Register(PlayerObject player, ConquestObject war)
    {
        if (!Available(player,war)) return false;
        war.GuildInfo.AttackerID = player.MyGuild.Guildindex;
        war.GuildInfo.NeedSave = true;
        return true;
    }

    public static bool TryCall(PlayerObject player, uint npc, string key)
    {
        if (!key.StartsWith(Prefix,StringComparison.Ordinal)) return false;
        if (!key.EndsWith(']') || !int.TryParse(key[Prefix.Length..^1],out var id)) return true;
        var war = Envir.Main.Conquests.FirstOrDefault(c => c.Info.Index == id);
        if (player.NPCObjectID != npc || player.NPCPage?.Key != Page)
        { player.Enqueue(new S.NPCResponse { Page=[Receipt(id,false),"请重新与攻城使者交谈。"] }); return true; }
        var success = war != null && Register(player,war);
        Show(player,Receipt(id,success));
        return true;
    }

    public static void Show(PlayerObject player, string notice = "")
    {
        var lines = new List<string> { notice,"请会长报名，以下为原引擎UTC战期；报名不另收费用。" };
        foreach (var war in Envir.Main.Conquests.Where(war => Available(player,war)))
            lines.Add($"<{war.Info.Name} · {war.Info.StartHour:00}:00开战/@SIEGEREGISTER_{war.Info.Index}>");
        if (lines.Count == 2) lines.Add("当前没有可报名的城池，或您不是符合条件的会长。");
        lines.Add("<返回/@MAIN>");
        var page = new NPCPage(Page);
        var buttons = lines.SelectMany(line => System.Text.RegularExpressions.Regex.Matches(line,@"<[^<>]+/(@[^<>/]+)>")
            .Select(match => "[" + match.Groups[1].Value.ToUpperInvariant() + "]")).ToList();
        var segment = new NPCSegment(page,lines,buttons,[],[],[]);
        page.SegmentList.Add(segment); player.NPCPage = page;
        player.NPCSuccess.Clear(); player.NPCSuccess.Add(segment,true); player.NPCSpeech = lines;
        player.Enqueue(new S.NPCResponse { Page = lines });
    }
}
