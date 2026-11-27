using System.Text.Json;
using Crystal.Native;
using Server;
using Server.Helpers;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class ShopCatalogChecks
{
    private sealed class Memory : DatabaseStorage
    {
        public byte[]? Data;
        public override bool Contains(string name) => Data != null;
        public override Stream Read(string name) => new MemoryStream(Data!);
        public override void Write(string name, byte[] data) => Data = data;
        public override void Remove(string name) => Data = null;
        public override string[] List(string directory, string pattern) => [];
    }
    public static void Run(Action<bool, string> check)
    {
        var world = Envir.Main; var items = world.ItemInfoList.ToArray(); var catalog = world.PublicShopCatalog; var storage = DatabaseStorage.Current; var systemRate = Settings.PublicShopPriceRate;
        var memory = new Memory(); DatabaseStorage.Current = memory;
        try
        {
            world.ItemInfoList.Clear();
            var potion = new ItemInfo { Index = 98001, Name = "测试金创药", Type = ItemType.Potion, Shape = 0, Price = 20, StackSize = 99, Stats = new Stats { [Stat.HP] = 20 } };
            var sword = new ItemInfo { Index = 98002, Name = "测试普通剑", Type = ItemType.Weapon, Price = 100, StackSize = 1 };
            world.ItemInfoList.AddRange([potion, sword]); world.PublicShopCatalog = null;
            check(PublicShopCatalogRules.Load(world) == null && memory.Data == null, "只读商城查询擅自写入默认配置");
            var defaults = PublicShopCatalogRules.Current(world);
            check(defaults.Tabs.Select(tab => tab.Name).SequenceEqual(PublicShopRules.DefaultTabNames) &&
                defaults.Tabs[0].Products.Single().ItemIndex == potion.Index && defaults.Tabs.All(tab => tab.Products.All(p => p.ItemIndex != sword.Index)), "未配置商城改变旧默认货架");
            PublicShopCatalog Make() => new() { Tabs = [new PublicShopTab { Id = 7, Name = "精选", Products = [new() { ItemIndex = sword.Index, PriceRate = 1.23m }] },
                new PublicShopTab { Id = 2, Name = "药品", Products = [new() { ItemIndex = potion.Index }] }] };
            var configured = Make(); PublicShopCatalogRules.Validate(configured, world); world.PublicShopCatalog = configured;
            var player = new PlayerObject(); var offers = player.PublicShopOffers().ToArray();
            check(offers.Length == 2 && offers.Single(p => p.ItemIndex == sword.Index).Price == 123 && offers.Single(p => p.ItemIndex == sword.Index).Category == 7,
                "自定义商城没有复用当前物品编号、选项卡和金币单价");
            check(offers.Single(p => p.ItemIndex == potion.Index).Price == Math.Ceiling(20 * (double)Settings.PublicShopPriceRate), "默认价格未保留系统倍率");
            Settings.PublicShopPriceRate = 2;
            check(player.PublicShopOffers().Single(p => p.ItemIndex == potion.Index).Price == 40 && player.PublicShopOffers().Single(p => p.ItemIndex == sword.Index).Price == 123,
                "修改系统倍率未更新继承商品或覆盖了单独倍率");
            var inherited = configured.Tabs[1].Products[0]; inherited.PriceRate = 2;
            Settings.PublicShopPriceRate = 3;
            check(player.PublicShopOffers().Single(p => p.ItemIndex == potion.Index).Price == 40,"单独倍率等于旧系统值时丢失覆盖语义");
            inherited.PriceRate = null;
            check(player.PublicShopOffers().Single(p => p.ItemIndex == potion.Index).Price == 60,"清空倍率不能恢复继承");
            Settings.PublicShopPriceRate = systemRate;
            memory.Data = JsonSerializer.SerializeToUtf8Bytes(new { Schema = 2, Tabs = new[] { new { Id = 0, Name = "旧倍率", Products = new[] { new { ItemIndex = sword.Index, PriceRate = 1.5m } } } } });
            check(PublicShopCatalogRules.Load(world).Tabs[0].Products[0].PriceRate == 1.5m,"旧倍率配置未保留显式覆盖");
            memory.Data = JsonSerializer.SerializeToUtf8Bytes(new { Schema = 1, Tabs = new[] { new { Id = 0, Name = "旧默认", Products = new[] { new { ItemIndex = potion.Index, Price = 0 } } } } });
            check(PublicShopCatalogRules.Load(world).Tabs[0].Products[0].PriceRate == null,"旧默认价迁移仍烘焙为固定倍率");
            memory.Data = JsonSerializer.SerializeToUtf8Bytes(configured);
            check(JsonSerializer.Serialize(PublicShopCatalogRules.Load(world)) == JsonSerializer.Serialize(configured), "商城SQLite快照格式往返丢失配置");
            void Rejected(Action<PublicShopCatalog> change, string message)
            {
                var test = Make(); change(test); var failed = false;
                try { PublicShopCatalogRules.Validate(test, world); } catch (InvalidDataException) { failed = true; }
                check(failed, message);
            }
            Rejected(c => c.Schema = 99, "接受未知商城快照版本"); Rejected(c => c.Tabs = [], "接受零选项卡");
            Rejected(c => c.Tabs[0].Name = "\n商店", "接受控制字符选项卡"); Rejected(c => c.Tabs[0].Name = new string('长', 17), "选项卡名称无长度限制");
            Rejected(c => c.Tabs[1].Name = "精选", "接受重复选项卡名称"); Rejected(c => c.Tabs[1].Id = 7, "接受重复选项卡编号");
            Rejected(c => c.Tabs[1].Products[0].ItemIndex = sword.Index, "接受跨选项卡重复商品");
            Rejected(c => c.Tabs[0].Products[0].ItemIndex = int.MaxValue, "接受未知商品编号");
            foreach (var type in new[] { ItemType.Quest, ItemType.Script, ItemType.MonsterSpawn, ItemType.SealedHero, ItemType.Nothing, (ItemType)255 })
                check(!PublicShopCatalogRules.CanList(new ItemInfo { Type = type, StackSize = 1 }), "商城可配置非法物品类型：" + type);
            memory.Data = "{\"Schema\":99,\"Tabs\":[]}"u8.ToArray(); var bytes = memory.Data.ToArray(); var blocked = false;
            try { PublicShopCatalogRules.Load(world); } catch (InvalidDataException) { blocked = true; }
            check(blocked && memory.Data.SequenceEqual(bytes), "未知快照被清空或静默改为默认");
            var packet = new S.PublicShopTabs { Tabs = configured.Tabs };
            Packet.IsServer = false;
            var read = (S.PublicShopTabs)Packet.ReceivePacket(packet.GetPacketBytes().ToArray(), out var extra);
            check(read.Tabs.Select(tab => (tab.Id, tab.Name)).SequenceEqual(configured.Tabs.Select(tab => (tab.Id, tab.Name))) && extra.Length == 0, "选项卡追加协议往返失败");
            check(new S.PublicShop { Items = [new() { ItemIndex = 3 }] }.GetPacketBytes().Count() == 17, "改变既有商城商品包布局");
            BookLevelChecks(world, check);
            var state = new GameState(); state.Apply(read); check(state.PublicShopTabs?.Tabs[0].Name == "精选", "微端没有归约动态选项卡");
            state.Apply(new S.LogOutSuccess()); check(state.PublicShopTabs == null, "商城选项卡跨角色残留");
        }
        finally { Settings.PublicShopPriceRate = systemRate; world.ItemInfoList.Clear(); world.ItemInfoList.AddRange(items); world.PublicShopCatalog = catalog; DatabaseStorage.Current = storage; }
    }

    private static void BookLevelChecks(Envir world, Action<bool, string> check)
    {
        var magics = world.MagicInfoList.ToArray();
        try
        {
            world.MagicInfoList.Clear();
            world.MagicInfoList.Add(new Server.MirDatabase.MagicInfo { Spell = Spell.FireBall, Level1 = 34 });
            var book = new ItemInfo { Index = 98003, Name = "等级边界书", Type = ItemType.Book, Shape = (short)Spell.FireBall,
                RequiredType = RequiredType.Level, RequiredAmount = 34, Price = 100, StackSize = 1 };
            world.ItemInfoList.Add(book);
            check(PublicShopCatalogRules.SkillBookAllowed(book, world), "34级技能书被商城拒绝");
            var catalog = new PublicShopCatalog { Tabs = [new() { Id = 0, Name = "技能", Products = [new() { ItemIndex = book.Index }] }] };
            PublicShopCatalogRules.Validate(catalog, world);
            book.RequiredAmount = 35;
            check(!PublicShopCatalogRules.SkillBookAllowed(book, world), "35级整的书仍允许商城上架");
            book.RequiredAmount = 1; world.MagicInfoList[0].Level1 = 35;
            check(!PublicShopCatalogRules.SkillBookAllowed(book, world), "技能本身35级但书籍低等级仍绕过限制");
            var rejected = false;
            try { PublicShopCatalogRules.Validate(catalog, world); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "管理保存仍接受35级以上技能书");
            world.PublicShopCatalog = catalog;
            check(PublicShopCatalogRules.Current(world).Tabs[0].Products.Length == 0 && !new PlayerObject().PublicShopOffers().Any(offer => offer.ItemIndex == book.Index) &&
                catalog.Tabs[0].Products.Length == 1, "旧商城高等级书仍可展示/购买，或读取破坏保存的配置");
            PublicShopCatalogRules.Validate(catalog, world, loading: true);
            world.MagicInfoList[0].Level1 = 10; book.RequiredType = RequiredType.MaxMC; book.RequiredAmount = 100;
            check(PublicShopCatalogRules.SkillBookAllowed(book, world), "将属性门槛100误认为100级技能书");
        }
        finally { world.MagicInfoList.Clear(); world.MagicInfoList.AddRange(magics); }
    }
}
