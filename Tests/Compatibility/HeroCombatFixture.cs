using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class HeroCombatFixture
{
    internal static Spell[] Skills(MirClass job) => job switch
    {
        MirClass.Warrior => [Spell.ProtectionField, Spell.Rage, Spell.FlamingSword, Spell.TwinDrakeBlade, Spell.CrossHalfMoon],
        MirClass.Wizard => [Spell.MagicShield, Spell.MagicBooster, Spell.Mirroring, Spell.IceStorm, Spell.ThunderBolt, Spell.FireBall],
        MirClass.Taoist => [Spell.Healing, Spell.SummonShinsu, Spell.SoulShield, Spell.BlessedArmour, Spell.PetEnhancer, Spell.Poisoning, Spell.SoulFireBall],
        MirClass.Assassin => [Spell.Haste, Spell.LightBody, Spell.CrescentSlash, Spell.HeavenlySword, Spell.DoubleSlash],
        _ => [Spell.ElementalBarrier, Spell.Meditation, Spell.Concentration, Spell.NapalmShot, Spell.DoubleShot]
    };
    internal static string Account(MirClass job) => job == MirClass.Warrior ? "NativeSkills" : "Native" + job;
    public static void Prepare(ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("英雄战斗准备仅允许隔离版本。");
        using var ownership = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        try
        {
            var world = Envir.Main;
            if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
            world.LoadAccounts();
            foreach (var job in Enum.GetValues<MirClass>())
            {
                var owner = world.CharacterList.Single(role => role.AccountInfo.AccountID == Account(job));
                if (owner.Heroes.Any(hero => hero != null)) throw new InvalidDataException("隔离英雄已准备，拒绝覆盖。");
                var hero = new HeroInfo(new ClientPackets.NewHero { Class = job, Gender = owner.Gender, Name = "组合" + job.ToLocalizedString() })
                    { Index = ++world.NextHeroID, Level = 50, HP = 10000, MP = 10000, Inventory = [], CurrentMapIndex = owner.CurrentMapIndex, CurrentLocation = owner.CurrentLocation };
                hero.Equipment[0] = world.CreateFreshItem(owner.Equipment[0].Info);
                var amulet = world.ItemInfoList.Single(item => item.Name == "隔离技能测试护身符");
                if (job == MirClass.Taoist)
                {
                    hero.Equipment[(int)EquipmentSlot.Amulet] = world.CreateFreshItem(amulet); hero.Equipment[(int)EquipmentSlot.Amulet].Count = 500;
                    hero.Equipment[(int)EquipmentSlot.BraceletR] = world.CreateFreshItem(world.ItemInfoList.First(item => item.Type == ItemType.Amulet && item.Shape == 2));
                    hero.Equipment[(int)EquipmentSlot.BraceletR].Count = 100; owner.HP = 4500;
                }
                hero.Magics = Skills(job).Select(spell => new UserMagic(spell) { Level = 3, Key = 0, CastTime = -100000 }).ToList();
                owner.Heroes = [hero]; owner.CurrentHeroIndex = hero.Index; owner.HeroSpawned = true; owner.PMode = PetMode.Both; owner.HeroBehaviour = HeroBehaviour.Attack;
                world.HeroList.Add(hero);
            }
            world.SaveDB(); world.SaveAccounts();
            Console.WriteLine("PASS isolated five-class hero setup: ordinary skills, all keys unbound, original equipment/materials and original targets.");
        }
        finally { DatabaseStorage.Current = null; }
    }
}
