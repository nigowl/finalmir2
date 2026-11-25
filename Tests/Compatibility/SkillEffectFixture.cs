using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class SkillEffectFixture
{
    public static void Configure(Envir world, MapInfo map, Point start, MonsterInfo[] summons)
    {
        map.Respawns.Clear();
        foreach (var summon in summons)
        {
            summon.Index = ++world.MonsterIndex; world.MonsterInfoList.Add(summon);
            var drop = Path.Combine("Envir", "Drops", (string.IsNullOrEmpty(summon.DropPath) ? summon.Name : summon.DropPath).Replace('\\', '/') + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(drop)!); File.WriteAllText(drop, "; 隔离召唤检查不产生掉落。\n");
        }
        // 不移动的原AI保持群攻靶位，避免随机游荡使长时间连招检查失去群怪条件。
        var target = new MonsterInfo { Index = ++world.MonsterIndex, Name = "技能测试靶怪", Image = Monster.Hen, AI = 13,
            Level = 1, ViewRange = 0, MoveSpeed = ushort.MaxValue, AttackSpeed = ushort.MaxValue, Stats = new Stats { [Stat.HP] = 1000000 } };
        world.MonsterInfoList.Add(target);
        foreach (var point in new[] { new Point(start.X + 1, start.Y), new Point(start.X + 2, start.Y), new Point(start.X + 2, start.Y + 1) })
            map.Respawns.Add(new RespawnInfo { RespawnIndex = ++world.RespawnIndex, MonsterIndex = target.Index, Location = point, Count = 1, Delay = 600 });
        var weapon = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离技能测试武器", Type = ItemType.Weapon, Shape = 1, Durability = 60000,
            Stats = new Stats { [Stat.HP] = 10000, [Stat.MP] = 10000, [Stat.MinDC] = 100, [Stat.MaxDC] = 100, [Stat.MinMC] = 100, [Stat.MaxMC] = 100,
                [Stat.MinSC] = 100, [Stat.MaxSC] = 100, [Stat.Accuracy] = 100, [Stat.Luck] = 100 } };
        var amulet = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离技能测试护身符", Type = ItemType.Amulet, Shape = 0, StackSize = 1000 };
        world.ItemInfoList.AddRange([weapon, amulet]);
        var injury = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离自疗测试药", Type = ItemType.Potion, Shape = 1,
            StackSize = 1, Stats = new Stats { [Stat.HP] = -6000 } }; world.ItemInfoList.Add(injury);
        var drain = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离缺蓝测试药", Type = ItemType.Potion, Shape = 1,
            StackSize = 1, Stats = new Stats { [Stat.MP] = -30000 } }; world.ItemInfoList.Add(drain);
        var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = "NativeSkills", Password = "Native123", CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
        var character = new CharacterInfo { Index = ++world.NextCharacterID, Name = "技能特效检查", Class = MirClass.Warrior, Gender = MirGender.Male, Level = 50,
            HP = 10000, MP = 10000, CurrentMapIndex = map.Index, CurrentLocation = start, BindMapIndex = map.Index, BindLocation = start,
            Equipment = new UserItem[14], Inventory = new UserItem[46], AccountInfo = account, CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1", Heroes = new HeroInfo[1] };
        character.Equipment[0] = world.CreateFreshItem(weapon);
        character.Equipment[(int)EquipmentSlot.Amulet] = world.CreateFreshItem(amulet); character.Equipment[(int)EquipmentSlot.Amulet].Count = 1000;
        character.Inventory[3] = world.CreateFreshItem(injury); character.Inventory[5] = world.CreateFreshItem(drain);
        character.Magics = world.MagicInfoList.Where(info => (byte)info.Spell is > 0 and < 200).GroupBy(info => info.Spell)
            .Select(group => new UserMagic(group.Key) { Level = group.Key is Spell.Slaying or Spell.Thrusting ? (byte)2 : (byte)3, CastTime = -100000 }).ToList();
        account.Characters.Add(character); world.AccountList.Add(account); world.CharacterList.Add(character); world.SaveAccounts();
        foreach (var job in new[] { MirClass.Taoist, MirClass.Wizard, MirClass.Assassin, MirClass.Archer })
        {
            var jobAccount = new AccountInfo { Index = ++world.NextAccountID, AccountID = $"Native{job}", Password = "Native123", CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1" };
            var actor = new CharacterInfo { Index = ++world.NextCharacterID, Name = $"{job.ToLocalizedString()}检查", Class = job, Gender = MirGender.Male, Level = 50,
                HP = 10000, MP = 10000, CurrentMapIndex = map.Index, CurrentLocation = start, BindMapIndex = map.Index, BindLocation = start,
                Equipment = new UserItem[14], Inventory = new UserItem[46], AccountInfo = jobAccount, CreationDate = DateTime.UtcNow, CreationIP = "127.0.0.1", Heroes = new HeroInfo[1] };
            actor.Equipment[0] = world.CreateFreshItem(weapon);
            if (job is MirClass.Assassin or MirClass.Archer)
            {
                var classWeapon = new ItemInfo { Index = ++world.ItemIndex, Name = $"隔离{job.ToLocalizedString()}测试武器", Type = ItemType.Weapon,
                    Shape = (short)(job == MirClass.Assassin ? 100 : 200), Durability = weapon.Durability, Stats = weapon.Stats };
                world.ItemInfoList.Add(classWeapon); actor.Equipment[0] = world.CreateFreshItem(classWeapon);
            }
            actor.Inventory[0] = world.CreateFreshItem(amulet); actor.Inventory[0].Count = 1000;
            actor.Inventory[3] = world.CreateFreshItem(injury);
            actor.Inventory[5] = world.CreateFreshItem(drain);
            for (short shape = 1; shape <= 2; shape++)
            {
                var poison = world.ItemInfoList.First(info => info.Type == ItemType.Amulet && info.Shape == shape);
                actor.Inventory[shape] = world.CreateFreshItem(poison); actor.Inventory[shape].Count = 100;
            }
            actor.Magics = character.Magics.Select(magic => new UserMagic(magic.Spell) { Level = 3, CastTime = -100000 }).ToList();
            if(job is MirClass.Assassin or MirClass.Archer)
            {
                var allowed=job==MirClass.Assassin?new[]{Spell.Haste,Spell.LightBody,Spell.DoubleSlash,Spell.CrescentSlash,Spell.HeavenlySword,Spell.PoisonSword}:
                    [Spell.ElementalBarrier,Spell.Concentration,Spell.Meditation,Spell.StraightShot,Spell.DoubleShot,Spell.NapalmShot,Spell.VampireShot,Spell.PoisonShot,Spell.CrippleShot];
                actor.Magics.RemoveAll(magic=>!allowed.Contains(magic.Spell));
            }
            jobAccount.Characters.Add(actor); world.AccountList.Add(jobAccount); world.CharacterList.Add(actor);
        }
        world.SaveAccounts();
    }
}
