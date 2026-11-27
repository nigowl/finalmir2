using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionPickupScenario
{
    public static void Prepare(ProjectPaths paths, int characterId, bool combat)
    {
        if (!paths.Data.Contains("mir2-companions-pickup-", StringComparison.Ordinal)) throw new InvalidOperationException("只允许本拾取隔离夹具。");
        using var gate = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
        var role = world.CharacterList.Single(role => role.Index == characterId);
        role.Level = 40; role.HP = role.MP = 10000; role.Experience = 0; role.AccountInfo.Gold = combat ? 0u : 306u;
        role.CurrentMapIndex = role.BindMapIndex; role.CurrentLocation = role.BindLocation = new(10, 10);
        role.Inventory = new UserItem[80]; role.Equipment = new UserItem[14]; role.Magics.Clear(); role.CurrentQuests.Clear(); role.CompletedQuests.Clear();
        foreach (var map in world.MapInfoList) { map.Respawns.Clear(); map.Movements.Clear(); map.SafeZones.Clear(); map.NoTownTeleport = true; }
        foreach (var info in world.ItemInfoList) info.Price = 0;
        world.QuestInfoList = []; world.NPCInfoList = [];
        var local = world.GetMapInfo(role.CurrentMapIndex); local.FileName = "NativePickup"; local.NoFight = false; local.NoDropMonster = false;
        local.SafeZones.Add(new() { Info = local, Location = role.BindLocation, Size = 0, StartPoint = true });
        var header = new byte[52]; BitConverter.GetBytes((short)32).CopyTo(header, 0); BitConverter.GetBytes((short)32).CopyTo(header, 2);
        using (var writer = new BinaryWriter(File.Create(Path.Combine(paths.Assets, "maps", local.FileName + ".map"))))
        { writer.Write(header); for (var x = 0; x < 32; x++) for (var y = 0; y < 32; y++) writer.Write(new byte[12]); }
        ItemInfo Add(string name, ItemType type, uint price, Stats stats)
        {
            var info = new ItemInfo { Index = ++world.ItemIndex, Name = name, Type = type, Price = price, Stats = stats,
                StackSize = type == ItemType.Potion ? (ushort)999 : (ushort)1, Durability = type == ItemType.Potion ? (ushort)0 : (ushort)10000 };
            world.ItemInfoList.Add(info); return info;
        }
        var red = Add("隔离拾取红药", ItemType.Potion, 0, new Stats { [Stat.HP] = 30 });
        var blue = Add("隔离拾取蓝药", ItemType.Potion, 0, new Stats { [Stat.MP] = 30 });
        var blade = Add("隔离基础补装剑", ItemType.Weapon, 40, new Stats { [Stat.MinDC] = 50, [Stat.MaxDC] = 50, [Stat.Accuracy] = 100 });
        var ring = Add("隔离掉落戒指", ItemType.Ring, 0, new Stats { [Stat.MaxAC] = 1 });
        role.Inventory[0] = world.CreateFreshItem(red); role.Inventory[0].Count = combat ? (ushort)20 : (ushort)1;
        role.Inventory[1] = world.CreateFreshItem(blue); role.Inventory[1].Count = 20;
        if (combat) role.Inventory[2] = world.CreateFreshItem(blade);
        var weaponId = role.Inventory[2]?.UniqueID ?? 0;
        if (!combat)
        {
            var npc = new NPCInfo { Index = ++world.NPCIndex, Name = "隔离铁匠", FileName = "NativePickupShop", MapIndex = local.Index, Location = new(13, 10), Rate = 120 };
            world.NPCInfoList.Add(npc);
            File.WriteAllText(Path.Combine(Settings.NPCPath, npc.FileName + ".txt"), $"[@MAIN]\n#SAY\n<购买/@BUY>\n[@BUY]\n[Types]\n{(int)ItemType.Weapon}\n[Trade]\n{blade.Name} 1\n");
        }
        world.MonsterInfoList = [new MonsterInfo { Index = ++world.MonsterIndex, Name = "隔离持续掉落鸡", Image = Monster.Hen, AI = 2, Level = 1, ViewRange = 0,
            MoveSpeed = ushort.MaxValue, AttackSpeed = 2500, Experience = 1, Stats = new Stats { [Stat.HP] = 1 }, DropPath = "NativePickup" }];
        local.Respawns.Add(new() { RespawnIndex = 1, MonsterIndex = world.MonsterIndex, Location = new(14, 14), Spread = 4, Count = 40, Delay = 1 });
        File.WriteAllText(Path.Combine(Settings.DropPath, "NativePickup.txt"), $"1/1 Gold 50\n1/1 {red.Name}\n1/1 {blue.Name}\n1/1 {ring.Name}\n");
        File.WriteAllText(Path.Combine(paths.Runtime, "companion-pickup-fixture.json"), JsonSerializer.Serialize(new { red = red.Index, blue = blue.Index, blade = blade.Index, ring = ring.Index, weaponId, combat }));
        world.SaveDB(); world.SaveAccounts();
    }
}
