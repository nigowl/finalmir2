using System.Text.Json;
using Crystal.Native;
using Microsoft.Data.Sqlite;
using Mir2.Hosting;
using Mir2.Management;
using Server;
using Server.ConsoleHost;
using Server.Helpers;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class ItemPriceChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    public static void Run(Action<bool,string> check, string root)
    {
        var world = Envir.Main; var items = world.ItemInfoList.ToArray(); var catalog = world.PublicShopCatalog;
        var previousStorage = DatabaseStorage.Current; var directory = Directory.GetCurrentDirectory(); var isServer = Packet.IsServer;
        var paths = new ProjectPaths(data:Path.Combine(root,"item-prices")); Directory.CreateDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database);
        try
        {
            DatabaseStorage.Current = storage; Directory.SetCurrentDirectory(paths.World);
            world.ItemInfoList.Clear(); world.PublicShopCatalog = null;
            var info = new ItemInfo { Index = 99801, Name = "价格检查材料", Type = ItemType.CraftingMaterial, Price = 101, StackSize = 999 };
            world.ItemInfoList.Add(info);
            var item = new UserItem(info) { Count = 3, UniqueID = 13 };
            check(item.BuyPrice() == 303 && item.SellPrice() == 151 && item.RecyclePrice() == 151, "未配置价格改变旧奇数及整叠舍入");
            var definition = new S.NewItemInfo { Info = info }.GetPacketBytes().ToArray();
            var settings = new ItemPriceSettings(storage,new(paths,storage)); var row = settings.Read().Single();
            var saved = settings.Save(new(new() { ItemIndex = info.Index, Buy = 200, Sell = 37, Recycle = 19 },row.Version));
            check(item.BuyPrice() == 600 && item.SellPrice() == 111 && item.RecyclePrice() == 57 && info.Price == 101,
                "三种价格未独立生效或覆盖了原修理基值");
            check(definition.SequenceEqual(new S.NewItemInfo { Info = info }.GetPacketBytes()),"改变原物品定义协议布局");
            check(Directory.GetFiles(Path.Combine(paths.Runtime,"backups"),"mir2.sqlite3",SearchOption.AllDirectories).Length > 0,"保存价格没有完整备份");
            try { settings.Save(new(saved.Prices with { Buy = 999 },row.Version)); throw new Exception("接受过期价格编辑"); }
            catch(InvalidOperationException) { check(item.BuyPrice() == 600,"版本冲突改变价格"); }
            Protocol(check,info,item);
            var player = new Player { Account = new AccountInfo { Gold = 100 }, Info = new CharacterInfo { HP = 100 } };
            player.Info.Inventory[0] = item;
            var request = new ClientPackets.RecycleItem { RequestId = 1, UniqueID = 13, Count = 2 };
            player.RecycleItem(request); player.RecycleItem(request);
            check(player.Account.Gold == 138 && item.Count == 1 && player.Packets.OfType<S.DeleteItem>().Count() == 1,"回收未使用独立价格或重放重复加金币");
            info.TradingPrices = null; ItemPriceRules.Load(world);
            check(item.RecyclePrice() == 19 && item.SellPrice() == 37 && item.BuyPrice() == 200,"价格SQLite恢复失败");
            using(var db = new SqliteConnection($"Data Source={paths.Database}"))
            {
                db.Open(); using var trigger = db.CreateCommand();
                trigger.CommandText="CREATE TRIGGER reject_prices BEFORE UPDATE ON snapshots WHEN NEW.name='settings/item-prices.json' BEGIN SELECT RAISE(ABORT,'price failure'); END";trigger.ExecuteNonQuery();
                var before = settings.Read().Single();
                try { settings.Save(new(before.Prices with { Sell = 99 },before.Version)); throw new Exception("价格失败未上抛"); }
                catch(SqliteException) { check(item.SellPrice() == 37,"失败事务未回滚内存价格"); }
                info.TradingPrices = null; ItemPriceRules.Load(world); check(item.SellPrice() == 37,"失败事务改变已保存价格");
                trigger.CommandText="DROP TRIGGER reject_prices";trigger.ExecuteNonQuery();
            }
            Migration(check,world,storage,info);
            Durability(check);
            var bytes = "{\"Schema\":99,\"Items\":[]}"u8.ToArray();storage.Write(ItemPriceRules.Snapshot,bytes);
            try { ItemPriceRules.Load(world); throw new Exception("接受未知价格格式"); }
            catch(InvalidDataException) { using var stream=storage.Read(ItemPriceRules.Snapshot);check(stream.Length==bytes.Length,"未知价格格式被覆盖"); }
        }
        finally
        {
            world.ItemInfoList.Clear();world.ItemInfoList.AddRange(items);world.PublicShopCatalog=catalog;
            DatabaseStorage.Current=previousStorage;Directory.SetCurrentDirectory(directory);Packet.IsServer=isServer;
        }
    }

    private static void Protocol(Action<bool,string> check,ItemInfo info,UserItem item)
    {
        Packet.IsServer=false;
        var packet = new S.ItemPriceChanged { Prices = info.TradingPrices!, Configured = true };
        var received=(S.ItemPriceChanged)Packet.ReceivePacket(packet.GetPacketBytes().ToArray(),out var extra);
        check(received.Prices==packet.Prices && received.Configured && extra.Length==0,"价格追加包往返失败");
        var state=new GameState();state.Apply(new S.NewItemInfo { Info=new ItemInfo { Index=info.Index,Price=101 } });state.Apply(received);
        var remote=new UserItem(state.Items[info.Index]){Count=3};
        check(remote.BuyPrice()==600&&remote.SellPrice()==111&&remote.RecyclePrice()==57,"客户端价格不同步");
        state.Apply(new S.NewItemInfo { Info=new ItemInfo { Index=info.Index,Price=101 } });
        check(state.Items[info.Index].TradingPrices==packet.Prices,"原定义重发丢失独立价格");
        state.Reset();state.Apply(received);state.Apply(new S.NewItemInfo { Info=new ItemInfo { Index=info.Index,Price=101 } });
        check(state.Items[info.Index].TradingPrices==packet.Prices,"先到价格包未正确绑定后到定义");
        state.Apply(new S.ItemPriceChanged{Prices=packet.Prices,Configured=false});
        check(state.Items[info.Index].TradingPrices==null,"未配置价格不能恢复原规则");
    }

    private static void Migration(Action<bool,string> check,Envir world,SqliteDatabaseStorage storage,ItemInfo info)
    {
        storage.Write(PublicShopCatalogRules.Snapshot,JsonSerializer.SerializeToUtf8Bytes(new { Schema=1,Tabs=new[]{new {Id=0,Name="旧货架",Products=new[]{new {ItemIndex=info.Index,Price=123u}}}}}));
        var migrated=PublicShopCatalogRules.Load(world);world.PublicShopCatalog=migrated;
        check(migrated.Schema==3&&new PlayerObject().PublicShopOffers().Single().Price==123,"旧绝对价格未转换为等价倍率");
        var service=new ItemPriceSettings(storage,new(new ProjectPaths(data:Directory.GetParent(Directory.GetCurrentDirectory())!.FullName),storage));
        var row=service.Read().Single();service.Save(new(row.Prices with { Buy=400 },row.Version));
        info.TradingPrices=null;ItemPriceRules.Load(world);world.PublicShopCatalog=PublicShopCatalogRules.Load(world);
        check(new PlayerObject().PublicShopOffers().Single().Price==246,"改买价后重启重新套用旧绝对价");
        var original=world.PublicShopCatalog.Tabs[0].Products[0].PriceRate;
        foreach(var rate in new[]{0m,-1m,(decimal)uint.MaxValue,(decimal)uint.MaxValue+1})
        {
            world.PublicShopCatalog.Tabs[0].Products[0].PriceRate=rate;
            try{PublicShopCatalogRules.Validate(world.PublicShopCatalog,world);throw new Exception("接受非法商品倍率");}
            catch(InvalidDataException){check(true,"非法倍率拒绝");}
        }
        world.PublicShopCatalog.Tabs[0].Products[0].PriceRate=original;
    }

    private static void Durability(Action<bool,string> check)
    {
        var info=new ItemInfo{Index=99802,Type=ItemType.Weapon,Price=1000,Durability=10000,TradingPrices=new(){Buy=2000,Sell=400,Recycle=200}};
        var item=new UserItem(info){CurrentDura=5000,MaxDura=10000};var repair=item.RepairPrice();
        check(item.BuyPrice()==1750&&item.SellPrice()==350&&item.RecyclePrice()==175,"独立价格丢失装备耐久修正");
        info.TradingPrices=null;check(item.RepairPrice()==repair,"交易价格污染修理费用");
        info.TradingPrices=new(){Buy=uint.MaxValue,Sell=0,Recycle=0};item.Count=999;
        check(item.BuyPrice()==uint.MaxValue&&item.SellPrice()==0&&ItemRecycling.Rejection(item).Length>0,"价格溢出或0回收价值检查失败");
    }
}
