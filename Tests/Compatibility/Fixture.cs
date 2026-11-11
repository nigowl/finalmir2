using System.Drawing;
using Server;
using Client.MirObjects;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class Fixture
{
    // Keep deterministic gameplay changes in disposable data, never in the reference world.
    public static void Create(string destination, bool tradeAccounts = false, bool guildAccount = false, bool catalog = false)
    {
        var reference = new Mir2.Hosting.ProjectPaths().Data;
        var root = Path.GetFullPath(destination);
        if (Directory.Exists(root)) throw new IOException("Fixture destination must not exist.");
        var database = Path.Combine(root, "import", "Crystal Database");
        Directory.CreateDirectory(database);
        Directory.CreateDirectory(Path.Combine(database, "Envir", "NPCs"));
        Directory.CreateDirectory(Path.Combine(database, "Envir", "Drops"));
        Directory.CreateDirectory(Path.Combine(database, "Configs"));
        foreach (var source in Directory.EnumerateFiles(Path.Combine(reference, "import", "Crystal Database", "Configs")))
            File.Copy(source, Path.Combine(database, "Configs", Path.GetFileName(source)));
        Directory.CreateDirectory(Path.Combine(root, "assets", "maps"));
        Directory.CreateSymbolicLink(Path.Combine(root, "assets", "libraries"), Path.Combine(reference, "assets", "libraries"));
        Directory.CreateSymbolicLink(Path.Combine(root, "assets", "sounds"), Path.Combine(reference, "assets", "sounds"));
        File.Copy(Path.Combine(reference, "assets", "maps", "0.map"), Path.Combine(root, "assets", "maps", "0.map"));
        File.Copy(Path.Combine(reference, "assets", "manifest.json"), Path.Combine(root, "assets", "manifest.json"));
        File.WriteAllText(Path.Combine(root, "assets", "import-report.json"), "[]");
        File.Copy(Path.Combine(reference, "import", "Crystal Database", "Server.MirDB"), Path.Combine(database, "Server.MirDB"));
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(database);
            var world = Envir.Main;
            if (!world.LoadDB()) throw new InvalidDataException("Cannot load reference world.");
            var map = new MapReader(Path.Combine(reference, "assets", "maps", "0.map"));
            bool Clear(int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height &&
                (map.MapCells[x, y].BackImage & 0x20000000) == 0 && (map.MapCells[x, y].FrontImage & 0x8000) == 0;
            var start = Enumerable.Range(270, 40).SelectMany(x => Enumerable.Range(600, 40).Select(y => new Point(x, y)))
                .OrderBy(p => Math.Abs(p.X - 288) + Math.Abs(p.Y - 616))
                .First(p => Enumerable.Range(-4, 9).All(dx => Enumerable.Range(-4, 9).All(dy => Clear(p.X + dx, p.Y + dy))));
            var first = new MapInfo { Index = 1, FileName = "0", Title = "Native Test Field", Light = LightSetting.Day, NoDropPlayer = true };
            first.SafeZones.Add(new SafeZoneInfo { Info = first, Location = start, Size = 0, StartPoint = true });
            if (guildAccount) first.SafeZones.Add(new SafeZoneInfo { Info = first, Location = new Point(start.X, start.Y - 1), Size = 0 });
            var second = new MapInfo { Index = 2, FileName = "0", Title = "Native Test Return", Light = LightSetting.Day, NoDropPlayer = true };
            first.Movements.Add(new MovementInfo { MapIndex = 2, Source = new Point(start.X + 4, start.Y), Destination = start });
            second.Movements.Add(new MovementInfo { MapIndex = 1, Source = new Point(start.X + 1, start.Y), Destination = start });
            world.MapInfoList.Clear(); world.MapInfoList.AddRange([first, second]); world.MapIndex = 2;
            foreach (var item in world.ItemInfoList) item.StartItem = false;
            var sword = world.ItemInfoList.First(i => i.Type == ItemType.Weapon && i.Shape == 1);
            sword.StartItem = true; sword.RequiredClass = RequiredClass.Warrior; sword.RequiredGender = RequiredGender.Male;
            sword.RequiredAmount = 0; sword.Weight = 0; sword.Stats[Stat.MinDC] = 100; sword.Stats[Stat.MaxDC] = 100; sword.Stats[Stat.Accuracy] = 100;
            sword.Stats[Stat.MinMC] = 100; sword.Stats[Stat.MaxMC] = 100;
            var potion = world.ItemInfoList.First(i => i.Type == ItemType.Potion && i.Shape == 0 && i.Stats[Stat.HP] > 0);
            potion.StartItem = true; potion.RequiredClass = RequiredClass.Warrior; potion.RequiredGender = RequiredGender.Male;
            potion.Price = 2; potion.Weight = 0; potion.StackSize = 20; potion.RequiredAmount = 0;
            var book = world.ItemInfoList.First(i => i.Type == ItemType.Book && i.Shape == (short)Spell.FireBall);
            book.StartItem = true; book.RequiredClass = RequiredClass.Warrior; book.RequiredGender = RequiredGender.Male; book.RequiredAmount = 0; book.Weight = 0;
            world.MagicInfoList.First(m => m.Spell == Spell.FireBall).Level1 = 1;
            var monster = new MonsterInfo { Index = 1, Name = "NativeTarget", Image = Monster.Hen, AI = 0, Level = 1,
                Experience = 60, ViewRange = 0, MoveSpeed = ushort.MaxValue, AttackSpeed = 2500 };
            monster.Stats[Stat.HP] = 20;
            world.MonsterInfoList.Clear(); world.MonsterInfoList.Add(monster); world.MonsterIndex = 1;
            // Walking also advances AttackTime; a 65-second step makes the death check random.
            var danger = new MonsterInfo { Index = 2, Name = "NativeDanger", Image = Monster.Oma, AI = 0, Level = 1,
                ViewRange = 0, MoveSpeed = 1000, AttackSpeed = 700 };
            danger.Stats[Stat.HP] = 9999; danger.Stats[Stat.MinDC] = 9999; danger.Stats[Stat.MaxDC] = 9999; danger.Stats[Stat.Accuracy] = 100;
            world.MonsterInfoList.Add(danger); world.MonsterIndex = 2;
            first.Respawns.Add(new RespawnInfo { MonsterIndex = 1, RespawnIndex = 1, Location = new Point(start.X + 1, start.Y + 1), Count = 1, Delay = 600 });
            first.Respawns.Add(new RespawnInfo { MonsterIndex = 1, RespawnIndex = 2, Location = new Point(start.X + 3, start.Y + 2), Count = 1, Delay = 600 });
            second.Respawns.Add(new RespawnInfo { MonsterIndex = 2, RespawnIndex = 3, Location = new Point(start.X + 2, start.Y + 2), Count = 1, Delay = 600 });
            world.NPCInfoList.Clear();
            world.NPCInfoList.Add(new NPCInfo { Index = 1, MapIndex = 1, FileName = "NativeShop", Name = "Native Shop", Image = 0,
                Location = new Point(start.X - 2, start.Y), Colour = Color.White });
            world.NPCIndex = 1; world.RespawnIndex = 3;
            world.QuestInfoList.Clear(); world.ConquestInfoList.Clear(); world.GTMapList.Clear(); world.GameShopList.Clear();
            world.DragonInfo.Enabled = false;
            Directory.CreateDirectory(Path.Combine("Envir", "Quests"));
            world.ItemIndex = Math.Max(world.ItemIndex, world.ItemInfoList.Max(i => i.Index));
            var ring = world.ItemInfoList.First(i => i.Type == ItemType.Ring);
            for (var index = 0; index < 2; index++)
                world.ItemInfoList.Add(new ItemInfo { Index = ++world.ItemIndex, Name = $"NativeChoice{index}", Type = ItemType.Ring,
                    Image = ring.Image, StackSize = 1, RequiredClass = RequiredClass.None, RequiredAmount = 0, Weight = 0 });
            for (var index = 1; index <= 3; index++)
            {
                world.ItemInfoList.Add(new ItemInfo { Index = ++world.ItemIndex, Name = $"NativeToken{index}", Type = ItemType.Quest,
                    Image = potion.Image, StackSize = 20, Weight = 0 });
                world.QuestInfoList.Add(new QuestInfo { Index = index, Name = $"Native Quest {index}", Group = "Native Quests", FileName = $"NativeQuest{index}",
                    RequiredMinLevel = 1, RequiredMaxLevel = ushort.MaxValue, RequiredClass = RequiredClass.None, Type = QuestType.General });
                File.WriteAllText(Path.Combine("Envir", "Quests", $"NativeQuest{index}.txt"),
                    $"[@DESCRIPTION]\nDefeat NativeTarget and return to Native Shop.\n[@TASKDESCRIPTION]\nContinue hunting in the test field.\n[@RETURNDESCRIPTION]\nChoose a reward and report to Native Shop.\n[@COMPLETION]\nHunt completed.\n[@CARRYITEMS]\nNativeToken{index} 1\n[@KILLTASKS]\nNativeTarget {(index == 1 ? 2 : 3)}\n" +
                    (index == 1 ? $"[@FIXEDREWARDS]\n{potion.Name} 2\n[@SELECTREWARDS]\nNativeChoice0 1\nNativeChoice1 1\n[@GOLDREWARD]\n77\n[@EXPREWARD]\n5\n" : ""));
            }
            world.QuestIndex = 3;
            File.WriteAllText(Path.Combine("Envir", "Drops", "NativeTarget.txt"), $"1/1 Gold 1000\n1/1 {potion.Name}\n");
            File.WriteAllText(Path.Combine("Envir", "NPCs", "NativeShop.txt"),
                $"[@MAIN]\n#SAY\nNative integration shop.\n<Buy/@BUY> <Sell/@SELL> <Storage/@STORAGE>\n\n[@BUY]\n#SAY\nBuy supplies.\n<Back/@MAIN>\n\n[@SELL]\n#SAY\nSell supplies.\n<Back/@MAIN>\n\n[@STORAGE]\n#SAY\nAccount storage.\n<Back/@MAIN>\n\n[Quests]\n1\n2\n3\n\n[Types]\n{(int)ItemType.Potion}\n\n[Trade]\n{potion.Name} 1\n");
            File.WriteAllText(Path.Combine("Configs", "WorldMap.ini"), "[Setup]\nEnabled=False\n\n[Layout]\n");
            if (catalog)
            {
                for (var i = 0; i < 53; i++)
                {
                    var entry = new MonsterInfo { Index = ++world.MonsterIndex, Name = $"Catalog Monster {i:00}", Image = Monster.Hen, Level = (ushort)(i + 1) };
                    entry.Stats[Stat.HP] = 100 + i; world.MonsterInfoList.Add(entry);
                    world.NPCInfoList.Add(new NPCInfo { Index = ++world.NPCIndex, MapIndex = 2, FileName = "CatalogList", Name = $"Catalog NPC {i:00}", Image = 0,
                        Location = new Point(start.X - 2, start.Y), Colour = Color.White });
                }
                File.WriteAllText(Path.Combine("Envir", "NPCs", "CatalogList.txt"), "[@MAIN]\n#SAY\nCatalog fixture.\n");
            }
            world.SaveDB();
            if (tradeAccounts)
            {
                world.AccountList.Clear(); world.CharacterList.Clear();
                foreach (var name in new[] { "TradeLeft", "TradeRight" })
                {
                    var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = name, Password = "Trade123", Gold = 1000, CreationIP = "127.0.0.1", CreationDate = DateTime.UtcNow };
                    var character = new CharacterInfo { Index = ++world.NextCharacterID, Name = name, AccountInfo = account, Class = MirClass.Warrior, Gender = MirGender.Male, CreationIP = "127.0.0.1", CreationDate = DateTime.UtcNow, Heroes = new HeroInfo[1] };
                    account.Characters.Add(character); world.AccountList.Add(account); world.CharacterList.Add(character);
                }
                world.SaveAccounts();
            }
            if (guildAccount)
            {
                var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = "NativeGuild", Password = "Native123", CreationIP = "127.0.0.1", CreationDate = DateTime.UtcNow };
                var leader = new CharacterInfo { Index = ++world.NextCharacterID, Name = "NativeGuildLead", AccountInfo = account, Class = MirClass.Warrior, Gender = MirGender.Male, CreationIP = "127.0.0.1", CreationDate = DateTime.UtcNow, Heroes = new HeroInfo[1] };
                account.Characters.Add(leader); world.AccountList.Add(account); world.CharacterList.Add(leader);
                var guild = new GuildInfo(new Server.MirObjects.PlayerObject { Info = leader }, "NativeGuild") { GuildIndex = ++world.NextGuildID, MemberCap = 20, Notice = new() { "Native guild fixture." } };
                guild.Ranks[0].Members[0].Player = null; guild.Ranks[0].Members[0].Online = false;
                guild.Ranks.Add(new GuildRank { Index = 1, Name = "Officers", Options = GuildRankOptions.CanChangeNotice });
                guild.Ranks.Add(new GuildRank { Index = 2, Name = "Members" });
                world.GuildList.Add(guild); leader.GuildIndex = guild.GuildIndex;
                Directory.CreateDirectory(Settings.GuildPath);
                using (var writer = new BinaryWriter(File.Create(Path.Combine(Settings.GuildPath, "0.mgd")))) guild.Save(writer);
                var settings = Path.Combine("Configs", "GuildSettings.ini"); var ini = new InIReader(settings);
                ini.Write("Guilds", "MinimumLevel", 1); ini.Save();
                world.SaveAccounts();
            }
            Console.WriteLine($"Created isolated native fixture at {root}; start {start.X},{start.Y}.");
        }
        finally { Directory.SetCurrentDirectory(previous); }
    }
}
