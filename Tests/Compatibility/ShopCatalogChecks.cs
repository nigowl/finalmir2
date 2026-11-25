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
        var world = Envir.Main; var items = world.ItemInfoList.ToArray(); var catalog = world.PublicShopCatalog; var storage = DatabaseStorage.Current;
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
            PublicShopCatalog Make() => new() { Tabs = [new PublicShopTab { Id = 7, Name = "精选", Products = [new() { ItemIndex = sword.Index, Price = 123 }] },
                new PublicShopTab { Id = 2, Name = "药品", Products = [new() { ItemIndex = potion.Index }] }] };
            var configured = Make(); PublicShopCatalogRules.Validate(configured, world); world.PublicShopCatalog = configured;
            var player = new PlayerObject(); var offers = player.PublicShopOffers().ToArray();
            check(offers.Length == 2 && offers.Single(p => p.ItemIndex == sword.Index).Price == 123 && offers.Single(p => p.ItemIndex == sword.Index).Category == 7,
                "自定义商城没有复用当前物品编号、选项卡和金币单价");
            check(offers.Single(p => p.ItemIndex == potion.Index).Price == Math.Ceiling(20 * (double)Settings.PublicShopPriceRate), "默认价格未保留系统倍率");
            memory.Data = JsonSerializer.SerializeToUtf8Bytes(configured);
            check(JsonSerializer.Serialize(PublicShopCatalogRules.Load(world)) == JsonSerializer.Serialize(configured), "商城SQLite快照格式往返丢失配置");
            void Rejected(Action<PublicShopCatalog> change, string message)
            {
                var test = Make(); change(test); var failed = false;
                try { PublicShopCatalogRules.Validate(test, world); } catch (InvalidDataException) { failed = true; }
                check(failed, message);
            }
            Rejected(c => c.Schema = 2, "接受未知商城快照版本"); Rejected(c => c.Tabs = [], "接受零选项卡");
            Rejected(c => c.Tabs[0].Name = "\n商店", "接受控制字符选项卡"); Rejected(c => c.Tabs[0].Name = new string('长', 17), "选项卡名称无长度限制");
            Rejected(c => c.Tabs[1].Name = "精选", "接受重复选项卡名称"); Rejected(c => c.Tabs[1].Id = 7, "接受重复选项卡编号");
            Rejected(c => c.Tabs[1].Products[0].ItemIndex = sword.Index, "接受跨选项卡重复商品");
            Rejected(c => c.Tabs[0].Products[0].ItemIndex = int.MaxValue, "接受未知商品编号");
            foreach (var type in new[] { ItemType.Quest, ItemType.Script, ItemType.MonsterSpawn, ItemType.SealedHero, ItemType.Nothing, (ItemType)255 })
                check(!PublicShopCatalogRules.CanList(new ItemInfo { Type = type, StackSize = 1 }), "商城可配置非法物品类型：" + type);
            memory.Data = "{\"Schema\":2,\"Tabs\":[]}"u8.ToArray(); var bytes = memory.Data.ToArray(); var blocked = false;
            try { PublicShopCatalogRules.Load(world); } catch (InvalidDataException) { blocked = true; }
            check(blocked && memory.Data.SequenceEqual(bytes), "未知快照被清空或静默改为默认");
            var packet = new S.PublicShopTabs { Tabs = configured.Tabs };
            Packet.IsServer = false;
            var read = (S.PublicShopTabs)Packet.ReceivePacket(packet.GetPacketBytes().ToArray(), out var extra);
            check(read.Tabs.Select(tab => (tab.Id, tab.Name)).SequenceEqual(configured.Tabs.Select(tab => (tab.Id, tab.Name))) && extra.Length == 0, "选项卡追加协议往返失败");
            check(new S.PublicShop { Items = [new() { ItemIndex = 3 }] }.GetPacketBytes().Count() == 17, "改变既有商城商品包布局");
            var state = new GameState(); state.Apply(read); check(state.PublicShopTabs?.Tabs[0].Name == "精选", "微端没有归约动态选项卡");
            state.Apply(new S.LogOutSuccess()); check(state.PublicShopTabs == null, "商城选项卡跨角色残留");
        }
        finally { world.ItemInfoList.Clear(); world.ItemInfoList.AddRange(items); world.PublicShopCatalog = catalog; DatabaseStorage.Current = storage; }
    }
}
