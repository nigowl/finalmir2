using Godot;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private void PickupSettings(bool enabled, bool whitelist, string names)
    {
        var panel = SettingsPanel();
        Setting(panel, "自动拣取").ButtonPressed = enabled;
        var selected = names.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in Client.Assets.ItemCatalog.Select(item => item.Name).Append("金币").Distinct())
            SetLocalItemRule(name, 1, whitelist ? selected.Contains(name) : !selected.Contains(name));
        CloseSettingsPanel(panel);
    }

    private async Task VerifyLootAndRoamingOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("拣取检查必须使用隔离的原生测试版本与缓存。");
        await EnterUiFixture();
        // 只改变本地候选可见性：此处检查拣取/寻路，不向原服改怪或战斗。
        var monsters = ClientAutomation.Targets(State).ToArray();
        foreach (var actor in monsters) actor.MasterObjectId = State.User!.ObjectID;
        try
        {
            if (!State.User!.Inventory.Any(item => item?.Info?.Type == ItemType.Potion))
            {
                stage = "原商店准备拣取测试药品";
                packets.Clear(); var npc = State.Actors.Values.Single(actor => actor.Kind == ObjectType.Merchant);
                Client.SelectTarget(npc.Id); await Packet<S.NPCResponse>();
                Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = "[@BUY]" }); var goods = await Packet<S.NPCGoods>();
                var supply = goods.List.First(item => item.Info.Type == ItemType.Potion);
                Client.Send(new C.BuyItem { ItemIndex = supply.UniqueID, Count = 1, Type = goods.Type });
                await Until(() => State.User.Inventory.Any(item => item?.Info?.Type == ItemType.Potion));
                GameArt("关闭 NPC").EmitSignal(BaseButton.SignalName.Pressed);
            }
            var potion = State.User!.Inventory.First(item => item?.Info?.Type == ItemType.Potion);
            var index = potion.Info.Index; var name = potion.Info.FriendlyName; var gold = State.User.Gold;
            int Count() => State.User.Inventory.Where(item => item?.Info?.Index == index).Sum(item => item.Count);
            var total = Count();
            foreach (var whitelist in new[] { true, false })
            {
                stage = whitelist ? "native pickup whitelist" : "native pickup blacklist";
                PickupSettings(true, whitelist, whitelist ? "不存在的指定物品" : name);
                var known = State.Actors.Keys.ToHashSet();
                potion = State.User.Inventory.First(item => item?.Info?.Index == index);
                packets.Clear(); Client.Send(new C.DropItem { UniqueID = potion.UniqueID, Count = 1 });
                if (!(await Packet<S.DropItem>()).Success) throw new Exception("拣取检查的原丢弃被拒绝。");
                await Until(() => State.Actors.Values.Any(actor => !known.Contains(actor.Id) && actor.Kind == ObjectType.Item && ClientPickup.Name(actor) == name));
                var ground = State.Actors.Values.Single(actor => !known.Contains(actor.Id) && actor.Kind == ObjectType.Item && ClientPickup.Name(actor) == name);
                EnableAutomaticBattle(false); await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
                if (!State.Actors.ContainsKey(ground.Id) || Count() != total - 1) throw new Exception("名单排除的物品仍被自动拣取。");
                PickupSettings(true, whitelist, whitelist ? name : ""); packets.RemoveAll(packet => packet is S.GainedItem);
                await Until(() => !State.Actors.ContainsKey(ground.Id) && Count() == total);
                if (packets.OfType<S.GainedItem>().Count() != 1 || State.User.Gold != gold) throw new Exception("自动拣取重复发送/入包或改变金币。");
                ToggleBattleMenu();
                await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
            }
            PickupSettings(false, false, "");
            GD.Print("PASS native pickup: whitelist/blacklist exclusions, real DropItem/PickUp/GainedItem/ObjectRemove, exactly one recovery and item/gold conservation.");
            stage = "native long-distance roaming and persistent destination";
            var start = State.User.Location; EnableAutomaticBattle(true);
            await Until(() => Client.MiniMap.Route.Count > 1);
            var goal = Client.MiniMap.Route[^1];
            if (Functions.MaxDistance(start, goal) < 32) throw new Exception("跑图仍只选择附近短距离目标。");
            await Until(() => Functions.MaxDistance(start, State.User.Location) >= 4);
            if (Client.MiniMap.Route.Count == 0 || Client.MiniMap.Route[^1] != goal) throw new Exception("跑图中途反复改变目标。");
            await Screenshot("exploration-route"); ToggleBattleMenu();
            GD.Print($"PASS native roaming: original TCP movement {start} -> {State.User.Location}, persistent distant goal {goal}, route visible and menu stop.");
        }
        finally { foreach (var actor in monsters) actor.MasterObjectId = 0; }
        await Client.Quit();
    }
}
