using System.Drawing;
using Mir2.Hosting;
using Server.MirDatabase;

namespace Server.ConsoleHost;

public sealed partial class WorldDefinitions
{
    public int PrepareMengchongDailies()
    {
        const string npcFile = "MongchonProvince/MudWall/盟重日常委托";
        var installed = World.QuestInfoList.Where(q => q.MengchongDaily).ToArray();
        if (installed.Length == 9 && World.NPCInfoList.Count(npc => npc.FileName == npcFile) == 1) return 0;
        if (installed.Length != 0 || World.NPCInfoList.Any(npc => npc.FileName == npcFile))
            throw new InvalidOperationException("已有不完整的盟重日常定义，请先核对，未覆盖现有内容。");

        var town = World.MapList.Single(map => map.Info.FileName == "3");
        var board = World.NPCInfoList.Single(npc => npc.MapIndex == town.Info.Index && npc.FileName == "MongchonProvince/MudWall/Board");
        var guide = World.NPCInfoList.Single(npc => npc.MapIndex == town.Info.Index && npc.FileName == "MongchonProvince/MudWall/MirGuide");
        var routeMaps = CompanionPlanning.Routes(town.Info.Index, World.MapInfoList);
        var reachable = World.MapList.Where(map => routeMaps.ContainsKey(map.Info.Index) && !map.Info.NoFight && !map.Info.NoDropMonster)
            .SelectMany(map => map.Info.Respawns.Where(spawn => spawn.Count > 0)).Select(spawn => spawn.MonsterIndex).ToHashSet();
        var tiers = new[]
        {
            (Level:20, Min:0, Max:29, Gold:300_000, Stone:"小", Targets:new[] { "飞刺虫", "沙蛆" }),
            (Level:30, Min:30, Max:39, Gold:500_000, Stone:"中", Targets:new[] { "红野猪", "黑野猪" }),
            (Level:40, Min:40, Max:ushort.MaxValue, Gold:1_000_000, Stone:"大", Targets:new[] { "祖玛弓箭手", "剧毒蜘蛛" })
        };
        ItemInfo RequireItem(string name) => World.ItemInfoList.Single(item => item.Name == name && item.StackSize > 0);
        var returnScroll = RequireItem("回城卷轴");
        var randomScroll = RequireItem("随机传送卷");
        if (returnScroll.Type != ItemType.Scroll || returnScroll.Shape != 1 || randomScroll.Type != ItemType.Scroll || randomScroll.Shape != 2)
            throw new InvalidDataException("盟重日常奖励引用的回城、随机卷轴类型错误。");
        var sampleItem = World.ItemInfoList.First(item => item.Type == ItemType.Quest && item.Image > 0);
        var roles = new[] { "巡边赏金", "魔血补给", "传送石补给" };
        var items = new List<ItemInfo>();
        var quests = new List<QuestInfo>();
        var files = new Dictionary<string, (string? Before, string After)>();
        var itemIndex = Math.Max(World.ItemIndex, World.ItemInfoList.Max(item => item.Index));
        var questIndex = Math.Max(World.QuestIndex, World.QuestInfoList.Max(q => q.Index));
        var location = Enumerable.Range(-4, 9).SelectMany(x => Enumerable.Range(-4, 9).Select(y => new Point(board.Location.X + x, board.Location.Y + y)))
            .Where(p => town.ValidPoint(p) && town.GetSafeZone(p) != null && town.Info.Movements.All(exit => exit.Source != p)
                && World.NPCInfoList.All(npc => npc.MapIndex != town.Info.Index || npc.Location != p))
            .OrderBy(p => Functions.MaxDistance(p, board.Location)).First();
        var helper = new NPCInfo
        {
            Index = Next(World.NPCIndex, World.NPCInfoList.Select(n => n.Index)), Name = "盟重日常使者",
            MapIndex = town.Info.Index, Location = location, Image = guide.Image, Colour = guide.Colour,
            FileName = npcFile, ShowOnBigMap = true
        };

        foreach (var tier in tiers)
        {
            var monsters = tier.Targets.Select(name => World.MonsterInfoList.Single(m => m.Name == name)).ToArray();
            if (monsters.Any(m => !reachable.Contains(m.Index) || m.IsBoss))
                throw new InvalidDataException("盟重日常缺少可公开步行到达的普通掉落怪物。");
            var stone = RequireItem($"魔血石（{tier.Stone}）");
            if (!MagicBloodStone.IsStone(stone)) throw new InvalidDataException("盟重日常奖励魔血石类型错误。");
            for (var i = 0; i < roles.Length; i++)
            {
                var title = roles[i];
                var token = new ItemInfo { Index = checked(++itemIndex), Name = $"盟重{tier.Level}档{title}印记", Type = ItemType.Quest,
                    RequiredClass = RequiredClass.None, Image = sampleItem.Image, StackSize = 20, Weight = 1 };
                if (World.ItemInfoList.Any(item => item.Name == token.Name)) throw new InvalidDataException("盟重日常印记已存在，未覆盖原定义。");
                items.Add(token);
                var quest = new QuestInfo
                {
                    Index = checked(++questIndex), Name = $"{title}（{tier.Level}档）", Group = $"盟重日常·{title}", Type = QuestType.Daily,
                    RequiredMinLevel = tier.Min, RequiredMaxLevel = tier.Max, RequiredClass = RequiredClass.None,
                    FileName = $"{npcFile}/{tier.Level}-{title}"
                };
                quests.Add(quest);
                var reward = i switch
                {
                    0 => $"[@GoldReward]\n{tier.Gold}\n",
                    1 => $"[@FixedRewards]\n{stone.Name} 1\n",
                    _ => $"[@FixedRewards]\n{returnScroll.Name} 100\n{randomScroll.Name} 100\n"
                };
                var targets = string.Join("、", tier.Targets);
                AddFile("Quests", quest.FileName, $"[@Description]\n这是盟重的每日委托，同种委托每天只能完成一次。\n[@TaskDescription]\n击杀{targets}，收集3枚{token.Name}，交给盟重日常使者。\n[@ReturnDescription]\n带上印记，回盟重日常使者处领取奖励。\n[@ItemTasks]\n{token.Name} 3\n{reward}");
                foreach (var monster in monsters)
                {
                    var path = PathFor("Drops", string.IsNullOrEmpty(monster.DropPath) ? monster.Name : monster.DropPath);
                    var file = files.TryGetValue(path, out var prior) ? prior : (File.ReadAllText(path), File.ReadAllText(path));
                    files[path] = (file.Item1, file.Item2.TrimEnd('\r', '\n') + $"\n1/2 {token.Name} Q\n");
                }
            }
        }
        AddFile("NPCs", npcFile, "[@MAIN]\n#SAY\n盟重每日委托：巡边赏金、魔血补给、传送石补给。每种每日只能完成一次，按你的等级安排难度。\n[Quests]\n" +
            string.Join('\n', quests.SelectMany(q => new[] { q.Index, -q.Index })) + "\n");
        helper.CollectQuestIndexes.AddRange(quests.Select(q => q.Index));
        helper.FinishQuestIndexes.AddRange(quests.Select(q => q.Index));
        var previous = (World.ItemIndex, World.QuestIndex, World.NPCIndex);
        Commit("mengchong-dailies", () =>
        {
            foreach (var (path, file) in files) AssetCatalog.AtomicText(path, file.After);
            World.ItemInfoList.AddRange(items); World.ItemIndex = itemIndex;
            World.QuestInfoList.AddRange(quests); World.QuestIndex = questIndex;
            World.NPCInfoList.Add(helper); World.NPCIndex = helper.Index;
        }, () =>
        {
            foreach (var (path, file) in files)
                if (file.Before == null) File.Delete(path); else AssetCatalog.AtomicText(path, file.Before);
            foreach (var item in items) World.ItemInfoList.Remove(item);
            foreach (var quest in quests) World.QuestInfoList.Remove(quest);
            World.NPCInfoList.Remove(helper);
            (World.ItemIndex, World.QuestIndex, World.NPCIndex) = previous;
        });
        return quests.Count;

        string PathFor(string folder, string script) => ProjectPaths.SafePath(paths.World,
            $"Envir/{folder}/{script.Replace('\\', '/')}.txt");
        void AddFile(string folder, string script, string content)
        {
            var path = PathFor(folder, script);
            if (File.Exists(path)) throw new InvalidDataException($"盟重日常脚本已存在：{script}");
            files.Add(path, (null, content));
        }
    }
}
