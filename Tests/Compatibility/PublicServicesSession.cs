using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crystal.Native;
using Mir2.Hosting;
using Mir2.Management;
using C = ClientPackets;
using S = ServerPackets;

internal static class PublicServicesSession
{
    public static async Task Run(string[] args)
    {
        var at = Array.IndexOf(args, "--public-services-session"); var port = int.Parse(args[at + 1]); var record = args[at + 2];
        var paths = new ProjectPaths();
        if (!paths.Data.Contains("mir2-native.", StringComparison.Ordinal)) throw new InvalidOperationException("仅允许隔离公共服务fixture。");
        Packet.IsServer = false; using var connection = new GameConnection(); var state = new GameState(); var packets = new List<Packet>();
        async Task Until(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                while (connection.Received.TryDequeue(out var packet)) { state.Apply(packet); packets.Add(packet); }
                if (condition()) return;
                if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                if (timer.ElapsedMilliseconds > 15000) throw new TimeoutException("公共服务TCP回执超时：" + string.Join('/', packets.TakeLast(5).Select(packet => packet.GetType().Name)));
                await Task.Delay(20);
            } while (true);
        }
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string Snapshot() => JsonSerializer.Serialize(new { state.User!.Gold, state.User.Experience,
            Items = state.User.Inventory.Where(item => item != null).OrderBy(item => item.UniqueID).Select(item => new { item.UniqueID, item.ItemIndex, item.Count, Remaining = MagicBloodStone.Remaining(item) }) });
        await connection.Connect("127.0.0.1", port); await Until(() => packets.OfType<S.Connected>().Any());
        await connection.Send(new C.ClientVersion { VersionHash = [] }); await Until(() => packets.OfType<S.ClientVersion>().Any());
        await connection.Send(new C.Login { AccountID = "NativeGM", Password = "Native123" }); await Until(() => packets.OfType<S.LoginSuccess>().Any());
        await connection.Send(new C.StartGame { CharacterIndex = state.Characters.Single(role => role.Name == "NativeGM").Index });
        await Until(() => state.User != null && state.Map != null && state.Quests.Count >= 2);
        if (args.Contains("--verify-save")) Check(Snapshot() == File.ReadAllText(record), "公共采购的金币、经验、物品编号/数量未保存恢复。");
        else
        {
            using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("MIR2_SERVER")!) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(paths.AdminKeyFile).Trim());
            http.DefaultRequestHeaders.Add("X-Mir2-Version", paths.VersionId);
            using (var prepared = await http.PostAsync("/management/development/public-shop/prepare", null))
                Check(prepared.IsSuccessStatusCode, "补给规格保存失败：" + await prepared.Content.ReadAsStringAsync());
            async Task Rate(float rate)
            {
                var settings = (await http.GetFromJsonAsync<ServerSettingsDocument>("/management/settings"))!;
                using var response = await http.PutAsJsonAsync("/management/settings", new ServerSettingsEdit(settings.Values with { PublicShopPriceRate = rate }, settings.Version));
                Check(response.IsSuccessStatusCode, "公共商店倍率保存失败：" + await response.Content.ReadAsStringAsync());
            }
            await connection.Send(new C.PublicShopRequest()); await Until(() => state.PublicShop != null);
            var offer = state.PublicShop!.Items.First(item => item.Category == 0);
            Check(offer.Price == 3 && state.PublicShop.Items.Select(item => item.Category).Distinct().Count() == 5, "默认50%加价或五类补给/技能目录错误。");
            Check(state.PublicShop.Items.All(item => PublicShopRules.Category(state.Items[item.ItemIndex]) >= 0), "公共商店混入特殊道具。");
            Check(state.PublicShop.Items.Count(item => item.Category == 2) == 9 && state.PublicShop.Items.Where(item => item.Category == 2)
                .GroupBy(item => state.Items[item.ItemIndex].Shape).All(group => group.Select(item => state.Items[item.ItemIndex].StackSize).Order().SequenceEqual(new ushort[] { 500, 1000, 2000 })),
                "符毒页未保留各三档500/1000/2000次或包含旧商品");
            var inventory = state.User!.Inventory;
            Check(inventory.Length == Globals.BaseInventorySize && inventory[45] is { UniqueID: > 0, Info.Name: "NativeEquipFixture" } && inventory[85] == null,
                "46格旧包未保留末格物品，或登录后没有实际扩到80格背包加6格快捷栏。");
            async Task Command(string command)
            {
                packets.Clear(); await connection.Send(new C.Chat { Message = command });
                await Until(() => packets.OfType<S.Chat>().Any(reply => reply.Message.Contains("命令已处理")));
            }
            var scroll = state.PublicShop.Items.First(item => state.Items[item.ItemIndex] is { Type: ItemType.Scroll, Shape: 1, StackSize: > 1 });
            var id = 0;
            async Task<S.PublicShopResult> Purchase(PublicShopEntry entry, ushort count)
            {
                packets.Clear(); await connection.Send(new C.PublicShopBuy { RequestId = ++id, ItemIndex = entry.ItemIndex, Count = count, UnitPrice = entry.Price });
                await Until(() => packets.OfType<S.PublicShopResult>().Any(result => result.RequestId == id));
                return packets.OfType<S.PublicShopResult>().Single();
            }
            await Command("@mk NativeEquipFixture 79");
            Check(inventory.All(item => item != null), "80格没有实际填满。");
            var full = Snapshot();
            var rejection = await Purchase(scroll, 1);
            Check(!rejection.Success && rejection.Message.Contains("空间") && full == Snapshot(), "满包买不可合堆的回城卷没有安全拒绝。");
            var discarded = inventory[79];
            packets.Clear(); await connection.Send(new C.DropItem { UniqueID = discarded.UniqueID, Count = 1 });
            await Until(() => packets.OfType<S.DropItem>().Any(packet => packet.UniqueID == discarded.UniqueID && packet.Success));
            var gold = state.User.Gold;
            Check(inventory[79] == null && (await Purchase(scroll, 1)).Success && inventory[79]?.ItemIndex == scroll.ItemIndex,
                "空出第80格后仍无法放入回城卷。");
            var scrollId = inventory[79].UniqueID;
            Check((await Purchase(scroll, 1)).Success && inventory[79].Count == 2 && inventory[79].UniqueID == scrollId && state.User.Gold == gold - 2 * scroll.Price,
                "满包时兼容回城卷未正确合堆或金币不守恒。");
            await Command("@cb");
            var request = new C.PublicShopBuy { RequestId = 1, ItemIndex = offer.ItemIndex, UnitPrice = offer.Price, Count = 2 };
            request.RequestId = ++id;
            gold = state.User.Gold;
            await connection.Send(request); await Until(() => packets.OfType<S.PublicShopResult>().Any(result => result.RequestId == id));
            Check(packets.OfType<S.PublicShopResult>().Single().Success && state.User.Gold == gold - 6 && packets.OfType<S.GainedItem>().Count() == 1, "无NPC会话公共购买未即时入包或扣款错误。");
            var bought = Snapshot(); packets.Clear(); await connection.Send(request); await Until(() => packets.OfType<S.PublicShopResult>().Any());
            Check(Snapshot() == bought && !packets.OfType<S.GainedItem>().Any() && !packets.OfType<S.LoseGold>().Any(), "重复购买请求再次成交。");
            async Task Buy(PublicShopEntry entry, ushort count)
            {
                var result = await Purchase(entry, count);
                Check(result.Success, "批量采购失败：" + result.Message);
            }
            var potionCount = state.User.Inventory.Where(item => item?.ItemIndex == offer.ItemIndex).Sum(item => item.Count);
            gold = state.User.Gold; await Buy(offer, 100);
            Check(state.User.Gold == gold - offer.Price * 100 && state.User.Inventory.Where(item => item?.ItemIndex == offer.ItemIndex).Sum(item => item.Count) == potionCount + 100 &&
                state.User.Inventory.All(item => item == null || item.Count <= item.Info.StackSize), "x100跨堆叠购买没有守恒");
            var amulet = state.PublicShop.Items.First(entry => state.Items[entry.ItemIndex].Name == "护身符（小）");
            await Buy(amulet, 10);
            Check(state.User.Inventory.Where(item => item?.ItemIndex == amulet.ItemIndex).Sum(item => item.Count) == 5000, "毒符10包没有得到5000次");
            var stoneOffer = state.PublicShop.Items.Single(entry => state.Items[entry.ItemIndex].Name == "魔血石（小）");
            await Buy(stoneOffer, 1);
            var stone = state.User.Inventory.Single(item => item?.ItemIndex == stoneOffer.ItemIndex);
            var hp = state.User.HP; var mp = state.User.MP; packets.Clear();
            await connection.Send(new C.EquipItem { Grid = MirGridType.Inventory, UniqueID = stone.UniqueID, To = (int)EquipmentSlot.Torch });
            await Until(() => packets.OfType<S.RefreshItem>().Any(packet => packet.Item.UniqueID == stone.UniqueID && packet.Item.AddedStats[Stat.MagicBlood] < 0));
            Check(state.User.HP > hp && state.User.MP > mp && MagicBloodStone.Remaining(state.User.Equipment[(int)EquipmentSlot.Torch]) < 100000 &&
                packets.OfType<S.HealthChanged>().Any(), "魔血石未通过原装备/回血回执生效");
            packets.Clear();
            await connection.Send(new C.RemoveItem { Grid = MirGridType.Inventory, UniqueID = stone.UniqueID, To = Array.IndexOf(state.User.Inventory, null) });
            await Until(() => packets.OfType<S.RemoveItem>().Any(packet => packet.UniqueID == stone.UniqueID && packet.Success));
            async Task Reject(int item, ushort count, uint price, string reason)
            {
                packets.Clear(); var original = Snapshot();
                await connection.Send(new C.PublicShopBuy { RequestId = ++id, ItemIndex = item, Count = count, UnitPrice = price });
                await Until(() => packets.OfType<S.PublicShopResult>().Any(result => result.RequestId == id));
                var result = packets.OfType<S.PublicShopResult>().Single();
                Check(!result.Success && result.Message.Contains(reason) && Snapshot() == original && !packets.OfType<S.LoseGold>().Any(), "非法采购修改资产或原因错误：" + result.Message);
            }
            await Reject(offer.ItemIndex, 0, offer.Price, "数量"); await Reject(int.MaxValue, 1, 1, "商品");
            await Reject(offer.ItemIndex, 1, 0, "价格");
            var torch = state.PublicShop.Items.First(entry => state.Items[entry.ItemIndex].Type == ItemType.Torch && !MagicBloodStone.IsStone(state.Items[entry.ItemIndex]));
            await Reject(torch.ItemIndex, 100, torch.Price, "空间");
            await Reject(stoneOffer.ItemIndex, 100, stoneOffer.Price, "金币");
            var horn = state.PublicShop.Items.Single(entry => PublicShopRules.IsGuildHorn(state.Items[entry.ItemIndex]));
            Check(horn.Category == 3 && horn.Price == 1_000_000, "沃玛角没有在补给页按100万报价。");
            await Rate(2); await Reject(offer.ItemIndex, 1, offer.Price, "价格");
            await Reject(horn.ItemIndex, 1, 1_500_000, "价格");
            gold = state.User.Gold; await Buy(horn, 1);
            Check(state.User.Gold == gold - 1_000_000 &&
                state.User.Inventory.Single(item => item?.ItemIndex == horn.ItemIndex) is { Count: 1, UniqueID: > 0 },
                "沃玛角受商城倍率影响或实际扣款/入包错误。");
            await Rate(1.5f);
            packets.Clear(); await connection.Send(new C.QuestNavigationRequest { QuestIndex = 2, RequestId = 77 });
            await Until(() => state.QuestNavigation.TryGetValue(2, out var navigation) && navigation.RequestId == 77);
            var target = state.QuestNavigation[2].Targets.Single(item => item.Id == 1);
            Check(target.MapIndex == 2 && target.Reachable && target.NextLocation != target.Location && target.Name == "NativeDanger", "跨图任务未使用真实目标/正常出口。");
            File.WriteAllText(record, Snapshot());
        }
        packets.Clear(); await connection.Send(new C.LogOut()); await Until(() => packets.OfType<S.LogOutSuccess>().Any());
        Console.WriteLine(args.Contains("--verify-save") ? "PASS public services original TCP restart: gold, XP, item IDs/counts preserved." :
            "PASS public services original TCP: 46-to-80 slots, full-bag rejection/stack merge, guild horn fixed price, x100 cross-stack purchase, x10 amulet packs, magic blood stone, capacity/gold/price/replay rejection and quest waypoint.");
    }
}
