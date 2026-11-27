using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class EquipmentAppraisalFixture
{
    public static void Prepare(ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-heroes.appraisal", StringComparison.Ordinal)) throw new InvalidOperationException("鉴定夹具仅允许独立测试版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        Directory.SetCurrentDirectory(paths.World);
        try
        {
            var world = Envir.Main;
            if (!world.LoadDB()) throw new InvalidDataException("鉴定夹具读取失败。");
            world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
            var role = world.CharacterList.Single(character => character.Name == "TradeRight");
            var map = world.MapInfoList.Single(map => map.Index == role.CurrentMapIndex);
            var basis = world.ItemInfoList.First(item => item.Type == ItemType.Weapon && !item.IsFishingRod);
            var weapon = new ItemInfo { Index = ++world.ItemIndex, Name = "鉴定试炼剑", Type = ItemType.Weapon, Image = basis.Image, Shape = basis.Shape,
                RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female, Price = 20000, Durability = 10000, Weight = 1, NeedIdentify = true,
                Stats = new Stats { [Stat.MinDC] = 3, [Stat.MaxDC] = 5 } };
            world.ItemInfoList.Add(weapon);
            role.AccountInfo.Gold = 1000000; role.Inventory = new UserItem[80]; role.Equipment = new UserItem[14]; role.HP = role.MP = 10000;
            for (var i = 1; i <= 5; i++)
            {
                var item = world.CreateDropItem(weapon, appraisable: true); item.EquipmentQuality = (EquipmentGrade)i;
                role.Inventory[i + 5] = item;
            }
            var legacy = world.CreateFreshItem(weapon); legacy.AddedStats[Stat.MaxDC] = 7; role.Inventory[11] = legacy;
            role.HeroSpawned = true; role.PMode = PetMode.MoveOnly;
            foreach (var hero in role.Heroes.Where(hero => hero != null)) hero.HeroBehaviour = HeroBehaviour.Follow;
            map.Respawns.Clear();
            var monster = new MonsterInfo { Index = ++world.MonsterIndex, Name = "隔离鉴定兽", Image = Monster.Hen, AI = 0, Level = 1, ViewRange = 0,
                Stats = new Stats { [Stat.HP] = 1 }, MoveSpeed = ushort.MaxValue, AttackSpeed = ushort.MaxValue };
            world.MonsterInfoList.Add(monster);
            map.Respawns.Add(new RespawnInfo { RespawnIndex = ++world.RespawnIndex, MonsterIndex = monster.Index, Location = new(role.CurrentLocation.X + 1, role.CurrentLocation.Y), Count = 1, Delay = 600 });
            Directory.CreateDirectory(Path.Combine(paths.World, "Envir/Drops"));
            File.WriteAllText(Path.Combine(paths.World, "Envir/Drops/隔离鉴定兽.txt"), "1/1 鉴定试炼剑\n");
            world.SaveDB(); world.SaveAccounts();
            Console.WriteLine("PASS isolated appraisal fixture: five hidden grades, legacy added stats and original monster drop, no formal player data.");
        }
        finally { DatabaseStorage.Current = null; }
    }
}
