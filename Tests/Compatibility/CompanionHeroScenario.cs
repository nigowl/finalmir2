using System.Drawing;
using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class CompanionHeroScenario
{
    public static void Run(ProjectPaths paths, int characterId, string mode)
    {
        if (!paths.Data.Contains("mir2-companions-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("仅允许修改本测试的独立版本。");
        using var gate = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        try
        {
            var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
            world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
            var role = world.CharacterList.Single(role => role.Index == characterId);
            if (mode == "audit")
            {
                var hero = role.Heroes.SingleOrDefault(hero => hero != null);
                Write(new { Role = role.Index, role.AccountInfo.Gold, role.CompletedQuests, Heroes = role.Heroes.Count(hero => hero != null),
                    Hero = hero == null ? null : new { hero.Index, hero.Name, Class = hero.Class.ToString(), hero.HP, hero.MP, hero.Level, hero.ReviveAtUtc,
                        Spells = hero.Magics.Select(magic => magic.Spell.ToString()).ToArray(), Equipment = hero.Equipment.Where(item => item != null).Select(item => new { item.ItemIndex, item.UniqueID }).ToArray() },
                    Seals = role.Inventory.Count(item => item?.Info.Type == ItemType.SealedHero),
                    Inventory = role.Inventory.Concat(role.Equipment).Where(item => item != null).Select(item => new { item.ItemIndex, item.UniqueID, item.Count }).ToArray() });
                return;
            }
            if (mode == "dead")
            {
                var hero = role.Heroes.Single(hero => hero != null); hero.HP = 0; hero.ReviveAtUtc = DateTime.UtcNow.AddSeconds(60);
                role.HeroSpawned = false; world.SaveAccounts(); Write(new { hero.Index, hero.ReviveAtUtc }); return;
            }
            if (mode == "medicine")
            {
                var hero = role.Heroes.Single(hero => hero != null); hero.HP = hero.MP = 1;
                hero.Equipment[(int)EquipmentSlot.Torch] = null; role.HeroSpawned = true;
                role.HP = role.MP = 10000;
                for (var i = 0; i < role.Inventory.Length; i++)
                    if (MagicBloodStone.IsStone(role.Inventory[i]?.Info)) role.Inventory[i] = null;
                foreach (var map in world.MapInfoList) map.Respawns.Clear();
                world.SaveDB(); world.SaveAccounts(); Write(new { hero.Index }); return;
            }
            if (mode is not ("prepare" or "tier40")) throw new ArgumentException("未知夹具模式。");
            var npc = world.NPCInfoList.Single(npc => npc.FileName == "MongchonProvince/MudWall/盟重日常委托");
            var townInfo = world.MapInfoList.Single(map => map.Index == npc.MapIndex);
            Settings.MapPath = Path.Combine(paths.Assets, "maps"); var town = new Map(townInfo);
            if (!town.Load()) throw new InvalidDataException("隔离日常地图加载失败。");
            role.Level = (ushort)(mode == "prepare" ? 30 : 40); role.HP = 200; role.MP = 100;
            role.CurrentMapIndex = role.BindMapIndex = npc.MapIndex; role.CurrentLocation = role.BindLocation = npc.Location + new Size(0, 1);
            if (mode == "prepare")
            {
                if (role.Heroes.Any(hero => hero != null)) throw new InvalidOperationException("测试角色已有英雄，拒绝覆盖。");
                role.CurrentQuests.Clear(); role.CompletedQuests.Clear(); role.Magics.Clear(); role.QuestInventory = new UserItem[40];
                role.Inventory = new UserItem[80]; role.Equipment = new UserItem[14]; role.Heroes = new HeroInfo[role.MaximumHeroCount]; role.CurrentHeroIndex = 0;
                role.AccountInfo.Gold = 1_000_000; role.HeroSpawned = false;
                var sword = world.ItemInfoList.First(item => item.Type == ItemType.Weapon && item.Shape == 1);
                sword.RequiredClass = RequiredClass.Warrior; sword.RequiredType = RequiredType.Level; sword.RequiredAmount = 1; sword.Weight = 0;
                sword.Stats[Stat.MinDC] = sword.Stats[Stat.MaxDC] = 200; sword.Stats[Stat.Accuracy] = 100;
                role.Equipment[0] = world.CreateFreshItem(sword);
                foreach (var stat in new[] { Stat.HP, Stat.MP })
                {
                    var potion = world.ItemInfoList.First(item => item.Type == ItemType.Potion && item.Shape == 0 && item.Stats[stat] > 0 && item.StackSize >= 20);
                    potion.RequiredAmount = 1; potion.RequiredClass = RequiredClass.None;
                    role.Inventory[stat == Stat.HP ? 0 : 1] = world.CreateFreshItem(potion); role.Inventory[stat == Stat.HP ? 0 : 1].Count = 50;
                }
                var scroll = world.ItemInfoList.Single(item => item.Name == "回城卷轴"); role.Inventory[2] = world.CreateFreshItem(scroll);
                var bow = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离英雄普通弓", Type = ItemType.Weapon, Shape = (short)(Globals.ClassWeaponCount * 2),
                    RequiredClass = RequiredClass.Archer, RequiredAmount = 1, Durability = 5000, Price = 100, Stats = new Stats { [Stat.MinMC] = 200, [Stat.MaxMC] = 200, [Stat.Accuracy] = 100 } };
                var book = new ItemInfo { Index = ++world.ItemIndex, Name = "隔离英雄普通射术书", Type = ItemType.Book, Shape = (short)Spell.StraightShot,
                    RequiredClass = RequiredClass.Archer, RequiredAmount = 1, Price = 100 };
                world.ItemInfoList.AddRange([bow, book]); world.MagicInfoList.Single(magic => magic.Spell == Spell.StraightShot).Level1 = 1;
                world.NPCInfoList.RemoveAll(other => other != npc);
                var heroGuide = new NPCInfo { Index = ++world.NPCIndex, MapIndex = npc.MapIndex, Location = npc.Location + new Size(1, 0), Name = "隔离英雄使者", FileName = "CompanionHeroesGuide" };
                var shop = new NPCInfo { Index = ++world.NPCIndex, MapIndex = npc.MapIndex, Location = npc.Location + new Size(-1, 0), Name = "隔离英雄书店", FileName = "CompanionHeroesShop" };
                world.NPCInfoList.AddRange([heroGuide, shop]);
                File.WriteAllText(Path.Combine(Settings.NPCPath, heroGuide.FileName + ".txt"), "[@MAIN]\n#SAY\n<<创建英雄/@CreateHero/Green>> <管理英雄/@MANAGEHERO>\n[@CREATEHERO]\n#SAY\n按原服许可办理。\n[@MANAGEHERO]\n#SAY\n原英雄管理。\n");
                File.WriteAllText(Path.Combine(Settings.NPCPath, shop.FileName + ".txt"), $"[@MAIN]\n#SAY\n<<购买/@BUY/Yellow>>\n[@BUY]\n#SAY\n普通职业装备和技能书。\n[Trade]\n{bow.Name} 1\n{book.Name} 1\n");
                Settings.AllowNewHero = true; Settings.Hero_CanCreateClass = [false, false, false, false, true];
                Settings.HeroSealItemName = world.ItemInfoList.First(item => item.Type == ItemType.SealedHero).Name; Settings.SaveHeroSettings();
            }
            else world.ProcessNewDay();
            foreach (var map in world.MapInfoList) map.Respawns.Clear();
            var targets = world.MonsterInfoList.Where(monster => (role.Level == 30 ? new[] { "红野猪", "黑野猪" } : new[] { "祖玛弓箭手", "剧毒蜘蛛" }).Contains(monster.Name)).ToArray();
            if (targets.Length != 2) throw new InvalidDataException("隔离分档怪物缺失。");
            var spots = town.WalkableCells.Where(point => town.GetSafeZone(point) == null && Functions.MaxDistance(point, npc.Location) is >= 9 and <= 18 &&
                townInfo.Movements.All(exit => exit.Source != point)).OrderBy(point => Functions.MaxDistance(point, npc.Location)).Take(40).ToArray();
            if (spots.Length != 40) throw new InvalidDataException("隔离掉落落点不足。");
            foreach (var monster in targets)
            {
                monster.Stats[Stat.HP] = 1; monster.Stats[Stat.MinDC] = monster.Stats[Stat.MaxDC] = 0; monster.ViewRange = 0; monster.Experience = 0;
                monster.DropPath = "CompanionHeroesDaily" + role.Level;
            }
            var daily = world.QuestInfoList.Where(quest => quest.MengchongDaily && quest.RequiredMinLevel <= role.Level && quest.RequiredMaxLevel >= role.Level).ToArray();
            File.WriteAllText(Path.Combine(Settings.DropPath, targets[0].DropPath + ".txt"),
                string.Join('\n', daily.SelectMany(quest => quest.ItemTasks).Select(task => $"1/2 {task.Item.Name} Q")) + "\n");
            for (var i = 0; i < spots.Length; i++) townInfo.Respawns.Add(new RespawnInfo { RespawnIndex = ++world.RespawnIndex,
                MonsterIndex = targets[i % 2].Index, Location = spots[i], Count = 1, Delay = 1 });
            world.SaveDB(); world.SaveAccounts();
            Write(new { role.Level, HeroWeapon = world.ItemInfoList.Single(item => item.Name == "隔离英雄普通弓").Index,
                PotionIds = role.Inventory.Where(item => item?.Info.Type == ItemType.Potion).Select(item => item.ItemIndex).Distinct().ToArray(),
                DailyIds = daily.Select(quest => quest.Index).ToArray() });
        }
        finally { DatabaseStorage.Current = null; }
        void Write(object value) => File.WriteAllText(Path.Combine(paths.Runtime, "companion-heroes-" + mode + ".json"), JsonSerializer.Serialize(value));
    }
}
