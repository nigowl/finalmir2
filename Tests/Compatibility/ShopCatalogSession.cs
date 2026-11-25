using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crystal.Native;
using Mir2.Hosting;
using Mir2.Management;
using C = ClientPackets;
using S = ServerPackets;

internal static class ShopCatalogSession
{
    public static async Task Run(string[] args)
    {
        var paths = new ProjectPaths();
        if (!paths.Data.Contains("/mir2-heroes.", StringComparison.Ordinal)) throw new InvalidOperationException("商城配置实测仅允许隔离版本。");
        var at = Array.IndexOf(args, "--shop-catalog-session"); var port = int.Parse(args[at + 1]); var record = args[at + 2];
        Packet.IsServer = false; using var connection = new GameConnection(); var state = new GameState(); var packets = new List<Packet>();
        async Task Until(Func<bool> ready)
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                while (connection.Received.TryDequeue(out var packet)) { state.Apply(packet); packets.Add(packet); }
                if (ready()) return;
                if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                if (timer.ElapsedMilliseconds > 15000) throw new TimeoutException("商城配置回执超时。");
                await Task.Delay(20);
            }
        }
        void Check(bool good, string message) { if (!good) throw new Exception(message); }
        string Snapshot() => JsonSerializer.Serialize(new { state.User!.Gold, Items = state.User.Inventory.Where(item => item != null)
            .OrderBy(item => item.UniqueID).Select(item => new { item.UniqueID, item.ItemIndex, item.Count }) });
        await connection.Connect("127.0.0.1", port); await Until(() => packets.OfType<S.Connected>().Any());
        await connection.Send(new C.ClientVersion { VersionHash = [] }); await Until(() => packets.OfType<S.ClientVersion>().Any());
        await connection.Send(new C.Login { AccountID = "NativeGM", Password = "Native123" }); await Until(() => packets.OfType<S.LoginSuccess>().Any());
        await connection.Send(new C.StartGame { CharacterIndex = state.Characters.Single().Index }); await Until(() => state.User != null);
        if (args.Contains("--verify-save")) Check(Snapshot() == File.ReadAllText(record), "自定义商城购买金币与物品编号/数量重启后改变。");
        else
        {
            await connection.Send(new C.PublicShopRequest()); await Until(() => state.PublicShop != null);
            Check(!packets.OfType<S.PublicShopTabs>().Any(), "向旧商城请求发送了追加布局包。");
            using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("MIR2_SERVER")!) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(paths.AdminKeyFile).Trim());
            http.DefaultRequestHeaders.Add("X-Mir2-Version", paths.VersionId);
            async Task Price(uint price)
            {
                var document = (await http.GetFromJsonAsync<PublicShopDocument>("/management/shop"))!;
                document.Catalog.Tabs.Single(tab => tab.Id == 7).Products.Single().Price = price;
                using var response = await http.PutAsJsonAsync("/management/shop", new PublicShopEdit(document.Catalog, document.Version));
                Check(response.IsSuccessStatusCode, "商城报价变更失败：" + await response.Content.ReadAsStringAsync());
            }
            var offer = state.PublicShop!.Items.Single(offer => offer.Category == 7); var gold = state.User!.Gold;
            var before = Snapshot(); await Price(0);
            var definitionPath = "/management/development/definition/items/" + offer.ItemIndex;
            var definition = (await http.GetFromJsonAsync<DefinitionEdit>(definitionPath))!;
            using (var refused = await http.PutAsJsonAsync("/management/development/definition", definition with { Price = 0 }))
                Check(refused.StatusCode == System.Net.HttpStatusCode.BadRequest, "允许将商城默认商品定义改为0价，导致重启无法加载。");
            Check(await http.GetFromJsonAsync<DefinitionEdit>(definitionPath) == definition && Snapshot() == before, "拒绝无效默认价时改变定义或玩家资产。");
            await Price(38);
            await connection.Send(new C.PublicShopBuy { RequestId = 1, ItemIndex = offer.ItemIndex, Count = 1, UnitPrice = 37 });
            await Until(() => packets.OfType<S.PublicShopResult>().Any(p => p.RequestId == 1));
            Check(!packets.OfType<S.PublicShopResult>().Last().Success && Snapshot() == before, "后台改价后旧报价仍成交或改变资产。");
            packets.Clear(); await connection.Send(new C.PublicShopCatalogRequest()); await Until(() => packets.OfType<S.PublicShop>().Any());
            Check(state.PublicShopTabs?.Tabs.Select(tab => tab.Name).SequenceEqual(["常用药品", "职业补给"]) == true, "新商城请求没有收到正确动态布局。");
            await connection.Send(new C.PublicShopBuy { RequestId = 2, ItemIndex = offer.ItemIndex, Count = 1, UnitPrice = 39 });
            await Until(() => packets.OfType<S.PublicShopResult>().Any(p => p.RequestId == 2));
            Check(!packets.OfType<S.PublicShopResult>().Last().Success && Snapshot() == before, "篡改单价仍成交或改变资产。");
            await Price(37); packets.Clear(); await connection.Send(new C.PublicShopCatalogRequest()); await Until(() => packets.OfType<S.PublicShop>().Any());
            var count = state.User.Inventory.Where(item => item?.ItemIndex == offer.ItemIndex).Sum(item => item.Count);
            var buy = new C.PublicShopBuy { RequestId = 3, ItemIndex = offer.ItemIndex, Count = 2, UnitPrice = 37 };
            packets.Clear(); await connection.Send(buy); await connection.Send(buy);
            await Until(() => packets.OfType<S.PublicShopResult>().Count(p => p.RequestId == 3) == 2);
            Check(packets.OfType<S.PublicShopResult>().All(p => p.Success) && packets.OfType<S.LoseGold>().Count() == 1 && state.User.Gold == gold - 74 &&
                state.User.Inventory.Where(item => item?.ItemIndex == offer.ItemIndex).Sum(item => item.Count) == count + 2, "重复商城请求没有只扣74金币并增加2份物品。");
            File.WriteAllText(record, Snapshot());
        }
        await connection.Send(new C.LogOut()); await Until(() => packets.OfType<S.LogOutSuccess>().Any());
        Console.WriteLine(args.Contains("--verify-save") ? "PASS configured shop assets after full restart." : "PASS old/new shop protocol, stale/tampered prices, idempotent original purchase and exact assets.");
    }
}
