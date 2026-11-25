using Mir2.Hosting;
using Client.MirObjects;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class HeroFixture
{
    public static void PrepareMovement(ProjectPaths paths)
    {
        Prepare(paths, true);
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        try
        {
            var world = Envir.Main; var role = world.CharacterList.Single(character => character.Name == "TradeRight");
            var map = world.MapInfoList.Single(); map.Movements.Clear(); map.SafeZones.Clear();
            map.SafeZones.Add(new SafeZoneInfo { Location = role.CurrentLocation, Size = 6, StartPoint = true });
            role.MaximumHeroCount = 5; role.Heroes = new HeroInfo[5]; role.PMode = PetMode.MoveOnly;
            for (var i = 0; i < 4; i++)
            {
                var hero = new HeroInfo(new ClientPackets.NewHero { Name = "格位英雄" + (i + 1), Class = MirClass.Warrior })
                    { Index = ++world.NextHeroID, Level = 30, HP = 100, MP = 100, CurrentMapIndex = map.Index, CurrentLocation = role.CurrentLocation };
                world.HeroList.Add(hero); role.Heroes[i] = hero;
            }
            role.CurrentHeroIndex = role.Heroes[0].Index; role.HeroSpawned = true;
            var npc = world.NPCInfoList.First(); npc.Name = "英雄使者"; npc.Location = new(role.CurrentLocation.X + 3, role.CurrentLocation.Y + 3);
            npc.FileName = "GridHeroGuide";
            world.QuestInfoList.First().RequiredMaxLevel = 1000;
            File.WriteAllText(Path.Combine(paths.World, "Envir/NPCs/GridHeroGuide.txt"),
                "[@MAIN]\n#SAY\n英雄管理与格位回归。\n<创建英雄/@CREATEHERO> <管理英雄/@MANAGEHERO>\n<关闭/@EXIT>\n\n[Quests]\n1\n");
            world.SaveDB(); world.SaveAccounts();
            Console.WriteLine("PASS isolated grid/five-hero fixture: original map, four heroes, original NPC and safe-zone border.");
        }
        finally { DatabaseStorage.Current = null; }
    }

    public static void PrepareShared(ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("英雄夹具仅允许独立临时版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        var world = Envir.Main;
        if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
        var role = world.CharacterList.Single(c => c.Name == "TradeLeft");
        var hero = role.Heroes.Single(h => h != null);
        role.Level = hero.Level = 50; role.HP = role.MP = 10; hero.HP = hero.MP = 1;
        role.CurrentLocation = role.BindLocation = world.CharacterList.Single(c => c.Name == "TradeRight").CurrentLocation;
        role.CurrentMapIndex = role.BindMapIndex = 1; role.HeroSpawned = true;
        role.Inventory = new UserItem[Globals.BaseInventorySize]; role.Equipment = new UserItem[14]; role.Magics.Clear();
        hero.Inventory = new UserItem[10]; hero.Equipment = new UserItem[14]; hero.Magics.Clear();
        UserItem Fresh(ItemInfo info, ushort count = 1) { var item = world.CreateFreshItem(info); item.Count = count; return item; }
        var weapon = world.ItemInfoList.First(item => item.StartItem && item.Type == ItemType.Weapon);
        var book = world.ItemInfoList.First(item => item.Type == ItemType.Book && item.Shape == (short)Spell.FireBall);
        var potion = world.ItemInfoList.First(item => item.Type == ItemType.Potion && item.Shape == 1 && item.Stats[Stat.HP] > 0 && item.RequiredAmount <= 50);
        potion.StartItem = true; potion.RequiredClass = (RequiredClass)31; potion.RequiredGender = RequiredGender.Male | RequiredGender.Female;
        role.Inventory[6] = Fresh(weapon); role.Inventory[7] = Fresh(book, 2); role.Inventory[0] = Fresh(potion, 5);
        hero.Inventory[9] = Fresh(world.ItemInfoList.First(item => item.Type == ItemType.Potion && item.Shape == 0 && item.Stats[Stat.MP] > 0), 7);
        var expansion = world.ItemInfoList.FirstOrDefault(item => item.Type == ItemType.Scroll && item.Shape == 15);
        if (expansion == null)
        {
            expansion = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离旧英雄扩包卷", Type = ItemType.Scroll, Shape = 15,
                RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female };
            world.ItemInfoList.Add(expansion);
        }
        role.Inventory[8] = Fresh(expansion);
        world.SaveDB(); world.SaveAccounts(); DatabaseStorage.Current = null;
        Console.WriteLine("PASS isolated shared hero fixture: ordinary weapon/book/potion, legacy bag item and expansion rejection.");
    }

    public static void Prepare(ProjectPaths paths, bool archer = false)
    {
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("英雄夹具仅允许独立临时版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        var world = Envir.Main;
        if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
        var map = world.MapInfoList.Single(map => map.Index == 1); map.FileName = "3"; map.Title = "盟重土城";
        world.MapInfoList.RemoveAll(other => other != map); world.NPCInfoList.RemoveAll(npc => npc.MapIndex != map.Index);
        map.NoReconnect = false; map.Respawns.Clear();
        if (!map.SafeZones.Any(zone => zone.Location == new System.Drawing.Point(330, 330))) map.SafeZones.Add(new SafeZoneInfo { Location = new(330, 330), Size = 20 });
        if (!world.NPCInfoList.Any(npc => npc.FileName == "Heroes/MirGuide"))
            world.NPCInfoList.Add(new NPCInfo { Index = ++world.NPCIndex, MapIndex = map.Index, Location = new(345, 345), Name = "测试引路人", FileName = "Heroes/MirGuide" });
        var role = world.CharacterList.Single(c => c.Name == "TradeRight");
        if (role.Heroes.Any(hero => hero != null) && !archer) throw new InvalidOperationException("英雄夹具已使用，拒绝覆盖。");
        if (archer) { role.Heroes = new HeroInfo[role.MaximumHeroCount]; role.CurrentHeroIndex = 0; role.HeroSpawned = false; role.CurrentQuests.Clear(); }
        role.Level = 30; role.HP = 100; role.MP = 100; role.CurrentMapIndex = role.BindMapIndex = map.Index;
        var tiles = new MapReader(Path.Combine(paths.Assets, "maps/3.map"));
        role.CurrentLocation = role.BindLocation = Enumerable.Range(327, 7).SelectMany(x => Enumerable.Range(327, 7).Select(y => new System.Drawing.Point(x, y)))
            .Where(p => (tiles.MapCells[p.X, p.Y].BackImage & 0x20000000) == 0 && (tiles.MapCells[p.X, p.Y].FrontImage & 0x8000) == 0)
            .OrderBy(p => Math.Abs(p.X - 330) + Math.Abs(p.Y - 330)).First();
        role.Inventory = new UserItem[80];
        world.SaveDB(); world.SaveAccounts(); DatabaseStorage.Current = null;
        var config = Path.Combine(paths.World, "Configs/HeroSettings.ini");
        var text = File.ReadAllText(config).Replace("CanCreateArcher=" + (!archer), "CanCreateArcher=" + archer); File.WriteAllText(config, text);
        Console.WriteLine($"PASS isolated hero fixture: level30, independent map/NPC, original sealed hero, archer enabled={archer}.");
    }
}
