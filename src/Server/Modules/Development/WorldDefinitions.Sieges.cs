using System.Drawing;
using Mir2.Hosting;
using Server.MirDatabase;

namespace Server.ConsoleHost;

public sealed partial class WorldDefinitions
{
    public int PrepareSiegeClerk()
    {
        const string script = "Guilds/SiegeClerk";
        const string content = "[@MAIN]\n#SAY\n老夫司戎，掌管攻城报名。请会长率本会成员按城池战期出征。\n<攻城报名/@SIEGE>\n[@SIEGE]\n";
        var path = ProjectPaths.SafePath(paths.World,$"Envir/NPCs/{script}.txt");
        var existing = World.NPCInfoList.Where(n => n.FileName == script).ToArray();
        if (existing.Length == 1 && existing[0].Name == "司戎长老" && File.Exists(path) && File.ReadAllText(path) == content) return 0;
        if (existing.Length != 0 || File.Exists(path)) throw new InvalidDataException("攻城使者或脚本已存在且不匹配，未覆盖。");
        if (!World.ConquestInfoList.Any(c => c.Type == ConquestType.Request)) throw new InvalidDataException("当前版本没有申请制城池，未改变原战期或城制。");
        var town = World.MapList.Single(m => m.Info.FileName == "3");
        var model = World.NPCInfoList.First(n => n.MapIndex == town.Info.Index && n.FileName.EndsWith("/MirGuide",StringComparison.Ordinal));
        var point = Nearby(new Point(329,333),8).FirstOrDefault(p => town.ValidPoint(p) && town.GetSafeZone(p) != null &&
            town.Info.Movements.All(e => e.Source != p) && FreeNpcTile(town,p));
        if (point.IsEmpty) throw new InvalidDataException("盟重没有可用的安全报名位置。");
        var npc = new NPCInfo { Index=Next(World.NPCIndex,World.NPCInfoList.Select(n => n.Index)),Name="司戎长老",FileName=script,
            MapIndex=town.Info.Index,Location=point,Image=model.Image,Colour=Color.Wheat,ShowOnBigMap=true };
        var before = World.NPCIndex;
        Commit("siege-clerk",() => { AssetCatalog.AtomicText(path,content); World.NPCInfoList.Add(npc); World.NPCIndex=npc.Index; },
            () => { File.Delete(path); World.NPCInfoList.Remove(npc); World.NPCIndex=before; });
        return 1;
    }
}
