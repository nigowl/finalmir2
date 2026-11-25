using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionGuildScenario
{
    public static void Run(ProjectPaths paths, int founderId, int joinerId, string mode)
    {
        if (!paths.Data.Contains("mir2-companions-guild-", StringComparison.Ordinal)) throw new InvalidOperationException("只允许本行会隔离测试版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("测试世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts(); world.LoadGuilds();
        var founder = world.CharacterList.Single(c => c.Index == founderId);
        var joiner = world.CharacterList.Single(c => c.Index == joinerId);
        if (mode == "ready")
        {
            founder.AccountInfo.Gold = 5000; world.SaveAccounts();
            Console.WriteLine("仅隔离fixture调整建会起始金币至5000，费用1000/材料1份仍由原NPC扣取。"); return;
        }
        if (mode is "proof" or "classic-proof")
        {
            if (founder.GuildIndex < 0 || founder.GuildIndex != joiner.GuildIndex || world.GuildList.Count != 1) throw new InvalidDataException("行会或成员未保存。");
            using var reader = new BinaryReader(DatabaseStorage.OpenRead(Path.Combine(Settings.ConquestsPath, "1.mcd")));
            var conquest = new ConquestGuildInfo(reader);
            if (conquest.Owner != founder.GuildIndex) throw new InvalidDataException("原攻城归属没有真实保存。");
            if (mode == "classic-proof" && joiner.AMode != AttackMode.Peace) throw new InvalidDataException("关闭攻城后没有恢复原和平模式。");
            Console.WriteLine(JsonSerializer.Serialize(new { guild = founder.GuildIndex, members = world.GuildList[0].Membercount, owner = conquest.Owner, gold = founder.AccountInfo.Gold })); return;
        }
        if (mode == "classic")
        {
            if (world.GuildList.Count != 1 || founder.GuildIndex != joiner.GuildIndex) throw new InvalidDataException("经典攻城fixture没有完整载入原行会，停止保存。");
            var castle = world.ConquestInfoList.Single();
            castle.Game = ConquestGame.Classic; castle.Type = ConquestType.Auto; castle.StartHour = 0; castle.WarLength = 1440;
            castle.Monday = castle.Tuesday = castle.Wednesday = castle.Thursday = castle.Friday = castle.Saturday = castle.Sunday = true;
            // 经典模式的山丘坐标故意留在外图；必须以原王宫人数和归属证明成功。
            castle.KingLocation = founder.BindLocation;
            foreach (var character in new[] { founder,joiner })
            { character.CurrentMapIndex = founder.BindMapIndex; character.CurrentLocation = Functions.PointMove(founder.BindLocation,MirDirection.Left,1); }
            world.SaveDB(); world.SaveAccounts();
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer,System.Text.Encoding.UTF8,true)) new ConquestGuildInfo { Owner = 0, AttackerID = -1 }.Save(writer);
            storage.Write(Path.Combine(Settings.ConquestsPath,castle.Index + ".mcd"),buffer.ToArray());
            Console.WriteLine("隔离经典攻城：原全天自动战期、王宫外登录、山丘坐标在外图，重置本测试城池归属后验证真实进入王宫。"); return;
        }
        foreach (var map in world.MapInfoList) { map.Respawns.Clear(); map.NoFight = false; map.Movements.Clear(); }
        world.NPCInfoList.Clear(); world.QuestInfoList.Clear();
        var local = world.MapInfoList.Single(m => m.Index == founder.BindMapIndex);
        var palace = world.MapInfoList.First(m => m.Index != local.Index);
        var start = founder.BindLocation;
        local.Movements.Add(new MovementInfo { Source = Functions.PointMove(start, MirDirection.Right, 4), MapIndex = palace.Index, Destination = start });
        palace.Movements.Add(new MovementInfo { Source = Functions.PointMove(start, MirDirection.Up, 4), MapIndex = local.Index, Destination = start });
        var token = new ItemInfo { Index = ++world.ItemIndex, Name = "NativeGuildToken", Type = ItemType.CraftingMaterial, StackSize = 10 };
        world.ItemInfoList.Add(token);
        foreach (var character in world.CharacterList.Where(c => c.Index == founderId || c.Index == joinerId))
        {
            character.CurrentMapIndex = local.Index; character.CurrentLocation = start; character.Level = 22;
            character.HP = 250; character.MP = 90; character.Experience = 0; character.AccountInfo.Gold = character == founder ? 1500u : 1000u;
            character.Inventory = new UserItem[46]; character.Equipment = new UserItem[14]; character.CurrentQuests.Clear(); character.CompletedQuests.Clear();
            // 建会专项先备好原药卷，避免公共补给合法扣费干扰建会费用断言。
            var medicine=world.ItemInfoList.First(item=>item.Type==ItemType.Potion&&item.Shape is 0 or 1&&item.Stats[Stat.HP]>0&&item.StackSize>=15);
            character.Inventory[1]=world.CreateFreshItem(medicine);character.Inventory[1].Count=15;
            character.Inventory[2]=world.CreateFreshItem(world.ItemInfoList.First(item=>item.Type==ItemType.Scroll&&item.Shape==1));
            var blade = new ItemInfo { Index = ++world.ItemIndex, Name = "NativeGuildBlade" + character.Index, Type = ItemType.Weapon, Durability = 5000,
                Stats = new Stats { [Stat.MinDC] = 10, [Stat.MaxDC] = 10 } };
            world.ItemInfoList.Add(blade); character.Equipment[0] = world.CreateFreshItem(blade);
        }
        founder.Inventory[0] = world.CreateFreshItem(token);
        var npc = new NPCInfo { Index = ++world.NPCIndex, Name = "Native Guild Clerk", FileName = "NativeGuildClerk", MapIndex = local.Index,
            Location = Functions.PointMove(start, MirDirection.Left, 3) };
        world.NPCInfoList.Add(npc);
        File.WriteAllText(Path.Combine(Settings.NPCPath, npc.FileName + ".txt"), "[@MAIN]\n#SAY\n原规则创建行会。\n<创建行会/@CREATEGUILD>\n[@CREATEGUILD]\n#ACT\nSTARTCONQUEST 1\n");
        Directory.CreateDirectory(Settings.ConfigPath);
        File.WriteAllText(Path.Combine(Settings.ConfigPath, "GuildSettings.ini"), "[Guilds]\nMinimumLevel=22\n[Required-0]\nAmount=1000\n[Required-1]\nItemName=NativeGuildToken\nAmount=1\n[Exp]\nLevel-0=10000\n[Cap]\nLevel-0=4\n");
        world.ConquestInfoList.Clear(); world.ConquestIndex = 1;
        world.ConquestInfoList.Add(new ConquestInfo { Index = 1, Name = "Native Guild Castle", MapIndex = local.Index, PalaceIndex = palace.Index,
            FullMap = true, Type = ConquestType.Forced, Game = ConquestGame.CapturePalace, Location = start, Size = 1000, WarLength = 30 });
        world.SaveDB(); world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime, "companion-guild-fixture.json"), JsonSerializer.Serialize(new { token = token.Index,
            uniqueId = founder.Inventory[0].UniqueID, origin = local.Index, palace = palace.Index, npc = npc.Index }));
        Console.WriteLine("隔离建会/入会/攻城fixture：22级、1000金币+原编号材料，原NPC开始临时攻城，王宫公开步行可达。");
    }
}
