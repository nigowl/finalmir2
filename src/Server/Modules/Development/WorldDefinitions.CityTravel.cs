using System.Drawing;
using System.Text;
using Mir2.Hosting;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.ConsoleHost;

public sealed partial class WorldDefinitions
{
    public (int Changed, object Details) PrepareCityTravel()
    {
        const string folder = "CityTravel/";
        var towns = new (int MapId, Point Center, string Name, string Guide, int ModelNpc)[]
        {
            (1, new(288, 616), "边境村", "边境驿卒", 1),
            (1, new(328, 264), "比奇城", "比奇驿官", 41),
            (64, new(315, 82), "沃玛营地", "林地向导", 71),
            (77, new(229, 205), "奇隆城", "奇隆驿商", 88),
            (103, new(131, 177), "石桥山谷", "石桥引路人", 123),
            (108, new(176, 326), "道馆", "道馆行脚道人", 111),
            (144, new(503, 483), "毒蛇山谷", "蛇谷探路人", 133),
            (172, new(330, 330), "盟重土城", "土城军驿使", 150),
            (289, new(140, 337), "般若岛", "般若渡船泊主", 200),
            (366, new(295, 254), "雪原山谷", "雪岭引路人", 241)
        };
        var fields = new (int MapId, int EntryFrom, int Region, string Name, uint Gold)[]
        {
            (34, 1, 1, "天然洞穴", 2000), (39, 1, 1, "半兽人古墓", 2000), (47, 1, 1, "比奇废弃矿洞", 5000),
            (65, 64, 64, "昆虫洞穴一层", 5000), (70, 69, 64, "沃玛寺庙一层", 10000),
            (104, 103, 103, "巨魔矿洞一层", 15000),
            (116, 108, 108, "道馆废矿一层", 5000), (120, 108, 108, "林间小路", 10000), (121, 120, 108, "赤月峡谷一层", 30000),
            (149, 144, 144, "山谷废弃矿洞", 5000),
            (182, 172, 172, "盟重地牢", 10000), (200, 236, 172, "石墓一层", 15000), (254, 253, 172, "祖玛寺庙一层", 25000),
            (296, 289, 289, "般若石洞穴", 20000), (304, 303, 289, "般若寺庙一层", 35000),
            (377, 366, 366, "雪洞窟一层", 50000)
        };
        string ScriptPath(string name) => ProjectPaths.SafePath(paths.World, $"Envir/NPCs/{folder}{name}.txt");
        var installed = World.NPCInfoList.Where(npc => npc.FileName.StartsWith(folder, StringComparison.Ordinal)).ToArray();
        var existing = installed.Length == towns.Length && towns.All(t => installed.Count(npc => npc.MapIndex == t.MapId &&
            npc.FileName == folder + t.Name && npc.Name == t.Guide && File.Exists(ScriptPath(t.Name))) == 1);
        if (!existing && (installed.Length > 0 || towns.Any(t => File.Exists(ScriptPath(t.Name)) || World.NPCInfoList.Any(n => n.Name == t.Guide))))
            throw new InvalidOperationException("已存在同名或不完整的传送使者，未覆盖现有定义。");

        var npcIndex = World.NPCIndex;
        var cityStops = towns.Select(town =>
        {
            var map = RequireMap(town.MapId);
            var zone = map.Info.SafeZones.Single(s => s.Location == town.Center);
            var model = World.NPCInfoList.Single(n => n.Index == town.ModelNpc && n.MapIndex == town.MapId);
            var landing = Nearby(town.Center, Math.Min((int)zone.Size, 4))
                .FirstOrDefault(p => CityTravel.Landing(map, p, true) && FreeNpcTile(map, p));
            var location = existing ? installed.Single(n => n.FileName == folder + town.Name).Location : Nearby(landing, 3)
                .FirstOrDefault(p => p != landing && CityTravel.Landing(map, p, true) && FreeNpcTile(map, p) &&
                    Functions.InRange(town.Center, p, zone.Size));
            if (landing.IsEmpty || location.IsEmpty) throw new InvalidDataException($"找不到{town.Name}的安全落点或使者位置。");
            var guide = existing ? installed.Single(n => n.FileName == folder + town.Name) : new NPCInfo
            {
                Index = checked(++npcIndex), MapIndex = town.MapId, FileName = folder + town.Name, Name = town.Guide,
                Location = location, Image = model.Image, Colour = Color.Gold, ShowOnBigMap = true, BigMapIcon = model.BigMapIcon
            };
            if (!CityTravel.Landing(map, guide.Location, true)) throw new InvalidDataException($"请先核对{town.Name}使者的安全位置。");
            return (town.Name, town.MapId, Guide: guide, Landing: landing);
        }).ToArray();
        var fieldStops = fields.Select(field =>
        {
            var map = RequireMap(field.MapId);
            var entrance = RequireMap(field.EntryFrom);
            var exits = map.Info.Movements.Where(m => m.MapIndex == field.EntryFrom && Public(m)).ToArray();
            if (map.Info.NoFight || !map.Info.Respawns.Any(spawn => spawn.Count > 0) || exits.Length == 0)
                throw new InvalidDataException($"{field.Name}没有已确认的公开打怪入口。");
            var landing = entrance.Info.Movements.Where(m => m.MapIndex == field.MapId && Public(m))
                .SelectMany(m => Nearby(m.Destination, 2))
                .FirstOrDefault(p => CityTravel.Landing(map, p, false) && FreeNpcTile(map, p) &&
                    exits.Any(exit => Functions.InRange(exit.Source, p, 5)));
            if (landing.IsEmpty) throw new InvalidDataException($"找不到{field.Name}的公开入口附近落点。");
            return (field.Name, field.MapId, field.Region, field.Gold, Landing: landing);
        }).ToArray();

        var scripts = cityStops.Select(stop =>
        {
            var local = fieldStops.Select((field, index) => (field, index))
                .Where(f => f.field.Region == stop.MapId || stop.MapId == 77 && f.field.Region == 64).ToArray();
            if (local.Length == 0) throw new InvalidDataException($"{stop.Name}没有本地危险区域。");
            var script = new StringBuilder($"[@MAIN]\n#SAY\n这里是{stop.Name}，请选择目的地。\n城镇安全区（免费）\n");
            script.AppendLine(string.Join("  ", cityStops.Select((city, index) => (city, index))
                .Where(c => c.city.Name != stop.Name).Select(c => $"<{c.city.Name}/@CITY{c.index}>")));
            script.AppendLine("本地危险区域（金币）");
            script.AppendLine(string.Join("  ", local.Select(f => $"<{f.field.Name}·{f.field.Gold:N0}/@FIELD{f.index}>")));
            script.AppendLine("没有回城卷时，成功传送赠送1张。");
            for (var i = 0; i < cityStops.Length; i++)
                if (cityStops[i].Name != stop.Name)
                    AddTravel(script, $"CITY{i}", cityStops[i].Guide.MapIndex, cityStops[i].Landing, "SAFE", 0);
            foreach (var (field, i) in local)
            {
                script.AppendLine($"[@FIELD{i}]\n#SAY\n确定传送到{field.Name}的危险区域？\n费用：{field.Gold:N0}金币。");
                script.AppendLine("附近可能有怪物；背包没有回城卷时赠送1张。");
                script.AppendLine($"<确认前往/@GO{i}>  <返回目的地列表/@MAIN>");
                AddTravel(script, $"GO{i}", field.MapId, field.Landing, "FIELD", field.Gold);
            }
            var path = ScriptPath(stop.Name);
            return (Path: path, Before: File.Exists(path) ? File.ReadAllText(path) : null, Content: script.ToString());
        }).Where(s => s.Before != s.Content).ToArray();
        if (scripts.Length == 0) return (0, new { npcs = 0, scripts = 0 });
        var previous = World.NPCIndex;
        Commit("city-travel", () =>
        {
            foreach (var script in scripts) AssetCatalog.AtomicText(script.Path, script.Content);
            if (!existing)
            {
                World.NPCInfoList.AddRange(cityStops.Select(stop => stop.Guide));
                World.NPCIndex = npcIndex;
            }
        }, () =>
        {
            foreach (var script in scripts)
                if (script.Before == null) File.Delete(script.Path); else AssetCatalog.AtomicText(script.Path, script.Before);
            if (!existing) foreach (var stop in cityStops) World.NPCInfoList.Remove(stop.Guide);
            World.NPCIndex = previous;
        });
        return (scripts.Length, new
        {
            npcs = existing ? 0 : cityStops.Length, scripts = scripts.Length,
            cities = cityStops.Select(s => new { s.Name, s.Guide.MapIndex, npc = s.Guide.Location, s.Guide.Image, s.Landing }).ToArray(),
            fields = fieldStops.Select(s => new { s.Name, s.MapId, s.Region, s.Gold, s.Landing }).ToArray()
        });

        Server.MirEnvir.Map RequireMap(int id) => World.GetMap(id) is { } map && CityTravel.Allows(map)
            ? map : throw new InvalidDataException($"地图 {id} 未加载或禁止公共传送。");
    }

    private static bool Public(MovementInfo move) => !move.NeedHole && !move.NeedMove && move.ConquestIndex <= 0;
    private static bool FreeNpcTile(Server.MirEnvir.Map map, Point point) =>
        !World.NPCInfoList.Any(n => n.MapIndex == map.Info.Index && n.Location == point) &&
        !map.GetCell(point).Objects.Any(obj => obj.Race is ObjectType.Merchant || obj.Blocking && obj.Race is not (ObjectType.Player or ObjectType.Monster));
    private static IEnumerable<Point> Nearby(Point center, int radius) =>
        from x in Enumerable.Range(center.X - radius, radius * 2 + 1)
        from y in Enumerable.Range(center.Y - radius, radius * 2 + 1)
        let point = new Point(x, y)
        orderby Functions.MaxDistance(point, center), Math.Abs(x - center.X) + Math.Abs(y - center.Y)
        select point;
    private static void AddTravel(StringBuilder script, string key, int map, Point point, string mode, uint gold) =>
        script.AppendLine($"[@{key}]\n#ACT\nCITYTRAVEL {map} {point.X} {point.Y} {mode} {gold}");
}
