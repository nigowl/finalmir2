using System.Drawing;
using System.Text.Json;
using Client.MirObjects;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class MovementStateFixture
{
    public static void Configure(Envir world, MapReader tiles, string root)
    {
        if (!root.Contains("/mir2-movement.", StringComparison.Ordinal)) throw new InvalidOperationException("移动夹具仅允许独立临时版本。");
        var map = world.MapInfoList.Single(map => map.Index == 1);
        bool Clear(Point p) => p.X >= 0 && p.Y >= 0 && p.X < tiles.Width && p.Y < tiles.Height &&
            (tiles.MapCells[p.X, p.Y].BackImage & 0x20000000) == 0 && (tiles.MapCells[p.X, p.Y].FrontImage & 0x8000) == 0 &&
            world.NPCInfoList.All(npc => npc.MapIndex != map.Index || npc.Location != p) && map.SafeZones.All(zone => !Functions.InRange(p, zone.Location, zone.Size));
        var candidates = from x in Enumerable.Range(1, tiles.Width - 2)
                         from y in Enumerable.Range(1, tiles.Height - 2)
                         let door = new Point(x, y)
                         let cell = tiles.MapCells[x, y]
                         where cell.DoorIndex != 0 && cell.DoorOffset > 0 && (cell.FrontImage & 0x7fff) > 0 && Clear(door)
                         from direction in new[] { MirDirection.Down, MirDirection.Right, MirDirection.Up, MirDirection.Left }
                         let before = Functions.PointMove(door, Functions.ReverseDirection(direction), 1)
                         let after = Functions.PointMove(door, direction, 1)
                         where Clear(before) && Clear(after) && tiles.MapCells[before.X, before.Y].DoorIndex == 0
                         from pushDirection in Enum.GetValues<MirDirection>()
                         let mage = Functions.PointMove(before, Functions.ReverseDirection(pushDirection), 1)
                         where Clear(mage) && mage != door && mage != after && Enumerable.Range(1, 4).All(step =>
                             Clear(Functions.PointMove(before, pushDirection, step)) && Functions.PointMove(before, pushDirection, step) != mage)
                         select new { Door = door, Before = before, After = after, Mage = mage, cell.DoorIndex };
        var fixture = candidates.First();
        foreach (var definition in world.MapInfoList) { definition.Respawns.Clear(); definition.Movements.Clear(); }
        var player = world.CharacterList.Single(role => role.Name == "NativeGM");
        player.CurrentLocation = fixture.Before; player.CurrentMapIndex = map.Index; player.HP = 1000; player.MP = 1000;
        player.Inventory = new UserItem[80]; player.Equipment = new UserItem[14];
        var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = "NativePush", Password = "Native123", CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
        var mageRole = new CharacterInfo { Index = ++world.NextCharacterID, Name = "NativePush", Class = MirClass.Wizard, Gender = MirGender.Female, Level = 80,
            AccountInfo = account, HP = 1000, MP = 1000, CurrentMapIndex = map.Index, CurrentLocation = fixture.Mage,
            BindMapIndex = map.Index, BindLocation = fixture.Mage, Heroes = new HeroInfo[1], CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
        mageRole.Magics.Add(new UserMagic(Spell.Repulsion) { Level = 3 });
        account.Characters.Add(mageRole); world.AccountList.Add(account); world.CharacterList.Add(mageRole);
        world.SaveAccounts();
        File.WriteAllText(Path.Combine(root, "movement.json"), JsonSerializer.Serialize(fixture));
        Console.WriteLine("Movement fixture: " + JsonSerializer.Serialize(fixture));
    }
}
