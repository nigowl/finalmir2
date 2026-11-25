using System.Drawing;
using System.Runtime.CompilerServices;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class QuestNavigationChecks
{
    public static void Prepare(Mir2.Hosting.ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("任务坐标夹具仅允许隔离英雄版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new Server.ConsoleHost.SqliteDatabaseStorage(paths.Database); Server.DatabaseStorage.Current = storage;
        var world = Envir.Main;
        if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
        var role = world.CharacterList.Single(role => role.Name == "TradeRight");
        var map = new MapInfo { Index = ++world.MapIndex, FileName = "3", Title = "比奇城" }; world.MapInfoList.Add(map);
        foreach (var other in world.MapInfoList) other.Movements.Clear();
        var npc = new NPCInfo { Index = ++world.NPCIndex, MapIndex = map.Index, Location = new(297, 612), Name = "铁匠李铁匠", FileName = "NativeQuestCoordinates" };
        world.NPCInfoList.Add(npc);
        File.WriteAllText(Path.Combine(Server.Settings.NPCPath, npc.FileName + ".txt"), "[@MAIN]\n#SAY\n任务坐标检查。\n[Quests]\n999\n");
        var quest = new QuestInfo { Index = 999, Name = "铁匠的第一次考验", FileName = "NativeQuestCoordinates", GotoMessage = "返回[297:612]处寻找铁匠李铁匠。" };
        File.WriteAllText(Path.Combine(Server.Settings.QuestPath, quest.FileName + ".txt"), "[@DESCRIPTION]\n隔离任务坐标检查。\n");
        world.QuestInfoList.Add(quest); world.QuestIndex = Math.Max(world.QuestIndex, quest.Index);
        role.CurrentQuests = [new(quest.Index) { StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow }];
        world.SaveDB(); world.SaveAccounts(); Server.DatabaseStorage.Current = null;
        Console.WriteLine("PASS isolated disconnected quest map fixture; hero and player assets retained.");
    }

    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool, string> check)
    {
        var world = Envir.Main; var maps = world.MapList; var npcs = world.NPCs; var quests = world.QuestInfoList;
        try
        {
            Map Map(int index, string title)
            {
                var map = new Map(new MapInfo { Index = index, Title = title }) { Width = 8, Height = 8, Cells = new Cell[8, 8] };
                for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
                return map;
            }
            var local = Map(701, "管理图"); var remote = Map(702, "比奇城"); world.MapList = [local, remote];
            var npc = (NPCObject)RuntimeHelpers.GetUninitializedObject(typeof(NPCObject));
            npc.Info = new NPCInfo { Name = "铁匠李铁匠", Location = new(3, 4) }; npc.Visible = true; npc.VisibleLog = [];
            npc.CurrentMap = remote; world.NPCs = [npc];
            world.QuestInfoList = [new QuestInfo { Index = 1, FinishNpcIndex = npc.ObjectID }];
            var player = new Player { Info = new CharacterInfo { Index = 7 }, Account = new AccountInfo { Gold = 123 }, CurrentMap = local };
            player.Info.CurrentQuests = [new(1) { EndDateTime = DateTime.UtcNow }];
            S.QuestNavigation Read(int id = 1)
            {
                player.ReadQuestNavigation(new() { QuestIndex = id, RequestId = 42 });
                return player.Packets.OfType<S.QuestNavigation>().Last();
            }
            var result = Read(); var target = result.Targets.Single();
            check(result.RequestId == 42 && result.OriginMap == 701 && target.MapTitle == "比奇城" && target.Location == npc.CurrentLocation && !target.Reachable,
                "不可达的本人任务没有保留地图/坐标，或获得可执行导航");
            check(Read(2).Targets.Count == 0, "未接任务泄露导航资料");
            npc.Visible = false; check(Read().Targets.Count == 0, "全局隐藏NPC泄露导航资料"); npc.Visible = true;
            npc.VisibleLog[7] = false; check(Read().Targets.Count == 0, "本人不可见NPC泄露导航资料"); npc.VisibleLog.Clear();
            local.Info.Movements.Add(new MovementInfo { MapIndex = 702, Source = new(1, 2) });
            target = Read().Targets.Single();
            check(target.Reachable && target.NextLocation == new Point(1, 2), "公开出口没有恢复原导航首步");
            remote.Info.RequiredGroup = true;
            check(!Read().Targets.Single().Reachable, "地图资料放宽了原组队路线要求");
            check(player.Account.Gold == 123 && player.Info.CurrentQuests.Count == 1 && player.CompletedQuests.Count == 0 && player.Info.Inventory.All(item => item == null),
                "只读任务导航修改了资产或任务");
        }
        finally { world.MapList = maps; world.NPCs = npcs; world.QuestInfoList = quests; }
    }
}
