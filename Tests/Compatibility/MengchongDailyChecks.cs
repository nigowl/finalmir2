using System.Drawing;
using Crystal.Native;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class MengchongDailyChecks
{
    public static void Prepare(ProjectPaths paths, int characterId, bool travel)
    {
        if (Path.GetFileName(Path.GetDirectoryName(paths.Data))?.StartsWith("finalmir2-daily.", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("仅允许在已安装日常的隔离版本准备夹具。");
        using var gate = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database);
        DatabaseStorage.Current = storage;
        var world = Envir.Main;
        if (!world.LoadDB()) throw new InvalidDataException("隔离任务世界读取失败。");
        world.LoadAccounts();
        var daily = world.QuestInfoList.Where(q => q.MengchongDaily).ToArray();
        var character = world.CharacterList.Single(c => c.Index == characterId);
        var npc = world.NPCInfoList.Single(n => n.Name == "盟重日常使者");
        var townInfo = world.MapInfoList.Single(map => map.Index == npc.MapIndex);
        Settings.MapPath = Path.Combine(paths.Assets, "maps");
        var town = new Map(townInfo);
        Require(town.Load(), "隔离地图无法加载");
        if (travel)
        {
            Require(character.Level is >= 20 and < 30 && character.CompletedQuests.Count == 0 && character.Inventory.Where(item => item?.Info is { Type: ItemType.Scroll, Shape: 1 }).Sum(item => item.Count) > 1,
                "只可用已通过首轮日常并模拟次日的隔离角色验证回城赶路");
            character.CurrentLocation = town.WalkableCells.First(p => town.GetSafeZone(p) == null && Functions.MaxDistance(p, npc.Location) >= 120 &&
                townInfo.Movements.All(exit => exit.Source != p));
            world.SaveAccounts();
            return;
        }
        var player = new PlayerObject { Info = character, Account = character.AccountInfo };
        var client = new GameState();
        var infoPacket = new S.UserInformation { ObjectID = 1, Class = MirClass.Warrior, Inventory = new UserItem[46], Equipment = new UserItem[14], QuestInventory = new UserItem[40] };
        foreach (var (level, tier) in new (ushort, int)[] { (1, 20), (29, 20), (30, 30), (39, 30), (40, 40), (51, 40) })
        {
            player.Level = level;
            infoPacket.Level = level;
            client.Apply(infoPacket);
            foreach (var quest in daily) client.Apply(new S.NewQuestInfo { Info = quest.CreateClientQuestInfo() });
            Require(daily.Where(q => q.CanAccept(player)).Select(q => q.Name).Order().SequenceEqual(daily.Where(q => q.Name.EndsWith($"（{tier}档）")).Select(q => q.Name).Order()), "服务器等级分档错误");
            Require(daily.Count(q => client.CanAcceptQuest(q.CreateClientQuestInfo())) == 3, "客户端等级分档错误");
        }
        var golds = daily.Where(q => q.GoldReward > 0).OrderBy(q => q.RequiredMinLevel).ToArray();
        player.Level = 30;
        character.CurrentQuests.Add(new QuestProgressInfo(golds[0].Index));
        Require(!golds[1].CanAccept(player), "升级后可重复接取同种日常");
        character.CurrentQuests.Clear();
        character.CompletedQuests.Add(golds[0].Index);
        Require(!golds[1].CanAccept(player), "同一天升级后可重复领奖");
        world.ProcessNewDay();
        Require(golds[1].CanAccept(player), "次日没有恢复接取资格");
        var scrollQuest = daily.First(q => q.FixedRewards.Count == 2);
        Require(scrollQuest.FixedRewards.All(r => r.Count == 100 && r.Item.StackSize > 0), "卷轴定义数量错误");
        var rewards = scrollQuest.FixedRewards.SelectMany(reward => Enumerable.Range(0, (reward.Count + reward.Item.StackSize - 1) / reward.Item.StackSize)
            .Select(i => new UserItem(reward.Item) { Count = (ushort)Math.Min(reward.Item.StackSize, reward.Count - i * reward.Item.StackSize) })).ToArray();
        var filler = world.ItemInfoList.First(i => i.Type == ItemType.Weapon);
        var firstFree = character.Inventory.Length - rewards.Length + 1;
        for (var i = 0; i < firstFree; i++) character.Inventory[i] = new UserItem(filler);
        Require(!scrollQuest.CanReceiveRewards(player, rewards), "满包错误放行卷轴");
        character.Inventory[firstFree - 1] = null;
        Require(scrollQuest.CanReceiveRewards(player, rewards), "恰好够格时不能领取卷轴");
        var stone = new UserItem(daily.First(q => q.FixedRewards.Count == 1).FixedRewards[0].Item);
        var torch = new UserItem(new ItemInfo { Type = ItemType.Torch, Stats = new Stats { [Stat.MaxAC] = 100 } });
        var equipment = new UserItem[14]; equipment[3] = torch;
        Require(Mir2.Companions.CompanionInventoryRules.UpgradeSlot(stone, equipment, MirClass.Warrior, false) == 3, "未将魔血石视为装备升级");
        equipment[3] = stone;
        Require(Mir2.Companions.CompanionInventoryRules.UpgradeSlot(torch, equipment, MirClass.Warrior, false) == -1, "普通火把替换了魔血石");

        // 只在隔离世界布置近场、低血量的原任务怪，印记及奖励由真实击杀和交付产生。
        character.Inventory = new UserItem[46]; character.Equipment = new UserItem[14];
        character.Gender = MirGender.Male; character.Level = 20; character.HP = 200; character.Experience = 0;
        character.AccountInfo.Gold = 0;
        var blade = world.ItemInfoList.First(i => i.Type == ItemType.Weapon && i.Shape == 1);
        blade.RequiredClass = RequiredClass.None; blade.RequiredAmount = 0; blade.RequiredType = RequiredType.Level;
        blade.Stats[Stat.MinDC] = blade.Stats[Stat.MaxDC] = 200; blade.Stats[Stat.Accuracy] = 100;
        character.Equipment[0] = world.CreateFreshItem(blade);
        var potion = world.ItemInfoList.First(i => i.Type == ItemType.Potion && i.Shape == 0 && i.Stats[Stat.HP] > 0 && i.StackSize >= 20);
        character.Inventory[0] = world.CreateFreshItem(potion); character.Inventory[0].Count = 20;
        character.BindMapIndex = character.CurrentMapIndex = npc.MapIndex;
        character.BindLocation = character.CurrentLocation = npc.Location + new Size(0, 1);
        var monster = world.MonsterInfoList.Single(m => m.Name == "飞刺虫");
        monster.Stats[Stat.HP] = 1; monster.Experience = 1; monster.ViewRange = 0;
        foreach (var map in world.MapInfoList) map.Respawns.Clear();
        var spots = town.WalkableCells.Where(p => town.GetSafeZone(p) == null && Functions.MaxDistance(p, npc.Location) >= 9 &&
            Functions.MaxDistance(p, npc.Location) <= 18 && townInfo.Movements.All(exit => exit.Source != p)).OrderBy(p => Functions.MaxDistance(p, npc.Location)).Take(20).ToArray();
        Require(spots.Length == 20, "隔离怪物落点不足");
        foreach (var spot in spots) townInfo.Respawns.Add(new RespawnInfo { RespawnIndex = ++world.RespawnIndex, MonsterIndex = monster.Index, Location = spot, Count = 1, Delay = 1 });
        world.SaveDB(); world.SaveAccounts();
        Console.WriteLine("盟重日常分档、跨档当日限制、次日恢复、奖励容量及魔血石检查通过；已准备原TCP任务夹具。");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
