using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.Helpers;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class ProgressionFeatureFixture
{
    internal static readonly Spell[] Selected = [Spell.Fencing, Spell.FireBall, Spell.Healing, Spell.FatalSword, Spell.Focus];
    internal static string Account(MirClass job) => "Balance" + job;

    internal static void Prepare(ProjectPaths paths)
    {
        if (!paths.Data.Contains("/mir2-balance.", StringComparison.Ordinal)) throw new InvalidOperationException("只允许隔离平衡验证版本。");
        using var gate = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        Settings.Load(); Settings.LoadBaseStats();
        var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("隔离世界读取失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load()); world.LoadAccounts();
        var sample = world.CharacterList.Single(role => role.Name == "TradeRight");
        var map = world.MapInfoList.Single(map => map.Index == sample.CurrentMapIndex);
        foreach (var definition in world.MapInfoList) definition.Respawns.Clear();
        ItemInfo Add(string name, ItemType type, ushort image)
        {
            var item = new ItemInfo { Index = ++world.ItemIndex, Name = name, Type = type, Image = image, Price = 20, StackSize = 999,
                RequiredClass = (RequiredClass)31, RequiredGender = RequiredGender.Male | RequiredGender.Female };
            world.ItemInfoList.Add(item); return item;
        }
        var material = Add("材料验证晶石", ItemType.CraftingMaterial, world.ItemInfoList.First(item => item.Type == ItemType.CraftingMaterial).Image);
        var ore = Add("材料验证矿石", ItemType.Ore, world.ItemInfoList.First(item => item.Type == ItemType.Ore).Image);
        var meat = Add("材料验证兽肉", ItemType.Meat, world.ItemInfoList.First(item => item.Type == ItemType.Meat).Image);
        var potion = Add("材料验证药品", ItemType.Potion, world.ItemInfoList.First(item => item.Type == ItemType.Potion).Image);
        potion.Stats[Stat.HP] = 1;
        var npc = new NPCInfo { Index = ++world.NPCIndex, MapIndex = map.Index, Name = "百工叟", FileName = "BalanceMaterials", Location = new(sample.CurrentLocation.X - 1, sample.CurrentLocation.Y) };
        world.NPCInfoList = [npc];
        var quest = new QuestInfo { Index = ++world.QuestIndex, Name = "材料交付验证", FileName = "BalanceMaterials", RequiredMinLevel = 1, RequiredMaxLevel = 255 };
        world.QuestInfoList = [quest];
        Directory.CreateDirectory(Settings.NPCPath); Directory.CreateDirectory(Settings.QuestPath); Directory.CreateDirectory(Settings.RecipePath);
        File.WriteAllText(Path.Combine(Settings.NPCPath, "BalanceMaterials.txt"), $"[@MAIN]\n#SAY\n材料交付与制作验证。\n<制作/@CRAFT>\n[@CRAFT]\n#SAY\n[Quests]\n{quest.Index}\n[Recipe]\n{potion.Name}\n");
        File.WriteAllText(Path.Combine(Settings.QuestPath, "BalanceMaterials.txt"), $"[@DESCRIPTION]\n交付兽肉。\n[@ITEMTASKS]\n{meat.Name} 2\n[@GOLDREWARD]\n17\n");
        File.WriteAllText(Path.Combine(Settings.RecipePath, potion.Name + ".txt"), $"[recipe]\nchance 100\ngold 3\n[ingredients]\n{material.Name} 2\n");
        foreach (var job in Enum.GetValues<MirClass>())
        {
            var spell = Selected[(int)job];
            var model = world.ItemInfoList.First(item => item.Type == ItemType.Book && item.Shape == (int)spell);
            var book = Add("自动学习验证" + job.ToLocalizedString(), ItemType.Book, model.Image);
            book.Shape = (short)spell; book.RequiredClass = (RequiredClass)(1 << (int)job);
            book.RequiredAmount = world.MagicInfoList.Single(magic => magic.Spell == spell).Level1;
            if (world.AccountList.Any(account => account.AccountID == Account(job))) throw new InvalidOperationException("夹具已经创建，拒绝覆盖测试资产。");
            var account = new AccountInfo { Index = ++world.NextAccountID, AccountID = Account(job), Password = "Native123", AdminAccount = true, Gold = 100000,
                CreationIP = "127.0.0.1", CreationDate = DateTime.UtcNow };
            var role = new CharacterInfo { Index = ++world.NextCharacterID, Name = "验证" + job.ToLocalizedString(), AccountInfo = account, Class = job, Gender = MirGender.Male,
                Level = 50, HP = 10000, MP = 10000, CreationIP = "127.0.0.1", CreationDate = DateTime.UtcNow,
                CurrentMapIndex = map.Index, BindMapIndex = map.Index, CurrentLocation = sample.CurrentLocation, BindLocation = sample.CurrentLocation,
                MaximumHeroCount = 5, Heroes = new HeroInfo[5], PMode = PetMode.MoveOnly, Inventory = new UserItem[80] };
            var hero = new HeroInfo(new ClientPackets.NewHero { Name = "验证英雄" + job.ToLocalizedString(), Class = job, Gender = MirGender.Male })
                { Index = ++world.NextHeroID, Level = 1, HP = 10000, MP = 10000, Inventory = [], CurrentMapIndex = map.Index, CurrentLocation = sample.CurrentLocation };
            role.Heroes[0] = hero; role.CurrentHeroIndex = hero.Index; role.HeroSpawned = true;
            UserItem Fresh(ItemInfo info, ushort count) { var item = world.CreateFreshItem(info); item.Count = count; return item; }
            role.Inventory[6] = Fresh(material, 600); role.Inventory[7] = Fresh(material, 600);
            role.Inventory[8] = Fresh(ore, 3); role.Inventory[8].CurrentDura = role.Inventory[8].MaxDura = 5200;
            role.Inventory[9] = Fresh(meat, 3); role.Inventory[10] = Fresh(potion, 2);
            role.QuestInventory[0] = Fresh(world.ItemInfoList.First(item => item.Type == ItemType.Torch), 1);
            account.Characters.Add(role); world.AccountList.Add(account); world.CharacterList.Add(role); world.HeroList.Add(hero);
        }
        var low = world.ItemInfoList.First(item => item.Type == ItemType.Book && item.Price > 0 && PublicShopCatalogRules.SkillBookAllowed(item, world));
        var high = world.ItemInfoList.First(item => item.Type == ItemType.Book && item.Price > 0 && !PublicShopCatalogRules.SkillBookAllowed(item, world) &&
            world.MagicInfoList.Any(magic => (int)magic.Spell == item.Shape));
        var catalog = new PublicShopCatalog { Tabs = [new PublicShopTab { Id = 1, Name = "验证货架", Products = [new() { ItemIndex = material.Index, PriceRate = .35m }, new() { ItemIndex = low.Index, PriceRate = .45m }, new() { ItemIndex = high.Index, PriceRate = .55m }] }] };
        storage.Write(PublicShopCatalogRules.Snapshot, JsonSerializer.SerializeToUtf8Bytes(catalog));
        world.SaveDB(); world.SaveAccounts();
        File.WriteAllText(Path.Combine(paths.Runtime, "progression-feature-fixture.json"), JsonSerializer.Serialize(new { Material = material.Index, Ore = ore.Index, Meat = meat.Index, Potion = potion.Index,
            LowBook = low.Index, HighBook = high.Index, Quest = quest.Index, Npc = npc.Index }));
        DatabaseStorage.Current = null;
        Console.WriteLine("PASS isolated five-class hero/material/legacy-shop fixture; no production accounts modified.");
    }
}
