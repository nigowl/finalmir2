using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crystal.Native;
using Mir2.Hosting;
using Mir2.Management;
using Server.Helpers;
using C = ClientPackets;
using S = ServerPackets;

internal static class ProgressionFeatureSession
{
    internal static async Task Run(string[] args)
    {
        var paths = new ProjectPaths();
        if (!paths.Data.Contains("/mir2-balance.", StringComparison.Ordinal)) throw new InvalidOperationException("只允许隔离平衡验证版本。");
        var at = Array.IndexOf(args, "--progression-feature-session"); var port = int.Parse(args[at + 1]); var record = args[at + 2];
        var verify = args.Contains("--verify-save");
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.Runtime, "progression-feature-fixture.json")));
        int Id(string key) => fixture.RootElement.GetProperty(key).GetInt32();
        using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("MIR2_SERVER")!) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(paths.AdminKeyFile).Trim());
        http.DefaultRequestHeaders.Add("X-Mir2-Version", paths.VersionId);
        async Task AutoLearn(bool enabled)
        {
            var doc = (await http.GetFromJsonAsync<ServerSettingsDocument>("/management/settings"))!;
            var values = doc.Values with { AutoLearnSkills = enabled, AutoLearnWarriorSkills = [ProgressionFeatureFixture.Selected[0]],
                AutoLearnWizardSkills = [ProgressionFeatureFixture.Selected[1]], AutoLearnTaoistSkills = [ProgressionFeatureFixture.Selected[2]],
                AutoLearnAssassinSkills = [ProgressionFeatureFixture.Selected[3]], AutoLearnArcherSkills = [ProgressionFeatureFixture.Selected[4]] };
            using var response = await http.PutAsJsonAsync("/management/settings", new ServerSettingsEdit(values, doc.Version));
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        }
        var snapshots = verify ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(record))! : new();
        Packet.IsServer = false;
        foreach (var job in Enum.GetValues<MirClass>())
        {
            using var connection = new GameConnection(); var state = new GameState(); var packets = new List<Packet>();
            async Task Until(Func<bool> ready)
            {
                var timer = Stopwatch.StartNew();
                while (true)
                {
                    while (connection.Received.TryDequeue(out var packet)) { state.Apply(packet); packets.Add(packet); }
                    if (ready()) return;
                    if (connection.Errors.TryDequeue(out var error)) throw new IOException(error);
                    if (timer.Elapsed.TotalSeconds > 25) throw new TimeoutException(job + " " + string.Join(" / ", packets.OfType<S.Chat>().TakeLast(6).Select(p => p.Message)));
                    await Task.Delay(20);
                }
            }
            string Snapshot()
            {
                var materials = state.User!.QuestInventory.Select(item => item?.Clone()).ToArray();
                // 登录会将消费后留下的兼容堆重新补满，仍逐项核对编号、数量和纯度。
                InventoryStacks.Compact(materials);
                return JsonSerializer.Serialize(new { state.User!.Level, state.User.Gold,
                Bag = state.User.Inventory.Select(item => item == null ? "-" : $"{item.UniqueID}/{item.ItemIndex}/{item.Count}/{item.CurrentDura}"),
                Materials = materials.Select(item => item == null ? "-" : $"{item.UniqueID}/{item.ItemIndex}/{item.Count}/{item.CurrentDura}"),
                HeroLevel = state.Hero!.Level, HeroSkills = state.Hero.Magics.Select(m => new { m.Spell, m.Level, m.Key, m.Experience }),
                Skills = state.User.Magics.Select(m => new { m.Spell, m.Level, m.Key, m.Experience }), Done = state.CompletedQuests.Order() });
            }
            await connection.Connect("127.0.0.1", port); await Until(() => packets.OfType<S.Connected>().Any());
            await connection.Send(new C.ClientVersion { VersionHash = [] }); await Until(() => packets.OfType<S.ClientVersion>().Any());
            await connection.Send(new C.Login { AccountID = ProgressionFeatureFixture.Account(job), Password = "Native123" }); await Until(() => packets.OfType<S.LoginSuccess>().Any());
            await connection.Send(new C.StartGame { CharacterIndex = state.Characters.Single().Index });
            await Until(() => state.User != null && state.Hero != null && state.Quests.Count == 0);
            var barrier = Environment.TickCount64;
            await connection.Send(new C.KeepAlive { Time = barrier }); await Until(() => packets.OfType<S.KeepAlive>().Any(p => p.Time == barrier));
            if (verify)
            {
                if (Snapshot() != snapshots[job.ToString()])
                { File.WriteAllText(record + ".actual-" + job + ".json", Snapshot()); throw new Exception(job + "完整重启后材料/金币/任务/英雄技能发生变化。"); }
            }
            else
            {
                var spell = ProgressionFeatureFixture.Selected[(int)job];
                if (state.User!.QuestInventory.Any(item => item?.Info.Type == ItemType.Torch) || !state.User.Inventory.Any(item => item?.Info.Type == ItemType.Torch))
                    throw new Exception("配方引用的普通蜡烛未从旧材料栏迁回普通包。");
                if (state.User!.Inventory.Any(item => item?.Info.Type is ItemType.CraftingMaterial or ItemType.Ore or ItemType.Meat) ||
                    state.User.QuestInventory.Where(item => item?.ItemIndex == Id("Material")).Sum(item => item.Count) != 1200 ||
                    state.User.QuestInventory.Where(item => item?.ItemIndex == Id("Material")).Select(item => item.Count).Order().SequenceEqual(new ushort[] { 201, 999 }) != true)
                    throw new Exception("登录材料归类、999合堆或数量守恒失败。");
                await AutoLearn(false);
                await connection.Send(new C.Chat { Message = "@levelhero 50" }); await Until(() => state.Hero!.Level == 50);
                if (state.Hero!.Magics.Count != 0) throw new Exception("英雄忽略自动学习开关。");
                var bag = JsonSerializer.Serialize(state.User.Inventory.Concat(state.User.QuestInventory)); var gold = state.User.Gold;
                await AutoLearn(true); packets.Clear(); await connection.Send(new C.Chat { Message = "@levelhero 51" });
                await Until(() => state.Hero!.Level == 51 && state.Hero.Magics.Any(m => m.Spell == spell));
                if (state.User.Magics.Count != 0 || state.Hero.Magics.Count != 1 ||
                    !packets.OfType<S.NewMagic>().Any(p => p.Hero && p.Magic.Spell == spell) ||
                    !packets.OfType<S.Chat>().Any(p => p.Message.Contains("升级自动学习")) || bag != JsonSerializer.Serialize(state.User.Inventory.Concat(state.User.QuestInventory)) || gold != state.User.Gold)
                    throw new Exception("英雄自动学习回执、提示、隔离或资产保护失败。");
                await connection.Send(new C.MagicKey { Spell = spell, Key = 19 });
                state.Hero.Magics.Single().Key = 19;
                await connection.Send(new C.Chat { Message = "@levelhero 52" }); await Until(() => state.Hero.Level == 52);
                if (state.Hero.Magics.Single().Key != 19) throw new Exception("英雄重复升级重置快捷键。");
                await connection.Send(new C.Chat { Message = "@lv 51" }); await Until(() => state.User.Level == 51 && state.User.Magics.Any(m => m.Spell == spell));
                if (job == MirClass.Warrior)
                {
                    await connection.Send(new C.PublicShopCatalogRequest()); await Until(() => state.PublicShop != null);
                    if (!state.PublicShop!.Items.Any(item => item.ItemIndex == Id("LowBook")) || state.PublicShop.Items.Any(item => item.ItemIndex == Id("HighBook")))
                        throw new Exception("旧商城货架未正确过滤35级及以上书籍。");
                    var before = Snapshot(); await connection.Send(new C.PublicShopBuy { RequestId = 1, ItemIndex = Id("HighBook"), UnitPrice = 11, Count = 1 });
                    await Until(() => packets.OfType<S.PublicShopResult>().Any(p => p.RequestId == 1));
                    if (packets.OfType<S.PublicShopResult>().Last().Success || Snapshot() != before) throw new Exception("伪造高等级技能书购买改变资产。");
                    var doc = (await http.GetFromJsonAsync<PublicShopDocument>("/management/shop"))!;
                    doc.Catalog.Tabs[0].Products = doc.Catalog.Tabs[0].Products.Append(new() { ItemIndex = Id("HighBook"), PriceRate = .55m }).ToArray();
                    using (var denied = await http.PutAsJsonAsync("/management/shop", new PublicShopEdit(doc.Catalog, doc.Version)))
                        if (denied.StatusCode != System.Net.HttpStatusCode.BadRequest) throw new Exception("后台仍允许上架高等级技能书。");
                    doc = (await http.GetFromJsonAsync<PublicShopDocument>("/management/shop"))!;
                    using (var saved = await http.PutAsJsonAsync("/management/shop", new PublicShopEdit(doc.Catalog, doc.Version))) saved.EnsureSuccessStatusCode();
                    packets.Clear(); await connection.Send(new C.PublicShopCatalogRequest()); await Until(() => packets.OfType<S.PublicShop>().Any());
                    var offer = state.PublicShop.Items.Single(item => item.ItemIndex == Id("Material"));
                    await connection.Send(new C.PublicShopBuy { RequestId = 2, ItemIndex = offer.ItemIndex, UnitPrice = offer.Price, Count = 5 });
                    await Until(() => packets.OfType<S.PublicShopResult>().Any(p => p.RequestId == 2));
                    if (!packets.OfType<S.PublicShopResult>().Last().Success || state.User.Gold != gold - 35) throw new Exception("商城购材未按原价结算。");
                    var npc = state.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant && actor.Name == "百工叟");
                    await connection.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" }); await Until(() => packets.OfType<S.NPCResponse>().Any());
                    await connection.Send(new C.AcceptQuest { NPCIndex = npc.Id, QuestIndex = Id("Quest") }); await Until(() => state.Quests.ContainsKey(Id("Quest")));
                    if (!state.Quests[Id("Quest")].Completed) throw new Exception("材料栏已有收集物未计入接取任务。");
                    await connection.Send(new C.FinishQuest { QuestIndex = Id("Quest"), SelectedItemIndex = -1 }); await Until(() => state.CompletedQuests.Contains(Id("Quest")));
                    if (state.User.QuestInventory.Where(item => item?.ItemIndex == Id("Meat")).Sum(item => item.Count) != 1 || state.User.Gold != gold - 35 + 17)
                        throw new Exception("真实任务交付未准确扣材料/发金币或误删剩余材料。");
                    packets.Clear(); await connection.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@MAIN]" }); await Until(() => packets.OfType<S.NPCResponse>().Any());
                    await connection.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@CRAFT]" }); await Until(() => packets.OfType<S.NPCGoods>().Any(p => p.Type == PanelType.Craft));
                    var goods = packets.OfType<S.NPCGoods>().Last().List.Single();
                    await connection.Send(new C.CraftItem { UniqueID = goods.UniqueID, Count = 1, Slots = [] }); await Until(() => packets.OfType<S.CraftItem>().Any());
                    if (!packets.OfType<S.CraftItem>().Last().Success || state.User.QuestInventory.Where(item => item?.ItemIndex == Id("Material")).Sum(item => item.Count) != 1203 ||
                        state.User.Inventory.Where(item => item?.ItemIndex == Id("Potion")).Sum(item => item.Count) != 3 || state.User.Gold != gold - 35 + 17 - 3)
                        throw new Exception("原TCP制作没有从材料栏准确消耗并发放。");
                    packets.Clear(); await connection.Send(new C.StorageRequest()); await Until(() => packets.OfType<S.UserStorage>().Any());
                    var slot = Array.FindIndex(state.User.QuestInventory, item => item?.ItemIndex == Id("Ore")); var ore = state.User.QuestInventory[slot];
                    await connection.Send(new C.StoreItem { From = ~slot, To = 0 }); await Until(() => packets.OfType<S.StoreItem>().Any());
                    if (!packets.OfType<S.StoreItem>().Last().Success || state.User.QuestInventory[slot] != null || state.Storage[0]?.UniqueID != ore.UniqueID) throw new Exception("材料存仓回执错误。");
                    await connection.Send(new C.TakeBackItem { From = 0, To = 6 });
                    await Until(() => state.Storage[0] == null && state.User.QuestInventory.Any(item => item?.UniqueID == ore.UniqueID));
                    if (state.User.Inventory.Any(item => item?.UniqueID == ore.UniqueID) || state.User.QuestInventory.Single(item => item?.UniqueID == ore.UniqueID).CurrentDura != 5200)
                        throw new Exception("取回自动归类复制矿石或改变纯度。");
                }
                snapshots[job.ToString()] = Snapshot();
            }
            packets.Clear(); await connection.Send(new C.LogOut()); await Until(() => packets.OfType<S.LogOutSuccess>().Any());
            await Task.Delay(6500);
        }
        if (!verify) File.WriteAllText(record, JsonSerializer.Serialize(snapshots));
        Console.WriteLine(verify ? "PASS original TCP full restart: exact five-class player/hero skills/keys, material IDs/counts/purity, quests and gold restored." :
            "PASS original TCP: five-class hero auto-learning toggle/duplicate/asset protection; old shop level gate/admin rejection; material migration/999/shop/quest/craft/storage with exact receipts.");
    }
}
