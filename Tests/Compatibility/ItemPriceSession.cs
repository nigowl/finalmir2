using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crystal.Native;
using Mir2.Hosting;
using Mir2.Management;
using C = ClientPackets;
using S = ServerPackets;

internal static class ItemPriceSession
{
    public static async Task Run(string[] args)
    {
        var paths=new ProjectPaths();
        if(!paths.Data.Contains("/mir2-prices-",StringComparison.Ordinal))throw new InvalidOperationException("只允许隔离价格版本。");
        var at=Array.IndexOf(args,"--item-price-session");var port=int.Parse(args[at+1]);var record=args[at+2];
        Packet.IsServer=false;using var connection=new GameConnection();var state=new GameState();var packets=new List<Packet>();
        async Task Until(Func<bool> ready)
        {
            var timer=Stopwatch.StartNew();
            while(true)
            {
                while(connection.Received.TryDequeue(out var packet)){state.Apply(packet);packets.Add(packet);}
                if(ready())return;
                if(connection.Errors.TryDequeue(out var error))throw new IOException(error);
                if(timer.ElapsedMilliseconds>15000)throw new TimeoutException("价格实测超时："+string.Join('|',packets.OfType<S.Chat>().Select(p=>p.Message)));
                await Task.Delay(20);
            }
        }
        void Check(bool good,string message){if(!good)throw new Exception(message);}
        string Snapshot()=>JsonSerializer.Serialize(new { state.User!.Gold,Items=state.User.Inventory.Where(item=>item!=null).OrderBy(item=>item.UniqueID).Select(item=>new {item.UniqueID,item.ItemIndex,item.Count}) });
        await connection.Connect("127.0.0.1",port);await Until(()=>packets.OfType<S.Connected>().Any());
        await connection.Send(new C.ClientVersion{VersionHash=[]});await Until(()=>packets.OfType<S.ClientVersion>().Any());
        await connection.Send(new C.Login{AccountID="NativeGM",Password="Native123"});await Until(()=>state.Characters.Count>0);
        await connection.Send(new C.StartGame{CharacterIndex=state.Characters.Single().Index});await Until(()=>state.User!=null);
        await connection.Send(new C.ItemPricesRequest());
        if(args.Contains("--verify-save"))
        {
            Check(Snapshot()==File.ReadAllText(record),"价格交易资产重启后丢失");
            var held=state.User!.Inventory.Single(item=>item!=null);await Until(()=>held.Info.TradingPrices!=null);
            Check(held.Info.TradingPrices is { Buy:100,Sell:37,Recycle:19 },"价格重启后未下发");
        }
        else
        {
            await Until(()=>state.Actors.Values.Any(actor=>actor.Kind==ObjectType.Merchant));
            var npc=state.Actors.Values.First(actor=>actor.Kind==ObjectType.Merchant);
            async Task Page(string key)
            {
                packets.Clear();await connection.Send(new C.CallNPC{ObjectID=npc.Id,Key="["+key+"]"});
                await Until(()=>key=="@BUY"?packets.OfType<S.NPCGoods>().Any():key=="@SELL"?packets.OfType<S.NPCSell>().Any():packets.OfType<S.NPCResponse>().Any());
            }
            await Page("@MAIN");await Page("@BUY");
            var goods=packets.OfType<S.NPCGoods>().Last();foreach(var entry in goods.List)state.Bind(entry);var sample=goods.List.First(item=>item.Info.Type==ItemType.Potion);
            using var http=new HttpClient{BaseAddress=new Uri(Environment.GetEnvironmentVariable("MIR2_SERVER")!)};
            http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",File.ReadAllText(paths.AdminKeyFile).Trim());http.DefaultRequestHeaders.Add("X-Mir2-Version",paths.VersionId);
            var row=(await http.GetFromJsonAsync<ItemPriceRow[]>("/management/shop/prices"))!.Single(row=>row.Prices.ItemIndex==sample.ItemIndex);
            using(var response=await http.PutAsJsonAsync("/management/shop/prices",new ItemPriceEdit(new(){ItemIndex=sample.ItemIndex,Buy=100,Sell=37,Recycle=19},row.Version)))
                Check(response.IsSuccessStatusCode,"真实API保存价格失败："+await response.Content.ReadAsStringAsync());
            await Until(()=>sample.Info.TradingPrices?.Buy==100);
            var before=state.User!.Gold;packets.Clear();await connection.Send(new C.BuyItem{ItemIndex=sample.UniqueID,Count=3,Type=goods.Type});
            await Until(()=>packets.OfType<S.GainedItem>().Any());
            Check(state.User.Gold==before-(uint)(300*goods.Rate),"NPC购买未按新买价及原倍率结算");
            var item=state.User.Inventory.Single(item=>item?.ItemIndex==sample.ItemIndex);var identity=item.UniqueID;
            await Page("@MAIN");await Page("@SELL");before=state.User.Gold;packets.Clear();
            await connection.Send(new C.SellItem{UniqueID=identity,Count=1});await Until(()=>packets.OfType<S.SellItem>().Any());
            Check(packets.OfType<S.SellItem>().Last().Success&&state.User.Gold==before+37&&item.Count==2,"NPC出售未按独立卖价结算");
            before=state.User.Gold;packets.Clear();var request=new C.RecycleItem{RequestId=1,UniqueID=identity,Count=1};
            await connection.Send(request);await connection.Send(request);await Until(()=>packets.OfType<S.RecycleItemResult>().Count()==2);
            Check(state.User.Gold==before+19&&item.Count==1&&item.UniqueID==identity,"独立回收价、数量或重放保护错误");
            var doc=(await http.GetFromJsonAsync<PublicShopDocument>("/management/shop"))!;
            doc.Catalog.Tabs=[new(){Id=0,Name="价格验证",Products=[new(){ItemIndex=sample.ItemIndex,PriceRate=1.1m}]}];
            using(var response=await http.PutAsJsonAsync("/management/shop",new PublicShopEdit(doc.Catalog,doc.Version)))Check(response.IsSuccessStatusCode,"保存商品倍率失败");
            packets.Clear();await connection.Send(new C.PublicShopCatalogRequest());await Until(()=>packets.OfType<S.PublicShop>().Any());
            var offer=state.PublicShop!.Items.Single();Check(offer.Price==110,"商城未按基础买价乘1.1报价");
            before=state.User.Gold;packets.Clear();await connection.Send(new C.PublicShopBuy{RequestId=1,ItemIndex=sample.ItemIndex,UnitPrice=110,Count=1});
            await Until(()=>packets.OfType<S.PublicShopResult>().Any());Check(packets.OfType<S.PublicShopResult>().Last().Success&&state.User.Gold==before-110&&item.Count==2,"商城倍率实扣不符");
            var system=(await http.GetFromJsonAsync<ServerSettingsDocument>("/management/settings"))!;
            doc=(await http.GetFromJsonAsync<PublicShopDocument>("/management/shop"))!;
            doc.Catalog.Tabs[0].Products[0].PriceRate=null;
            using(var response=await http.PutAsJsonAsync("/management/shop",new PublicShopEdit(doc.Catalog,doc.Version)))Check(response.IsSuccessStatusCode,"恢复继承倍率失败");
            using(var response=await http.PutAsJsonAsync("/management/settings",new ServerSettingsEdit(system.Values with{PublicShopPriceRate=2},system.Version)))Check(response.IsSuccessStatusCode,"系统倍率热更新失败");
            before=state.User.Gold;packets.Clear();await connection.Send(new C.PublicShopBuy{RequestId=2,ItemIndex=sample.ItemIndex,UnitPrice=110,Count=1});
            await Until(()=>packets.OfType<S.PublicShopResult>().Any());Check(!packets.OfType<S.PublicShopResult>().Last().Success&&state.User.Gold==before&&item.Count==2,"系统变倍率后旧报价仍扣款");
            packets.Clear();await connection.Send(new C.PublicShopCatalogRequest());await Until(()=>packets.OfType<S.PublicShop>().Any());
            Check(state.PublicShop!.Items.Single().Price==200,"继承商品未立即跟随系统2倍");
            packets.Clear();await connection.Send(new C.PublicShopBuy{RequestId=3,ItemIndex=sample.ItemIndex,UnitPrice=200,Count=1});
            await Until(()=>packets.OfType<S.PublicShopResult>().Any());Check(packets.OfType<S.PublicShopResult>().Last().Success&&state.User.Gold==before-200&&item.Count==3,"继承倍率未按真实金币结算");
            var latest=(await http.GetFromJsonAsync<ServerSettingsDocument>("/management/settings"))!;
            using(var response=await http.PutAsJsonAsync("/management/settings",new ServerSettingsEdit(system.Values,latest.Version)))Check(response.IsSuccessStatusCode,"恢复系统倍率失败");
            doc=(await http.GetFromJsonAsync<PublicShopDocument>("/management/shop"))!;doc.Catalog.Tabs[0].Products[0].PriceRate=1.1m;
            using(var response=await http.PutAsJsonAsync("/management/shop",new PublicShopEdit(doc.Catalog,doc.Version)))Check(response.IsSuccessStatusCode,"恢复单独倍率失败");
            File.WriteAllText(record,Snapshot());
        }
        await connection.Send(new C.LogOut());await Until(()=>packets.OfType<S.LogOutSuccess>().Any());
        Console.WriteLine(args.Contains("--verify-save")?"PASS 价格配置与交易资产完整重启恢复。":"PASS 原TCP价格热更新、NPC买卖、独立回收价/重放、商城1.1倍率及真实金币/编号/数量。");
    }
}
