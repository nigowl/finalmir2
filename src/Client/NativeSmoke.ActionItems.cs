using System.Reflection;
using System.Text.Json;
using Crystal.Native;
using Client.MirObjects;
using Godot;
using Point = System.Drawing.Point;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyActionItems()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-actions.", StringComparison.Ordinal))
            throw new InvalidOperationException("动作与物品检查必须使用隔离版本。");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var prefs = (ClientPreferences)typeof(Main).GetField("preferences", flags)!.GetValue(Client)!;
        bool Moving() => (bool)typeof(Main).GetField("moving", flags)!.GetValue(Client)!;
        prefs.SmartBattle = false;
        stage = "action fixture login";
        await Until(() => Client.HandshakeReady);
        Client.Login("NativeWizard", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading && State.Actors.Values.Any(actor => actor.Kind == ObjectType.Monster));
        // 隔离技能角色带一瓶负HP药，先使用它，避免低初始生命影响后续动作检查。
        var injury = State.User!.Inventory.First(item => item?.Info.Type == ItemType.Potion);
        Client.UseItem(injury.UniqueID); await Packet<S.UseItem>();
        stage = "running skill queues until confirmed step then resumes route";
        var origin = State.User.Location;
        var destination = new Point(origin.X, origin.Y - 4);
        await Until(() => Client.World.PlayerAnimationReady(MirDirection.Up, MirAction.Running));
        var aimPoint = new Point(origin.X - 2, origin.Y - 2);
        await MoveMouse(Client.World.Screen(aimPoint) + new Vector2(24, 16));
        typeof(Main).GetField("moving", flags)!.SetValue(Client, true);
        typeof(Main).GetField("moveTime", flags)!.SetValue(Client, Environment.TickCount64 - 3001);
        Client.Cast(State.User.Magics.FindIndex(magic => magic.Spell == Spell.FireWall));
        typeof(Main).GetMethod("ProcessSkillPreparation", flags)!.Invoke(Client, [Environment.TickCount64]);
        if (Moving() || typeof(Main).GetField("preparedSkill", flags)!.GetValue(Client) != null)
            throw new Exception("移动回执超时仍锁住待施法。");
        Client.SetDestination(destination, true);
        await Until(Moving);
        packets.RemoveAll(packet => packet is S.Magic);
        var requestedAt = Environment.TickCount64;
        Client.Cast(State.User.Magics.FindIndex(magic => magic.Spell == Spell.FireWall));
        if (State.Me!.Action == MirAction.Spell) throw new Exception("仍在等待移动回执时提前切换施法动作。");
        await Until(() => !Moving() && (long)typeof(Main).GetField("castPendingUntil", flags)!.GetValue(Client)! > 0);
        var submittedAfter = Environment.TickCount64 - State.Me.MotionTime;
        if (submittedAfter is < 0 or >= 200) throw new Exception($"手动施法仍在等走跑动画，移动回执后 {submittedAfter}ms 才提交。");
        var result = await Packet<S.Magic>();
        if (result.Spell != Spell.FireWall || !result.Cast || result.TargetID != 0 || result.Target != aimPoint || Moving())
            throw new Exception($"跑步施法目标/时序错误：{result.Spell}, {result.TargetID}, {result.Target}, moving={Moving()}");
        var castTime = State.Me.ActionTime;
        GD.Print($"PROFILE manual cast: movement ack to send {submittedAfter}ms, input to authoritative cast {castTime - requestedAt}ms.");
        var visual = Client.World.Visual(State.Me, castTime + 100);
        State.Apply(new S.Struck { AttackerID = 77 });
        if (State.Me.Action != MirAction.Spell || State.Me.ActionTime != castTime || Client.World.Visual(State.Me, castTime + 100) != visual)
            throw new Exception("受击截断施法或让人物位置闪回。");
        await ToSignal(GetTree().CreateTimer(.18), SceneTreeTimer.SignalName.Timeout);
        await CombatScreenshot("run-cast");
        await Until(() => State.User.Location == destination && !Moving());
        await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
        stage = "actual harvest acknowledgement and two-frame animation";
        packets.RemoveAll(packet => packet is S.ObjectHarvest);
        Client.Send(new C.Harvest { Direction = MirDirection.Left });
        var harvest = await Packet<S.ObjectHarvest>();
        if (harvest.ObjectID != State.User.ObjectID || State.Me.Action != MirAction.Harvest)
            throw new Exception("本人割肉没有真实动作回执。");
        if (Client.World.ActorFrame(State.Me, State.Me.ActionTime + 20, out var first) == null ||
            Client.World.ActorFrame(State.Me, State.Me.ActionTime + 340, out var second) == null || first == second)
            throw new Exception("割肉动作没有完整的两帧。");
        await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
        stage = "manual targeted magic enables profession smart combination";
        prefs.SmartBattle = true;
        var target = ClientAutomation.Targets(State).First();
        Client.World.Selected = target.Id;
        packets.RemoveAll(packet => packet is S.Magic);
        Client.Cast(State.User.Magics.FindIndex(magic => magic.Spell == Spell.FireBall));
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.FireBall && packet.Cast));
        await Until(() => packets.OfType<S.Magic>().Any(packet => packet.Spell == Spell.MagicShield && packet.Cast) && State.Me.MagicShield);
        prefs.SmartBattle = false;
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        Client.SetDestination(State.User.Location);
        stage = "authoritative continuous movement";
        var previousLocation = State.User.Location;
        var acknowledgements = new List<long>();
        void Moved(Packet packet)
        {
            if (packet is not S.UserLocation location || location.Location == previousLocation) return;
            previousLocation = location.Location; acknowledgements.Add(Environment.TickCount64);
        }
        Client.PacketReceived += Moved;
        var speedGoal = new Point(State.User.Location.X, State.User.Location.Y - 8);
        try { Client.SetDestination(speedGoal, true); await Until(() => State.User.Location == speedGoal && !Moving()); }
        finally { Client.PacketReceived -= Moved; }
        var intervals = acknowledgements.Zip(acknowledgements.Skip(1), (a, b) => b - a).ToArray();
        // 包在绘制帧中采样，两个回执的客户端采样误差可差一帧。
        if (intervals.Length < 3 || intervals.Any(interval => interval < State.PlayerTiming.MoveMilliseconds - 20) || intervals.Average() >= State.PlayerTiming.MoveMilliseconds + 100)
            throw new Exception($"提速后真实移动间隔异常：{string.Join(',', intervals)}ms。");
        GD.Print($"PROFILE action speed: move baseline=600ms, configured={State.PlayerTiming.MoveMilliseconds}ms, actual={string.Join(',', intervals)}ms.");
        await ToSignal(GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout);
        stage = "item checkbox table and persisted independent rules";
        await Until(() => Client.Theme.GetStylebox("pressed", "GameTab") is StyleBoxTexture);
        var panel = SettingsPanel();
        var tabs = panel.GetChildren().OfType<TabContainer>().Single();
        if (tabs.GetTabCount() != 5 || tabs.GetTabTitle(4) != "物品") throw new Exception("缺少物品页。");
        tabs.CurrentTab = 4;
        var table = (Tree)panel.FindChild("ItemRuleTable", true, false);
        if (table.Columns != 4 || table.GetColumnTitle(0) != "显示" || table.GetColumnTitle(1) != "拣取" || table.GetColumnTitle(2) != "回收" || Client.Assets.ItemCatalog.Length < 1000)
            throw new Exception("物品目录或三列勾选表格不完整。");
        if (((CheckBox)panel.FindChild("AutoPickup", true, false)).GetParent() != tabs.GetChild<Control>(2) ||
            ((CheckBox)panel.FindChild("AutoRecycle", true, false)).GetParent() != tabs.GetChild<Control>(4))
            throw new Exception("自动拣取应位于挂机页，自动回收应位于物品页。");
        ((CheckBox)panel.FindChild("AutoPickup", true, false)).ButtonPressed = true;
        ((CheckBox)panel.FindChild("AutoRecycle", true, false)).ButtonPressed = true;
        await ClickItemRule(panel, "检查余装", 0, true);
        await ClickItemRule(panel, "检查余装", 1, false);
        await ClickItemRule(panel, "检查余装", 2, true);
        await ClickItemRule(panel, "检查保留", 2, true);
        if (!tabs.GetChild<Control>(4).GetGlobalRect().Encloses(table.GetGlobalRect())) throw new Exception("物品表格超出背景。");
        await CombatScreenshot("settings-items");
        ((LineEdit)panel.FindChild("ItemRuleSearch", true, false)).Text = "";
        ((LineEdit)panel.FindChild("ItemRuleSearch", true, false)).EmitSignal(LineEdit.SignalName.TextChanged, "");
        await CombatScreenshot("settings-items-catalog");
        CloseSettingsPanel(panel);
        prefs = (ClientPreferences)typeof(Main).GetField("preferences", flags)!.GetValue(Client)!;
        prefs.AutoSell = false;
        var saved = JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(Environment.GetEnvironmentVariable("MIR2_CLIENT_SETTINGS")!))!;
        if (!saved.AutoSell || saved.ForItem("检查余装") != (ItemActions.Display | ItemActions.Recycle)) throw new Exception("物品三列没有独立保存。");
        stage = "remote auto recycling without NPC or full bag";
        prefs.SmartBattle = false;
        Client.SetDestination(new Point(State.User.Location.X, State.User.Location.Y - 10), true);
        await Until(() => !Moving() && State.Actors.Values.All(actor => actor.Kind != ObjectType.Merchant));
        var junk = State.User.Inventory.Single(item => item?.Info.Name == "检查余装");
        var kept = State.User.Inventory.Where(item => item?.Info.Name == "检查保留").Select(item => item.UniqueID).ToArray();
        var gold = State.User.Gold;
        if (State.User.Inventory.Count(item => item == null) <= 2) throw new Exception("回收场景必须有充足背包空位。");
        packets.Clear();
        prefs.AutoSell = true;
        await Until(() => packets.OfType<S.RecycleItemResult>().Any(packet => packet.UniqueID == junk.UniqueID && packet.Success));
        prefs = (ClientPreferences)typeof(Main).GetField("preferences", flags)!.GetValue(Client)!;
        prefs.AutoSell = false;
        var recycled = packets.OfType<S.RecycleItemResult>().Single(packet => packet.UniqueID == junk.UniqueID && packet.Success);
        if (State.User.Gold != gold + recycled.Gold || State.User.Inventory.Any(item => item?.UniqueID == junk.UniqueID) || kept.Any(id => !State.User.Inventory.Any(item => item?.UniqueID == id)))
            throw new Exception("自动回收金额、唯一编号或保留物错误。");
        var gains = packets.OfType<S.GainedGold>().Count();
        Client.Send(new C.RecycleItem { RequestId = recycled.RequestId, UniqueID = recycled.UniqueID, Count = recycled.Count });
        await Until(() => packets.OfType<S.RecycleItemResult>().Count(packet => packet.UniqueID == junk.UniqueID) == 2);
        if (State.User.Gold != gold + recycled.Gold || packets.OfType<S.GainedGold>().Count() != gains) throw new Exception("重放回收重复增加金币。");
        Client.Send(new C.RecycleItem { RequestId = recycled.RequestId + 1, UniqueID = kept[0], Count = 1 });
        await Until(() => packets.OfType<S.RecycleItemResult>().Any(packet => packet.UniqueID == kept[0] && !packet.Success));
        if (!State.User.Inventory.Any(item => item?.UniqueID == kept[0])) throw new Exception("服务端回收了禁止出售物品。");
        File.WriteAllText(Path.ChangeExtension(Environment.GetEnvironmentVariable("MIR2_CLIENT_SETTINGS")!, ".assets.json"),
            JsonSerializer.Serialize(new { State.User.Gold, Items = State.User.Inventory.Where(item => item != null).OrderBy(item => item.UniqueID).Select(item => new { item.UniqueID, item.ItemIndex, item.Count }) }));
        var pending = new C.RecycleItem { RequestId = int.MaxValue, UniqueID = kept[0], Count = 1 };
        typeof(Main).GetField("recyclePending", flags)!.SetValue(Client, pending);
        typeof(Main).GetField("recycleSentAt", flags)!.SetValue(Client, Environment.TickCount64 - 5001);
        typeof(Main).GetMethod("ProcessAutoRecycle", flags)!.Invoke(Client, [Environment.TickCount64]);
        if (!(bool)typeof(Main).GetField("recyclePaused", flags)!.GetValue(Client)! || !ReferenceEquals(pending, typeof(Main).GetField("recyclePending", flags)!.GetValue(Client)))
            throw new Exception("回收超时丢弃未决请求锁。");
        typeof(Main).GetMethod("AutoRecyclePacket", flags)!.Invoke(Client, [new S.MapChanged()]);
        if (!ReferenceEquals(pending, typeof(Main).GetField("recyclePending", flags)!.GetValue(Client))) throw new Exception("切图丢失未知回收锁。");
        typeof(Main).GetMethod("AutoRecyclePacket", flags)!.Invoke(Client, [new S.RecycleItemResult { RequestId = pending.RequestId, UniqueID = pending.UniqueID }]);
        if (typeof(Main).GetField("recyclePending", flags)!.GetValue(Client) != null) throw new Exception("迟到回收回执没有解除对应锁。");
        await VerifyHoverMagic();
        GD.Print("PASS native action/items: queued run cast/harvest, manual smart shield, 500ms moves, three checkbox columns/persistence, remote recycling with no NPC/full bag, replay/refusal asset protection, hover poison/talisman.");
        await Client.Quit();
    }

    private void SetLocalItemRule(string name, int column, bool enabled) => typeof(Main).GetMethod("SetItemRule", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Client, [name, column, enabled]);

    private async Task ClickItemRule(Control panel, string name, int column, bool enabled)
    {
        var search = (LineEdit)panel.FindChild("ItemRuleSearch", true, false); search.Text = name;
        search.EmitSignal(LineEdit.SignalName.TextChanged, name);
        var table = (Tree)panel.FindChild("ItemRuleTable", true, false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var row = table.GetRoot().GetFirstChild();
        if (row == null || row.GetText(3) != name) throw new Exception("没有显示指定物品：" + name);
        if (row.IsChecked(column) != enabled)
        {
            var point = table.GlobalPosition + table.GetItemAreaRect(row, column).GetCenter();
            await MoveMouse(point); MouseButtonAt(point, true); MouseButtonAt(point, false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (row.IsChecked(column) != enabled) throw new Exception($"物品勾选无效：{name}/{column}");
    }
}
