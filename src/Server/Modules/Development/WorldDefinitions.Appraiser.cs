using System.Drawing;
using Mir2.Hosting;
using Server.MirDatabase;

namespace Server.ConsoleHost;

public sealed partial class WorldDefinitions
{
    public int PrepareEquipmentAppraiser()
    {
        const string script = "Appraisal/MengchongAppraiser";
        const string content = "[@MAIN]\n#SAY\n老夫玄鉴，识兵甲、辨良材。\n未鉴定装备可正常穿戴，鉴定后方显真正品质。\n<鉴定装备/@APPRAISE>\n[@APPRAISE]\n";
        var path = ProjectPaths.SafePath(paths.World, $"Envir/NPCs/{script}.txt");
        var town = World.MapList.Single(map => map.Info.FileName == "3");
        var existing = World.NPCInfoList.Where(npc => npc.FileName == script).ToArray();
        if (existing.Length == 1 && existing[0].Name == "玄鉴先生" && existing[0].MapIndex == town.Info.Index &&
            File.Exists(path) && File.ReadAllText(path) == content) return 0;
        if (existing.Length != 0 || File.Exists(path)) throw new InvalidDataException("鉴定大师或脚本已存在且不匹配，未覆盖。");
        var model = World.NPCInfoList.First(npc => npc.MapIndex == town.Info.Index && npc.FileName.EndsWith("/MirGuide", StringComparison.Ordinal));
        var location = Nearby(new Point(333, 333), 8).FirstOrDefault(point => town.ValidPoint(point) &&
            town.GetSafeZone(point) != null && town.Info.Movements.All(exit => exit.Source != point) && FreeNpcTile(town, point));
        if (location.IsEmpty) throw new InvalidDataException("盟重没有可用的安全鉴定位置。");
        var npc = new NPCInfo { Index = Next(World.NPCIndex, World.NPCInfoList.Select(n => n.Index)), Name = "玄鉴先生",
            FileName = script, MapIndex = town.Info.Index, Location = location, Image = model.Image, Colour = Color.Wheat, ShowOnBigMap = true };
        var before = World.NPCIndex;
        Commit("equipment-appraiser", () => { AssetCatalog.AtomicText(path, content); World.NPCInfoList.Add(npc); World.NPCIndex = npc.Index; },
            () => { File.Delete(path); World.NPCInfoList.Remove(npc); World.NPCIndex = before; });
        return 1;
    }
}
