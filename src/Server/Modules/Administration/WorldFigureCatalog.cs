using Mir2.Management;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public static class WorldFigureCatalog
{
    public static SpriteAppearance Appearance(MonsterInfo monster)
    {
        var image = (int)monster.Image;
        var library = image >= 10000 ? $"Pet/{image - 10000:00}" : image is >= 950 and < 960 ? $"Gate/{image - 950:00}"
            : image is >= 940 and < 950 ? $"Siege/{image - 940:00}" : $"Monster/{image:000}";
        return new(monster.Name, library, 0);
    }
    public static CatalogFigurePage Read(string kind, int page, string? search)
    {
        if (page is < 1 or > 1000000) throw new InvalidDataException("Invalid page.");
        bool Match(int id, string name) => string.IsNullOrWhiteSpace(search) || name.Contains(search, StringComparison.OrdinalIgnoreCase) || id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
        if (kind == "monsters")
        {
            var entries = Envir.Main.MonsterInfoList.Where(m => Match(m.Index, m.Name)).OrderBy(m => m.Index).ToArray();
            return new(entries.Skip((page - 1) * 50).Take(50).Select(Monster).ToArray(), entries.Length);
        }
        if (kind == "npcs")
        {
            var entries = Envir.Main.NPCInfoList.Where(n => Match(n.Index, n.Name)).OrderBy(n => n.Index).ToArray();
            return new(entries.Skip((page - 1) * 50).Take(50).Select(Npc).ToArray(), entries.Length);
        }
        throw new InvalidDataException("Unknown figure catalog.");
    }
    private static CatalogFigure Monster(MonsterInfo m)
    {
        var appearance = Appearance(m);
        var attributes = new Dictionary<string, string>
        {
            ["编号"] = m.Index.ToString(), ["名称"] = m.Name, ["外观"] = $"{m.Image} ({(int)m.Image})", ["等级"] = m.Level.ToString(),
            ["经验奖励"] = m.Experience.ToString(), ["AI"] = m.AI.ToString(), ["特效"] = m.Effect.ToString(), ["光照"] = m.Light.ToString(),
            ["视野"] = m.ViewRange.ToString(), ["识破隐身"] = m.CoolEye.ToString(), ["攻击间隔（ms）"] = m.AttackSpeed.ToString(), ["移动间隔（ms）"] = m.MoveSpeed.ToString(),
            ["掉落脚本"] = string.IsNullOrWhiteSpace(m.DropPath) ? m.Name : m.DropPath, ["掉落规则数"] = m.Drops.Count.ToString(),
            ["可诱惑"] = Yes(m.CanTame), ["可推动"] = Yes(m.CanPush), ["可召回"] = Yes(m.CanRecall), ["自动显血"] = Yes(m.AutoRev),
            ["不死系"] = Yes(m.Undead), ["Boss"] = Yes(m.IsBoss), ["出生脚本"] = Yes(m.HasSpawnScript), ["死亡脚本"] = Yes(m.HasDieScript),
            ["HP"] = m.Stats[Stat.HP].ToString(), ["DC"] = $"{m.Stats[Stat.MinDC]}–{m.Stats[Stat.MaxDC]}",
            ["AC"] = $"{m.Stats[Stat.MinAC]}–{m.Stats[Stat.MaxAC]}", ["MAC"] = $"{m.Stats[Stat.MinMAC]}–{m.Stats[Stat.MaxMAC]}"
        };
        foreach (var stat in m.Stats.Values) attributes[stat.Key.ToString()] = stat.Value.ToString();
        return new(m.Index, m.Name, appearance.Library, appearance.Frame, attributes);
    }
    private static CatalogFigure Npc(NPCInfo n)
    {
        var map = Envir.Main.MapInfoList.FirstOrDefault(m => m.Index == n.MapIndex);
        var image = n.Image;
        return new(n.Index, n.Name, image >= 1000 ? $"Flag/{image - 1000:00}" : $"NPC/{image:00}", 0, new()
        {
            ["地图"] = $"{map?.FileName}（#{n.MapIndex}） · {map?.Title ?? "未配置地图"}", ["坐标"] = $"{n.Location.X}, {n.Location.Y}",
            ["脚本"] = n.FileName, ["外观"] = n.Image.ToString(), ["交易倍率"] = n.Rate.ToString()
        });
    }
    private static string Yes(bool value) => value ? "是" : "否";
}
