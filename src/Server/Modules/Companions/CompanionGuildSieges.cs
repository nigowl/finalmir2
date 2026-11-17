using System.Drawing;
using Mir2.Companions;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public static class CompanionGuildSieges
{
    public static KnownGuildPlan? Plan(PlayerObject player, ISet<string>? blocked)
    {
        var guild = player.MyGuild;
        if (guild == null) return null;
        var routes = CompanionPlanning.Routes(player.CurrentMap.Info.Index, Envir.Main.MapInfoList, player.GroupMembers?.Count ?? 0);
        foreach (var war in Envir.Main.Conquests.Where(c => c.WarIsOn && c.ConquestMap != null &&
            (c.GuildInfo.Owner == guild.Guildindex || guild.Conquest == null && (c.StartType != ConquestType.Request || c.GuildInfo.AttackerID == guild.Guildindex)))
            .OrderByDescending(c => c.GuildInfo.Owner == guild.Guildindex).ThenBy(c => c.Info.Index))
        {
            var map = war.GameType is ConquestGame.CapturePalace or ConquestGame.Classic ? war.PalaceMap : war.ConquestMap;
            if (map == null || map.Info.NoFight || !routes.ContainsKey(map.Info.Index)) continue;
            var key = $"guild/siege/{war.Info.Index}";
            if (blocked?.Contains(key) == true) continue;
            var reachable = HoldingPoint(player,war,map);
            if (reachable == null) continue;
            var goal = new KnownGoal("guild-siege", map.Info.Index, reachable.Value.X, reachable.Value.Y,
                (war.GuildInfo.Owner == guild.Guildindex ? "守卫" : "参与攻城：") + war.Info.Name, NextExit:routes[map.Info.Index], Key:key);
            var targets = Targets(player, war).Select(target => target.ObjectID).Take(64).ToArray();
            return new("siege", guild.Name, "", goal.Reason, player.EnableGuildInvite, goal, targets);
        }
        return null;
    }

    private static Point? HoldingPoint(PlayerObject player, ConquestObject war, Map map)
    {
        var palace = war.GameType is ConquestGame.CapturePalace or ConquestGame.Classic;
        var point = war.GameType switch
        {
            ConquestGame.CapturePalace or ConquestGame.Classic => new Point(map.Width / 2,map.Height / 2),
            ConquestGame.KingOfHill => war.Info.KingLocation,
            ConquestGame.ControlPoints => war.Info.ControlPoints.OrderBy(f => Functions.MaxDistance(f.Location,player.CurrentLocation)).FirstOrDefault()?.Location ?? war.Info.Location,
            _ => war.Info.Location
        };
        var radius = war.GameType == ConquestGame.KingOfHill ? (int)war.Info.KingSize : war.GameType == ConquestGame.ControlPoints ? 3 : 32;
        var exits = CompanionPlanning.WalkingExits(map.Info).Select(exit => exit.Source).ToHashSet();
        bool Available(Point p) => map.ValidPoint(p) && !exits.Contains(p) &&
            (map.GetConquest(p) ?? map.tempConquest) == war &&
            !map.GetCell(p).Objects.Any(other => other != player && !other.Dead && other.Blocking);
        // 已在计分范围内就原地驻守，其他成员选择空格，不能挤向同一中心或站到范围外。
        if (player.CurrentMap == map && (palace || Functions.InRange(player.CurrentLocation,point,radius)) && Available(player.CurrentLocation)) return player.CurrentLocation;
        return Enumerable.Range(0,Math.Min(32,radius) + 1).SelectMany(r => Enumerable.Range(-r,2 * r + 1)
            .SelectMany(offset => new[] { new Point(point.X + offset,point.Y - r), new Point(point.X + offset,point.Y + r),
                new Point(point.X - r,point.Y + offset), new Point(point.X + r,point.Y + offset) }))
            .Where(Available).Select(p => (Point?)p).FirstOrDefault();
    }

    private static IEnumerable<MapObject> Targets(PlayerObject player, ConquestObject war)
    {
        bool Visible(MapObject target) => !target.Dead && target.CurrentMap == player.CurrentMap &&
            Functions.InRange(target.CurrentLocation, player.CurrentLocation, Globals.DataRange) &&
            (target.CurrentMap.GetConquest(target.CurrentLocation) ?? target.CurrentMap.tempConquest) == war && target.IsAttackTarget(player);
        if (player.InSafeZone || player.CurrentMap.Info.NoFight || (player.CurrentMap.GetConquest(player.CurrentLocation) ?? player.CurrentMap.tempConquest) != war) return [];
        var opponents = player.CurrentMap.Players.Where(p => p != player && p.MyGuild != null && p.MyGuild != player.MyGuild &&
            !player.MyGuild.AllyGuilds.Contains(p.MyGuild) && (war.StartType != ConquestType.Request ||
                p.MyGuild.Guildindex == (war.GuildInfo.Owner == player.MyGuild.Guildindex ? war.GuildInfo.AttackerID : war.GuildInfo.Owner))).Cast<MapObject>();
        var gates = war.GuildInfo.Owner == player.MyGuild.Guildindex ? [] : war.GateList.Select(g => g.Gate).Where(g => g != null).Cast<MapObject>()
            .Concat(war.WallList.Select(w => w.Wall).Where(w => w != null));
        return opponents.Concat(gates).Where(Visible).OrderBy(target => Functions.MaxDistance(target.CurrentLocation, player.CurrentLocation));
    }
}
