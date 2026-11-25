using System.Drawing;
using Mir2.Hosting;
using Server.MirDatabase;

namespace Server.ConsoleHost;

public sealed partial class WorldDefinitions
{
    public int PrepareHeroGuide()
    {
        const string script = "Heroes/MengchongHeroGuide";
        const string content = "[@MAIN]\n#SAY\n我可以为你创建和管理英雄。\n选择职业、性别和名称，创建后使用背包中的封印英雄激活。\n英雄开放等级、职业和数量以当前设置为准。\n<创建英雄/@CREATEHERO>  <管理英雄/@MANAGEHERO>\n";
        var path = ProjectPaths.SafePath(paths.World, $"Envir/NPCs/{script}.txt");
        var town = World.MapList.Single(map => map.Info.FileName == "3");
        var existing = World.NPCInfoList.Where(npc => npc.FileName == script || npc.Name == "英雄使者").ToArray();
        if (existing.Length == 1 && existing[0].FileName == script && existing[0].Name == "英雄使者" &&
            existing[0].MapIndex == town.Info.Index && town.ValidPoint(existing[0].Location) &&
            town.GetSafeZone(existing[0].Location) != null && File.Exists(path) && File.ReadAllText(path) == content) return 0;
        if (existing.Length > 0 || File.Exists(path)) throw new InvalidDataException("英雄使者或脚本已存在且不匹配，未覆盖现有内容。");
        var model = World.NPCInfoList.First(npc => npc.MapIndex == town.Info.Index && npc.FileName.EndsWith("/MirGuide", StringComparison.Ordinal));
        var location = Nearby(new Point(330, 330), 8).FirstOrDefault(p => town.ValidPoint(p) && town.GetSafeZone(p) != null &&
            town.Info.Movements.All(exit => exit.Source != p) && FreeNpcTile(town, p));
        if (location.IsEmpty) throw new InvalidDataException("盟重没有可用的安全英雄使者位置。");
        var guide = new NPCInfo { Index = Next(World.NPCIndex, World.NPCInfoList.Select(npc => npc.Index)), Name = "英雄使者",
            MapIndex = town.Info.Index, Location = location, Image = model.Image, Colour = Color.Gold, FileName = script, ShowOnBigMap = true };
        var previous = World.NPCIndex;
        Commit("hero-guide", () =>
        {
            AssetCatalog.AtomicText(path, content); World.NPCInfoList.Add(guide); World.NPCIndex = guide.Index;
        }, () => { File.Delete(path); World.NPCInfoList.Remove(guide); World.NPCIndex = previous; });
        return 1;
    }
}
