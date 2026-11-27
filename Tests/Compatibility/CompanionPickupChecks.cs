using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Crystal.Native;
using Mir2.Companion;
using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class CompanionPickupChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        Planning(check);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var profile = new CompanionProfile { Id = Guid.NewGuid(), Name = "拾取穿戴检查", AcceptGroup = false, Chat = false,
            Traits = new() { Frugality = 95, Collection = 60 } };
        var session = new CompanionSession(new(profile, "test", "private", true, "127.0.0.1", port,
            "test", "pickup-run", CompanionProtocol.Fingerprint, "", "", "", 1), _ => Task.CompletedTask);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic; var type = typeof(CompanionSession);
        FieldInfo Field(string name) => type.GetField(name, flags)!;
        Task Invoke(string method, params object[] args) => (Task)type.GetMethod(method, flags)!.Invoke(session, args)!;
        var state = (GameState)Field("state").GetValue(session)!;
        var connection = (GameConnection)Field("connection").GetValue(session)!;
        await connection.Connect("127.0.0.1", port); using var peer = await listener.AcceptTcpClientAsync(); var stream = peer.GetStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            state.Apply(new S.MapInformation { MapIndex = 1, FileName = "Pickup" });
            state.Apply(new S.BaseStatsInfo { Stats = new BaseStats(MirClass.Wizard) { Stats = [new(Stat.HP) { Base = 100 }, new(Stat.MP) { Base = 100 }, new(Stat.BagWeight) { Base = 100 }] } });
            state.Apply(new S.UserInformation { ObjectID = 1, Name = profile.Name, Class = MirClass.Wizard, Level = 40, HP = 100, MP = 100, Gold = 306,
                Location = new(5, 5), Inventory = new UserItem[80], Equipment = new UserItem[14], QuestInventory = [], Magics = [] });
            var map = (Client.MirObjects.MapReader)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Client.MirObjects.MapReader));
            map.Width = map.Height = 24; map.MapCells = new Client.MirObjects.CellInfo[24, 24];
            for (var x = 0; x < 24; x++) for (var y = 0; y < 24; y++) map.MapCells[x, y] = new();
            Field("map").SetValue(session, map); Field("mapName").SetValue(session, "Pickup");
            var knowledge = new CompanionKnowledge("pickup", 1, false, true, true, [], [], [], [], NoHero: true,
                Goal: new("service", 1, 18, 5, "购买新手武器", Key: "service/9"), LootTargets: [3]);
            session.Knowledge = knowledge;
            state.Apply(new S.ObjectGold { ObjectID = 3, Gold = 50, Location = new(5, 5) });
            await Invoke("Think", Environment.TickCount64); check(await Read() == ClientPacketIds.PickUp, "补装赶路完全跳过脚下金币");
            state.Actors[3].Location = new(7, 5);
            session.Knowledge = knowledge with { Goal = new("hunt", 1, 8, 7, "普通练级", MonsterIds: [12], Key: "hunt/1/12"), Enemies = [new(2, 12, 1, 10, true)] };
            state.Apply(new S.ObjectMonster { ObjectID = 2, Name = "刷新小怪", Location = new(8, 7), AI = 0 });
            await Invoke("Think", Environment.TickCount64); check(await Read() == ClientPacketIds.Walk && (string)Field("lastState").GetValue(session)! == "拾取中", "持续刷新小怪饿死普通人格的近处拾取");
            Field("moving").SetValue(session, false); Field("combatTarget").SetValue(session, 2u);
            await Invoke("Think", Environment.TickCount64); check(await Read() == ClientPacketIds.Walk && (string)Field("lastState").GetValue(session)! == "打怪中", "正在进行的战斗被反复拾取抢占");
            Field("moving").SetValue(session, false);
            session.Knowledge = knowledge with { Goal = new("hunt", 2, 18, 5, "跨图任务", NextExit: new(1, 18, 5, 2)) };
            await Invoke("Think", Environment.TickCount64); check(await Read() == ClientPacketIds.Walk && (string)Field("lastState").GetValue(session)! != "拾取中", "跨图赶路为无关物品绕路");
            Field("moving").SetValue(session, false);
            session.Knowledge = knowledge with { Goal = new("wait", 1, 5, 5, "等待筹资", Key: "fund/wait") };
            await Invoke("Think", Environment.TickCount64); check(await Read() == ClientPacketIds.Walk && (string)Field("lastState").GetValue(session)! == "拾取中", "等待筹资时不捡附近可用金币");
            Field("moving").SetValue(session, false); state.Actors[3].Location = new(5, 5);
            Field("pendingAsset").SetValue(session, "buy"); Field("assetSent").SetValue(session, Environment.TickCount64);
            await Invoke("Think", Environment.TickCount64); check(!stream.DataAvailable, "资产未决时插入拾取请求"); Field("pendingAsset").SetValue(session, null);
            state.User!.Inventory[0] = new(new ItemInfo { Index = 10, Type = ItemType.Potion, Stats = new Stats { [Stat.HP] = 30 } }) { Count = 1 };
            var weapon = new ItemInfo { Index = 11, Type = ItemType.Weapon, Durability = 1000, Price = 40 };
            var offered = new UserItem(weapon) { UniqueID = 111, CurrentDura = 1000, MaxDura = 1000 };
            Field("goods").SetValue(session, new S.NPCGoods { Rate = 1.2f, List = [offered] });
            session.Knowledge = knowledge;
            await Invoke("BuySupplies"); check(await Read() == ClientPacketIds.BuyItem, "仅剩一瓶红药导致空武器槽补装永久无法成交");
            check(state.User.Gold == 306 && state.User.Equipment[0] == null, "购买请求提前扣款或预穿装备");
            Field("pendingAsset").SetValue(session, null); state.User.Inventory[1] = offered;
            await Invoke("ImproveEquipment"); check(await Read() == ClientPacketIds.EquipItem, "获得武器后未走原穿戴请求");
            await Invoke("Apply", new S.EquipItem { Grid = MirGridType.Inventory, UniqueID = 111, To = 0, Success = true });
            check(state.User.Equipment[0]?.UniqueID == 111 && state.User.Inventory[1] == null, "穿戴回执未保留原编号或未清背包格");
        }
        finally { await session.Stop(); }
        async Task<ClientPacketIds> Read()
        {
            var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
            var body = new byte[BitConverter.ToUInt16(header) - 4]; await stream.ReadExactlyAsync(body, timeout.Token);
            return (ClientPacketIds)BitConverter.ToInt16(header, 2);
        }
    }

    private static void Planning(Action<bool, string> check)
    {
        var world = Envir.Main; var scripts = world.Scripts; var index = world.ScriptIndex; var npcs = world.NPCs;
        try
        {
            world.Scripts = []; world.NPCs = [];
            var map = new Map(new MapInfo { Index = 901 });
            var player = new PlayerObject { Info = new CharacterInfo { Level = 40 }, Account = new AccountInfo { Gold = 306 },
                CurrentMap = map, Stats = new Stats { [Stat.BagWeight] = 100, [Stat.HandWeight] = 100 } };
            var profile = new CompanionProfile { Traits = new() { Frugality = 95 } };
            var npc = new NPCObject(new NPCInfo { Name = "补装铁匠", FileName = "PickupPlanningAbsent", Rate = 120 }) { CurrentMap = map };
            var script = NPCScript.Get(npc.ScriptID); script.NPCPages = [new(NPCScript.BuyKey)];
            var info = new ItemInfo { Index = 1, Type = ItemType.Weapon, Durability = 1000, Price = 40 };
            script.Goods = [new(info) { CurrentDura = 1000, MaxDura = 1000 }];
            KnownGoal? Choose() => CompanionRecovery.Choose(player, profile, [map], [npc], new() { [901] = null }, null);
            check(Choose()?.NpcId == npc.ObjectID, "真实含税价格可负担的基础武器没有规划补装");
            npc.Info.Rate = 200; check(Choose() == null, "补装规划忽略NPC实际倍率及人格预算，反复走向买不起的商店"); npc.Info.Rate = 120;
            player.Stats[Stat.BagWeight] = 0; info.Weight = 1; check(Choose() == null, "基础补装忽略实际剩余负重"); player.Stats[Stat.BagWeight] = 100;
            player.Stats[Stat.HandWeight] = 0; check(Choose() == null, "补装规划忽略原引擎腕力要求"); player.Stats[Stat.HandWeight] = 100;
            script.NPCPages = []; check(Choose() == null, "没有真实购买入口也被规划为补装商店");
            check(player.Account.Gold == 306 && player.Info.Inventory.All(i => i == null), "只读补装规划修改了资产");
        }
        finally { world.Scripts = scripts; world.ScriptIndex = index; world.NPCs = npcs; }
    }
}
