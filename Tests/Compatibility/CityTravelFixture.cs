using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirEnvir;

internal static class CityTravelFixture
{
    public static void Prepare(ProjectPaths paths, bool regression = false)
    {
        if (!paths.Data.StartsWith("/private/tmp/finalmir2-daily.", StringComparison.Ordinal) &&
            !paths.Data.StartsWith("/private/tmp/finalmir2-travel-menu.", StringComparison.Ordinal) &&
            !paths.Data.Contains("/mir2-city-travel.", StringComparison.Ordinal))
            throw new InvalidOperationException("传送夹具只允许在独立临时版本运行。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database);
        DatabaseStorage.Current = storage;
        var world = Envir.Main;
        if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(Server.MirDatabase.BuffInfo.Load()); world.LoadAccounts();
        var targets = Enumerable.Range(0, 16).Select(i => $"TravelMenu{i}")
            .Concat(new[] { "TravelPoor", "TravelFull", "TravelWeight", "TravelOwned", "TravelNative" }).ToArray();
        var scroll = world.ItemInfoList.Single(item => item.Name == "回城卷轴" && item.Type == ItemType.Scroll && item.Shape == 1);
        var filler = world.ItemInfoList.Where(item => item.StackSize == 1 && item.Type == ItemType.Weapon).OrderBy(item => item.UnitWeight).First();
        var heavy = world.ItemInfoList.Where(item => item.StackSize == 1 && item.Type == ItemType.Weapon).OrderByDescending(item => item.UnitWeight).First();
        foreach (var name in targets)
        {
            var role = world.CharacterList.Single(c => c.Name == name && c.AccountInfo.AccountID == name);
            if (role.Level != 0 && !regression) throw new InvalidOperationException("隔离角色已初始化，不能重复覆盖。");
            role.Level = 1; role.HP = 100; role.MP = 100;
            role.CurrentMapIndex = role.BindMapIndex = 1;
            role.CurrentLocation = role.BindLocation = new(288, 616);
            role.AccountInfo.Gold = name == "TravelPoor" ? 1999u : 100_000u;
            role.Inventory = new UserItem[regression ? Globals.BaseInventorySize : role.Inventory.Length];
            if (name == "TravelFull")
                for (var slot = 0; slot < role.Inventory.Length; slot++) role.Inventory[slot] = world.CreateFreshItem(filler);
            if (name == "TravelWeight")
                for (var slot = 0; slot < 20; slot++) role.Inventory[slot] = world.CreateFreshItem(heavy);
            if (name == "TravelOwned") { role.Inventory[0] = world.CreateFreshItem(scroll); role.Inventory[0].Count = 5; }
        }
        if (regression)
        {
            foreach (var map in world.MapInfoList)
            { map.Respawns.Clear(); }
            var town = world.MapInfoList.Single(map => map.Index == 1);
            foreach (var zone in town.SafeZones.Where(zone => zone.Location == new System.Drawing.Point(288, 616))) zone.Size = 0;
            var tiles = new Client.MirObjects.MapReader(Path.Combine(paths.Assets, "maps", town.FileName + ".map"));
            var start = new[] { new System.Drawing.Point(289, 616), new(288, 617), new(288, 615) }.First(p =>
                (tiles.MapCells[p.X, p.Y].BackImage & 0x20000000) == 0 && (tiles.MapCells[p.X, p.Y].FrontImage & 0x8000) == 0 &&
                world.NPCInfoList.All(npc => npc.MapIndex != 1 || npc.Location != p));
            foreach (var name in targets) world.CharacterList.Single(c => c.Name == name).CurrentLocation = start;
            world.CharacterList.Single(c => c.Name == "TravelOwned").CurrentLocation = new(328, 264);
            world.SaveDB();
        }
        world.SaveAccounts();
        Console.WriteLine("已准备独立传送角色：费用、缺卷、已有卷、满包和超重；不修改正式角色。");
    }
}
