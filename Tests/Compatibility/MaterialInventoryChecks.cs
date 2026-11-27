using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Sockets;
using Crystal.Native;
using Server;
using Server.Helpers;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirNetwork;
using Server.MirObjects;
using S = ServerPackets;

internal static class MaterialInventoryChecks
{
    private sealed class Player : PlayerObject
    {
        internal readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    internal static void Run(Action<bool, string> check)
    {
        var world = Envir.Main;
        var oldItems = world.ItemInfoList; var oldQuests = world.QuestInfoList; var oldRecipes = world.RecipeInfoList;
        var password = Settings.RequireStoragePassword;
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var peer = new TcpClient(); peer.Connect((IPEndPoint)listener.LocalEndpoint);
        var connection = new MirConnection(990101, listener.AcceptTcpClient());
        try
        {
            Settings.RequireStoragePassword = false;
            var material = new ItemInfo { Index = 990101, Name = "测试制作材料", Type = ItemType.CraftingMaterial, StackSize = 999, Price = 100 };
            var ore = new ItemInfo { Index = 990102, Name = "测试矿石", Type = ItemType.Ore, StackSize = 999 };
            var meat = new ItemInfo { Index = 990103, Name = "测试任务肉", Type = ItemType.Meat, StackSize = 999 };
            var oil = new ItemInfo { Index = 990104, Name = "测试配方油", Type = ItemType.Scroll, Shape = 1, StackSize = 999 };
            var potion = new ItemInfo { Index = 990105, Name = "测试普通药", Type = ItemType.Potion, StackSize = 999, Price = 100 };
            var food = new ItemInfo { Index = 990106, Name = "测试普通食物", Type = ItemType.Food, StackSize = 999 };
            var amulet = new ItemInfo { Index = 990107, Name = "测试普通符", Type = ItemType.Amulet, StackSize = 999 };
            var definition = new QuestInfo { Index = 990101, ItemTasks = [new QuestItemTask { Item = meat, Count = 2 }] };
            var recipe = (RecipeInfo)RuntimeHelpers.GetUninitializedObject(typeof(RecipeInfo));
            recipe.Item = new UserItem(potion) { UniqueID = 990101 }; recipe.Chance = 100; recipe.Gold = 10;
            recipe.Tools = []; recipe.Ingredients = [new UserItem(material) { Count = 2 }];
            recipe.RequiredClass = []; recipe.RequiredFlag = []; recipe.RequiredQuest = [];
            world.ItemInfoList = [material, ore, meat, oil, potion, food, amulet]; world.QuestInfoList = [definition]; world.RecipeInfoList = [recipe];
            check(new[] { material, ore, meat }.All(item => MaterialInventoryRules.Contains(item, world)) &&
                new[] { oil, potion, food, amulet }.All(item => !MaterialInventoryRules.Contains(item, world)), "材料归类混入普通药品/食物/战斗耗材");
            recipe.Ingredients.Add(new UserItem(oil));
            check(MaterialInventoryRules.Contains(oil, world), "配方引用的非材料类型没有纳入材料栏");
            recipe.Ingredients.RemoveAt(1);
            world.RecipeInfoList.Add((RecipeInfo)RuntimeHelpers.GetUninitializedObject(typeof(RecipeInfo)));
            check(!MaterialInventoryRules.Contains(potion, world), "无有效原料的旧配方使普通物品归类失败");
            world.RecipeInfoList.RemoveAt(1);
            var weapon = new ItemInfo { Index = 990108, Name = "配方普通武器", Type = ItemType.Weapon };
            var book = new ItemInfo { Index = 990109, Name = "配方普通书", Type = ItemType.Book };
            var torch = new ItemInfo { Index = 990110, Name = "配方普通蜡烛", Type = ItemType.Torch };
            recipe.Ingredients.AddRange(new[] { weapon, book, torch, potion, amulet }.Select(info => new UserItem(info)));
            definition.CarryItems.AddRange(new[] { weapon, book, torch, potion, amulet }.Select(info => new QuestItemTask { Item = info, Count = 1 }));
            check(new[] { weapon, book, torch, potion, amulet }.All(info => !MaterialInventoryRules.Contains(info, world)),
                "配方/任务引用将普通装备、药品、技能书或毒符误移入材料栏");
            recipe.Ingredients.RemoveRange(1, 5); definition.CarryItems.Clear();
            var account = new AccountInfo { Gold = 1000 };
            var player = new Player { Info = new CharacterInfo { Index = 990101, Level = 1, Name = "材料栏验证", AccountInfo = account, CreationIP = "127.0.0.1", Heroes = new HeroInfo[1] },
                Account = account, Stats = new Stats { [Stat.BagWeight] = 1000 }, CurrentMap = new Map(new MapInfo()) };
            player.Report = new Reporting(player);
            player.Connection = connection; player.Info.Mount = new MountInfo(player);
            var progress = new QuestProgressInfo(definition.Index); progress.Init(player); player.CurrentQuests.Add(progress);
            var items = new[] { material, ore, meat, potion, food, amulet }.Select((info, n) => new UserItem(info) { UniqueID = (ulong)(990100 + n), Count = 3 }).ToArray();
            Array.Copy(items, player.Info.Inventory, items.Length);
            player.OrganizeQuestInventory(false);
            check(player.Info.QuestInventory.Take(3).SequenceEqual(items.Take(3)) && player.Info.Inventory.Take(3).All(item => item == null) &&
                player.Info.Inventory.Skip(3).Take(3).SequenceEqual(items.Skip(3)), "登录迁移没有保留原物品/数量或误移动药符");
            player.OrganizeQuestInventory();
            check(progress.Completed && progress.ItemTaskCount.Single().Count == 3, "迁移后任务进度没有按实际材料重算");
            player.RecalculateQuestBag(definition);
            check(player.Info.QuestInventory.Take(3).All(item => item?.Count == 3), "任务结束误删普通任务材料或矿石");
            player.TakeQuestItem(meat, 2); player.OrganizeQuestInventory();
            check(items[2].Count == 1 && !progress.Completed && progress.ItemTaskCount.Single().Count == 1, "交付/制作后材料数量或任务状态未更新");
            var segment = new NPCSegment(new NPCPage("[@MAIN]"), [], [], [], [], []);
            segment.ParseCheck("CHECKITEM 测试任务肉 1"); check(segment.Check(player), "NPC检查没有读取材料栏");
            player.Packets.Clear(); segment.ParseAct(segment.ActList, "TAKEITEM 测试任务肉 1"); segment.Check(player);
            check(!player.CarriedItems.Any(item => item.Info == meat) && player.Packets.OfType<S.DeleteQuestItem>().Single().Count == 1,
                "NPC交付未从实际材料栏扣除或回执错误");
            var script = (NPCScript)RuntimeHelpers.GetUninitializedObject(typeof(NPCScript)); script.CraftGoods = [recipe];
            player.Packets.Clear(); script.Craft(player, recipe.Item.UniqueID, 1, []);
            check(player.Packets.OfType<S.CraftItem>().Last().Success && items[0].Count == 1 && account.Gold == 990 &&
                player.Info.Inventory.Where(item => item?.Info == potion).Sum(item => item.Count) == 4, "原配方制作没有消耗材料栏并按原价发放");
            var gold = account.Gold; script.Craft(player, recipe.Item.UniqueID, 1, []);
            check(!player.Packets.OfType<S.CraftItem>().Last().Success && items[0].Count == 1 && account.Gold == gold, "材料不足仍制作/扣款");
            var state = new GameState(); foreach (var info in world.ItemInfoList) state.Apply(new S.NewItemInfo { Info = info });
            state.Apply(new S.UserInformation { ObjectID = 1, Inventory = new UserItem[80], Equipment = new UserItem[14], QuestInventory = new UserItem[40] });
            state.Apply(new S.UserStorage { Storage = new UserItem[80] });
            var source = items[1]; var slot = Array.IndexOf(player.Info.QuestInventory, source);
            state.User!.QuestInventory[slot] = source.Clone();
            player.Packets.Clear(); player.StoreItem(~slot, 0);
            foreach (var packet in player.Packets) state.Apply(packet);
            check(player.Packets.OfType<S.StoreItem>().Single().Success && account.Storage[0] == source && player.Info.QuestInventory[slot] == null &&
                state.Storage[0]?.UniqueID == source.UniqueID && state.User.QuestInventory[slot] == null, "材料存仓未保持编号或客户端归约错误");
            Array.Fill(player.Info.Inventory, new UserItem(food));
            player.Packets.Clear(); player.TakeBackItem(0, -1); foreach (var packet in player.Packets) state.Apply(packet);
            check(player.Packets.OfType<S.TakeBackItem>().Single().Success && account.Storage[0] == null && player.Info.QuestInventory.Any(item => item == source) &&
                state.Storage[0] == null && state.User.QuestInventory.Any(item => item?.UniqueID == source.UniqueID), "普通包满时不能取回材料或出现双副本");
            account.Storage[0] = new UserItem(potion) { UniqueID = 990201 };
            player.TakeBackItem(0, -1); check(!player.Packets.OfType<S.TakeBackItem>().Last().Success && account.Storage[0] != null, "普通物品绕过背包容量进入材料栏");
            player.Info.QuestInventory = Enumerable.Range(0, 40).Select(n => new UserItem(material) { UniqueID = (ulong)(991000 + n), Count = 999 }).ToArray();
            account.Storage[0] = source;
            player.TakeBackItem(0, -1); check(!player.Packets.OfType<S.TakeBackItem>().Last().Success && account.Storage[0] == source, "材料栏满时取回丢物");
            check(!player.CanGainItem(new UserItem(ore)) && !player.CanGainItems([new UserItem(ore)]), "材料栏满时预检仍放行新材料");
            var original = player.Info.QuestInventory.Select(item => (item.UniqueID, item.Count)).ToArray();
            using var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) player.Info.Save(writer);
            stream.Position = 0; using var reader = new BinaryReader(stream); var restored = new CharacterInfo(reader, Envir.Version, Envir.CustomVersion);
            check(restored.QuestInventory.Select(item => (item.UniqueID, item.Count)).SequenceEqual(original), "材料栏存档恢复改变编号或数量");
            state.User.QuestInventory[0] = new UserItem(oil) { UniqueID = 992001, Count = 2 };
            state.Apply(new S.UseItem { UniqueID = 992001, Success = true });
            check(state.User.QuestInventory[0].Count == 1, "材料中可用配方道具的使用回执未扣除");
            player.Info.Inventory = new UserItem[86]; player.Info.QuestInventory = new UserItem[40];
            var oldPotion = new UserItem(potion) { UniqueID = 992002, Count = 23 };
            var oldWeapon = new UserItem(weapon) { UniqueID = 992003, CurrentDura = 4321 };
            player.Info.QuestInventory[0] = oldPotion; player.Info.QuestInventory[1] = oldWeapon;
            player.Packets.Clear(); player.OrganizeQuestInventory();
            check(player.Info.QuestInventory.All(item => item == null) && player.Info.Inventory.Contains(oldPotion) && oldPotion.Count == 23 &&
                player.Info.Inventory.Contains(oldWeapon) && oldWeapon.CurrentDura == 4321 && player.Packets.OfType<S.DeleteQuestItem>().Count() == 2 &&
                player.Packets.OfType<S.GainedItem>().Count() == 2, "普通物品迁回背包改变编号/数量/耐久或没有准确回执");
            Array.Fill(player.Info.Inventory, new UserItem(food) { Count = 999 });
            player.Info.QuestInventory[0] = oldPotion; player.OrganizeQuestInventory();
            check(player.Info.QuestInventory[0] == oldPotion && oldPotion.Count == 23, "普通背包满时迁回丢弃旧物品");
        }
        finally { connection.Disconnect(20); world.ItemInfoList = oldItems; world.QuestInfoList = oldQuests; world.RecipeInfoList = oldRecipes; Settings.RequireStoragePassword = password; }
    }
}
