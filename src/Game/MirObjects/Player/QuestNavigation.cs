using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;
using S = ServerPackets;

namespace Server.MirObjects;

public partial class PlayerObject
{
    public void ReadQuestNavigation(ClientPackets.QuestNavigationRequest request)
    {
        var result = new S.QuestNavigation { RequestId = request.RequestId, QuestIndex = request.QuestIndex, OriginMap = CurrentMap.Info.Index };
        var progress = CurrentQuests.FirstOrDefault(quest => quest.Index == request.QuestIndex);
        if (progress == null) { Enqueue(result); return; }
        var maps = Envir.MapList.Where(map => map.Info.Index == result.OriginMap || !map.Info.RequiredGroup ||
            (GroupMembers?.Count ?? 0) >= Math.Max(2, map.Info.RequiredGroupSize)).DistinctBy(map => map.Info.Index).ToDictionary(map => map.Info.Index);
        var routes = new Dictionary<int, (int Hops, Point? Exit)> { [result.OriginMap] = (0, null) };
        var queue = new Queue<int>(); queue.Enqueue(result.OriginMap);
        while (queue.TryDequeue(out var id) && routes.Count < 2048)
            foreach (var exit in maps[id].Info.Movements.Where(exit => !exit.NeedHole && !exit.NeedMove && exit.ConquestIndex == 0))
            {
                if (!maps.ContainsKey(exit.MapIndex) || routes.ContainsKey(exit.MapIndex)) continue;
                routes[exit.MapIndex] = (routes[id].Hops + 1, routes[id].Exit ?? exit.Source); queue.Enqueue(exit.MapIndex);
            }
        void Add(int id, string name, Map map, Point location, uint npc = 0)
        {
            if (result.Targets.Count >= 64) return;
            var reachable = routes.TryGetValue(map.Info.Index, out var route);
            Point? point = null;
            for (var radius = 0; reachable && radius <= 12 && point == null; radius++)
                for (var y = location.Y - radius; y <= location.Y + radius && point == null; y++)
                    for (var x = location.X - radius; x <= location.X + radius; x++)
                        if (map.ValidPoint(x, y) && map.GetCell(x, y).Objects?.Any(ob => ob.Blocking) != true) { point = new Point(x, y); break; }
            result.Targets.Add(new QuestNavigationTarget { Id = id, Name = name, MapIndex = map.Info.Index, MapTitle = map.Info.Title,
                Location = location, NextLocation = reachable ? route.Exit ?? point ?? location : location, NpcId = npc, Reachable = reachable && point != null });
        }
        void Hunt(int id, string name, Func<MonsterInfo, bool> matches)
        {
            var candidate = maps.Values.Where(map => routes.ContainsKey(map.Info.Index)).SelectMany(map => map.Respawns
                .Where(spawn => spawn.Info.Count > 0 && spawn.Monster != null && matches(spawn.Monster)).Select(spawn => (map, spawn)))
                .OrderBy(pair => routes[pair.map.Info.Index].Hops).ThenBy(pair => Functions.MaxDistance(CurrentLocation, pair.spawn.Info.Location)).FirstOrDefault();
            if (candidate.map != null) Add(id, name, candidate.map, candidate.spawn.Info.Location);
        }
        if (!progress.Completed)
        {
            for (var i = 0; i < progress.KillTaskCount.Count && i < 30; i++)
            {
                var task = progress.KillTaskCount[i]; if (task.Complete) continue;
                Hunt(i + 1, task.Info.Monster.Name, monster => monster.Name.StartsWith(task.Info.Monster.Name, StringComparison.OrdinalIgnoreCase));
            }
            for (var i = 0; i < progress.ItemTaskCount.Count && i < 30; i++)
            {
                var task = progress.ItemTaskCount[i]; if (task.Complete) continue;
                Hunt(i + 101, task.Info.Item.FriendlyName, monster => NavigationDrop(monster.Drops, task.ItemID));
            }
        }
        var finish = Envir.NPCs.FirstOrDefault(npc => npc.ObjectID == progress.Info.FinishNpcIndex && npc.Visible && npc.CurrentMap != null &&
            (!npc.VisibleLog.TryGetValue(Info.Index, out var visible) || visible));
        if (finish != null) Add(0, finish.Name, finish.CurrentMap, finish.CurrentLocation, finish.ObjectID);
        Enqueue(result);
    }

    private static bool NavigationDrop(IEnumerable<DropInfo> drops, int item, int depth = 0) => depth < 16 && drops.Any(drop =>
        drop.BaseProbability > 0 && (drop.Item?.Index == item || drop.GroupedDrop != null && NavigationDrop(drop.GroupedDrop, item, depth + 1)));
}
