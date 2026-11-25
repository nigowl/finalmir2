using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class HeroRecoveryFixture
{
    public static void CheckLogin(ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("GM脚本检查仅允许隔离版本。");
        Directory.SetCurrentDirectory(paths.World);
        var script = Server.MirObjects.NPCScript.GetOrAdd(1999999, "00Default", Server.MirObjects.NPCScriptType.AutoPlayer);
        var login = script.NPCPages.Single(p => p.Key == "[@_LOGIN]");
        var prompt = script.NPCPages.Single(p => p.Key.Equals("[@AdminMapPrompt]", StringComparison.OrdinalIgnoreCase));
        if (!login.SegmentList.Any(s => s.ActList.Any(a => a.Type == Server.MirObjects.ActionType.DelayGoto) && s.CheckList.Any(c => c.Type == Server.MirObjects.CheckType.IsAdmin)) ||
            login.SegmentList.Any(s => s.ActList.Any(a => a.Type == Server.MirObjects.ActionType.Move)) ||
            !prompt.SegmentList.Any(s => s.Say.Any(line => line.Contains("是否进入管理员地图")) && s.CheckList.Any(c => c.Type == Server.MirObjects.CheckType.IsAdmin)))
            throw new InvalidDataException("登录脚本没有解析出仅GM可见的确认页。");
        Console.WriteLine("PASS original login script: deferred GM prompt after nested housekeeping pages, no login MOVE.");
    }

    public static void Audit(ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("英雄恢复检查仅允许隔离版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        try
        {
            var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
            world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            foreach (var account in world.AccountList.OrderBy(a => a.Index)) account.Save(writer);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Accounts = world.AccountList.Count, Characters = world.CharacterList.Count,
                Heroes = world.CharacterList.Sum(c => c.Heroes.Count(h => h != null)), Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray())) }));
        }
        finally { DatabaseStorage.Current = null; }
    }

    public static void Prepare(ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("英雄恢复检查仅允许隔离版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        try
        {
            var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
            world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
            var role = world.CharacterList.Single(c => c.Name == "TradeLeft");
            var hero = role.Heroes.First(h => h != null);
            hero.HP = -1; hero.ReviveAtUtc = DateTime.MinValue; hero.Magics.Clear();
            role.HeroSpawned = true; role.PMode = PetMode.MoveOnly;
            var injury = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离英雄阵亡药", Type = ItemType.Potion, Shape = 1, StackSize = 2,
                RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female, Stats = new Stats { [Stat.HP] = -100000 } };
            world.ItemInfoList.Add(injury); role.Inventory[10] = world.CreateFreshItem(injury); role.Inventory[10].Count = 2;
            var revive = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离英雄复活卷", Type = ItemType.Scroll, Shape = 6, StackSize = 10,
                RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female };
            world.ItemInfoList.Add(revive); role.Inventory[11] = world.CreateFreshItem(revive); role.Inventory[11].Count = 2;
            var gm = world.CharacterList.Single(c => c.Name == "TradeRight"); gm.AccountInfo.AdminAccount = true;
            gm.CurrentMapIndex = gm.BindMapIndex = role.CurrentMapIndex; gm.CurrentLocation = gm.BindLocation = role.CurrentLocation;
            world.MapInfoList.Add(new MapInfo { Index = world.MapInfoList.Max(map => map.Index) + 1, FileName = "R001", Title = "管理员" });
            world.SaveDB(); world.SaveAccounts();
            var setup = Path.Combine(paths.World, "Configs/Setup.ini");
            File.WriteAllText(setup, File.ReadAllText(setup).Replace("AllowCreateAssassin=False", "AllowCreateAssassin=True").Replace("AllowCreateArcher=False", "AllowCreateArcher=True"));
            Console.WriteLine("PASS isolated fresh hero, original lethal potion and GM saved town/admin map fixture.");
        }
        finally { DatabaseStorage.Current = null; }
    }
}
