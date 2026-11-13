using System.Drawing;
using Server.MirEnvir;
using Server.MirDatabase;

internal static class CompanionGuidanceFixture
{
    public static void Configure(Envir world, Point start, Func<Point, bool> walkable, ItemInfo potion)
    {
        var visited = new HashSet<Point> { start }; var queue = new Queue<Point>(); queue.Enqueue(start);
        Point? remote = null;
        while (queue.TryDequeue(out var point) && visited.Count < 20000)
        {
            if (Functions.MaxDistance(point, start) >= 36) { remote = point; break; }
            for (var direction = 0; direction < 8; direction++)
            {
                var next = Functions.PointMove(point, (MirDirection)direction, 1);
                if (walkable(next) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        if (remote == null) throw new InvalidDataException("隔离地图没有足够远的可达NPC位置。");
        var first = world.MapInfoList[0]; var second = world.MapInfoList[1];
        first.Respawns.Clear(); second.Respawns.Clear();
        foreach (var map in world.MapInfoList) foreach (var exit in map.Movements) exit.ShowOnBigMap = false;
        world.MonsterInfoList.Single(monster=>monster.Index==1).AI=9;
        world.MonsterInfoList.Single(monster=>monster.Index==1).Level=12;
        world.MonsterInfoList.Single(monster=>monster.Index==1).DropPath="Guidance\\NativeTarget";
        second.Respawns.Add(new RespawnInfo { MonsterIndex = 1, RespawnIndex = 1, Location = new(start.X + 3, start.Y + 3), Spread = 3, Count = 4, Delay = 1 });
        world.NPCInfoList[0].Location = remote.Value;
        var near=Enumerable.Range(0,8).Select(direction=>Functions.PointMove(start,(MirDirection)direction,2)).First(walkable);
        world.NPCInfoList.Add(new NPCInfo{Index=2,MapIndex=first.Index,Name="Near Return NPC",FileName="GuidanceReturn",Location=near});
        world.NPCIndex=2;
        var token = new ItemInfo { Index = ++world.ItemIndex, Name = "GuidanceToken", Type = ItemType.Quest, Image = potion.Image, StackSize = 20 };
        world.ItemInfoList.Add(token);
        var meat = new ItemInfo { Index = ++world.ItemIndex, Name = "GuidanceMeat", Type = ItemType.Meat, Price = 100, Durability = 10000, StackSize = 1 };
        world.ItemInfoList.Add(meat);
        world.QuestInfoList.RemoveAll(quest => quest.Index != 1);
        world.QuestInfoList[0].Name = "远行收集任务";
        world.QuestInfoList.Add(new QuestInfo{Index=2,Name="回访交付任务",FileName="GuidanceCarry",RequiredMinLevel=1,RequiredMaxLevel=255,RequiredQuest=1,RequiredClass=RequiredClass.None});
        world.QuestInfoList.Add(new QuestInfo{Index=3,Name="纯拜访任务",FileName="GuidanceVisit",RequiredMinLevel=1,RequiredMaxLevel=255,RequiredQuest=2,RequiredClass=RequiredClass.None});
        world.QuestIndex=3;
        File.WriteAllText(Path.Combine("Envir", "Quests", "NativeQuest1.txt"),
            "[@DESCRIPTION]\n寻找另一张地图的怪物并收集道具。\n[@TASKDESCRIPTION]\n收集两份道具。\n[@RETURNDESCRIPTION]\n返回交付。\n[@COMPLETION]\n任务完成。\n[@ITEMTASKS]\nGuidanceToken 2\n[@GOLDREWARD]\n77\n[@EXPREWARD]\n5\n");
        Directory.CreateDirectory(Path.Combine("Envir", "Drops", "Guidance"));
        File.WriteAllText(Path.Combine("Envir", "Drops", "Guidance", "NativeTarget.txt"), $"1/1 Gold 1000\n1/1 {potion.Name}\n1/1 GuidanceToken Q\n1/1 GuidanceMeat\n");
        File.WriteAllText(Path.Combine("Envir", "NPCs", "NativeShop.txt"), $"[@MAIN]\n#SAY\n欢迎接取远行任务。\n<出售/@SELL>\n[@SELL]\n#SAY\n出售普通肉类。\n<返回/@MAIN>\n[Types]\n{(int)ItemType.Meat}\n[Quests]\n1\n-2\n3\n");
        File.WriteAllText(Path.Combine("Envir", "NPCs", "GuidanceReturn.txt"), "[@MAIN]\n#SAY\n请返回远处交付。\n[Quests]\n-1\n2\n");
        File.WriteAllText(Path.Combine("Envir", "Quests", "GuidanceCarry.txt"),"[@DESCRIPTION]\n把携带物送回。\n[@TASKDESCRIPTION]\n返回交付。\n[@CARRYITEMS]\nGuidanceToken 1\n[@EXPREWARD]\n5\n");
        File.WriteAllText(Path.Combine("Envir", "Quests", "GuidanceVisit.txt"),"[@DESCRIPTION]\n完成对话拜访。\n[@EXPREWARD]\n5\n");
        Console.WriteLine($"引导场景：起点 {start}，任务NPC {remote}，目标怪物和任务道具只在地图 {second.Index}。");
    }
}
