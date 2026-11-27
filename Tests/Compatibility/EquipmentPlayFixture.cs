using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class EquipmentPlayFixture
{
    public static void Prepare(ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-heroes.presentation.", StringComparison.Ordinal)) throw new InvalidOperationException("装备游玩夹具仅允许隔离版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        Directory.SetCurrentDirectory(paths.World);
        try
        {
            var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("读取隔离世界失败。");
            world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
            var role = world.CharacterList.Single(character => character.Name == "TradeRight");
            var map = world.MapInfoList.Single(map => map.Index == role.CurrentMapIndex);
            role.Class = MirClass.Wizard; role.Level = 40; role.HP = role.MP = 10000; role.AccountInfo.Gold = 100000;
            role.AccountInfo.ClearStoragePassword(); role.AccountInfo.Storage = new UserItem[80]; role.AccountInfo.HasExpandedStorage = false;
            role.Inventory = new UserItem[86]; role.Equipment = new UserItem[14]; role.Magics.Clear();
            role.Magics.Add(new UserMagic(Spell.FireBall) { Level = 0, Key = 1 });
            ItemInfo Definition(string name, ItemType type, short shape, ushort image) {
                var info = new ItemInfo { Index = ++world.ItemIndex, Name = name, Type = type, Shape = shape, Image = image, Weight = 1,
                    Durability = type is ItemType.Weapon or ItemType.Armour ? (ushort)10000 : (ushort)0, Price = 100,
                    RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female };
                world.ItemInfoList.Add(info); return info;
            }
            var weaponModel = world.ItemInfoList.First(item => item.Type == ItemType.Weapon && !item.IsFishingRod);
            var weapon = Definition("光效试炼剑", ItemType.Weapon, weaponModel.Shape, weaponModel.Image);
            weapon.Stats[Stat.MinDC] = 3; weapon.Stats[Stat.MaxDC] = 5;
            var armourModel = world.ItemInfoList.First(item => item.Type == ItemType.Armour && item.Shape == 0);
            var armour = Definition("光效试炼衣", ItemType.Armour, armourModel.Shape, armourModel.Image); armour.Stats[Stat.MaxAC] = 3;
            UserItem Item(ItemInfo info, ushort count = 1) { var item = world.CreateFreshItem(info); item.Count = count; return item; }
            role.Inventory[6] = Item(weapon); role.Inventory[6].Identified = false; role.Inventory[6].EquipmentQuality = EquipmentGrade.Supreme;
            role.Inventory[7] = Item(armour); role.Inventory[7].Identified = true; role.Inventory[7].EquipmentQuality = EquipmentGrade.Supreme; role.Inventory[7].AddedStats.Add(EquipmentAppraisal.Bonuses(role.Inventory[7]));
            role.Inventory[8] = Item(weapon); role.Inventory[8].CurrentDura = 4301;
            var potionModel = world.ItemInfoList.First(item => item.Type == ItemType.Potion && item.Stats[Stat.HP] > 0);
            var potion = Definition("整理试炼药", ItemType.Potion, 0, potionModel.Image); potion.StackSize = 20; potion.Stats[Stat.HP] = 10;
            var scrollModel = world.ItemInfoList.First(item => item.Type == ItemType.Scroll && item.Shape == 2);
            var scroll = Definition("整理试炼卷轴", ItemType.Scroll, 2, scrollModel.Image); scroll.StackSize = 10;
            role.Inventory[9] = Item(potion, 600); role.Inventory[10] = Item(potion, 600);
            role.Inventory[11] = Item(scroll, 600); role.Inventory[12] = Item(scroll, 600);
            role.Inventory[13] = Item(potion, 5); role.Inventory[13].SoulBoundId = 999999;
            var recycling = Definition("回收试炼剑", ItemType.Weapon, weaponModel.Shape, weaponModel.Image);
            role.Inventory[14] = Item(recycling); role.Inventory[14].EquipmentQuality = EquipmentGrade.Supreme; role.Inventory[14].Identified = false;
            foreach (short shape in new short[] { 0, 1, 2 })
            {
                var material = Definition("共用试炼毒符" + shape, ItemType.Amulet, shape, potionModel.Image); material.StackSize = 500;
                role.Inventory[15 + shape] = Item(material, shape == 0 ? (ushort)3 : (ushort)2);
            }
            role.HeroSpawned = true; role.PMode = PetMode.MoveOnly;
            foreach (var hero in role.Heroes.Where(hero => hero != null))
            {
                hero.Class = MirClass.Taoist; hero.Level = 40; hero.HP = hero.MP = 10000;
                hero.HeroBehaviour = HeroBehaviour.Attack; hero.Equipment = new UserItem[14];
                hero.Magics = new[] { Spell.Poisoning, Spell.SoulFireBall }.Select(spell => new UserMagic(spell) { Level = 0, Key = 0 }).ToList();
            }
            map.Respawns.Clear();
            foreach (var (name, hp, dx) in new[] { ("光效试炼靶", 100, 2), ("掉落试炼兽", 1, 3) })
            {
                var monster = new MonsterInfo { Index = ++world.MonsterIndex, Name = name, Image = Monster.Hen, AI = 0, Level = 1, ViewRange = 0,
                    Stats = new Stats { [Stat.HP] = hp }, MoveSpeed = ushort.MaxValue, AttackSpeed = ushort.MaxValue };
                world.MonsterInfoList.Add(monster);
                map.Respawns.Add(new RespawnInfo { RespawnIndex = ++world.RespawnIndex, MonsterIndex = monster.Index, Location = new(role.CurrentLocation.X + dx, role.CurrentLocation.Y), Count = 1, Delay = 600 });
                Directory.CreateDirectory(Path.Combine(paths.World, "Envir/Drops"));
                File.WriteAllText(Path.Combine(paths.World, "Envir/Drops/" + name + ".txt"), name == "掉落试炼兽" ? "1/1 光效试炼剑\n" : "");
            }
            world.SaveDB(); world.SaveAccounts();
            Console.WriteLine("PASS isolated equipment play fixture: hidden Supreme, wearable hero armour, ordinary duplicate, 1200 potions/scrolls, bound pile and real spell/drop targets.");
        }
        finally { DatabaseStorage.Current = null; }
    }
}
