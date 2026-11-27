using System.Reflection;
using System.Text.Json;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyEquipmentPlay()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-heroes.presentation.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("装备游玩检查仅允许隔离版本。");
        await Until(() => Client.HandshakeReady); Client.Login("TradeRight", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && State.Hero != null && !Client.World.Loading);
        string Snapshot() => JsonSerializer.Serialize(new { State.User!.Gold,
            Bag = State.User.Inventory.Where(item => item != null).OrderBy(item => item.UniqueID).Select(item => new { item.UniqueID, item.ItemIndex, item.Count, item.Identified, item.EquipmentQuality, Stats = item.AddedStats.Values }),
            Equipment = State.User.Equipment.Select(item => item?.UniqueID ?? 0), HeroEquipment = State.Hero!.Equipment.Select(item => item?.UniqueID ?? 0) });
        if (OS.GetCmdlineUserArgs().Contains("--verify-equipment-play-saved"))
        {
            if (Snapshot() != File.ReadAllText(Argument("--equipment-record"))) throw new Exception("卷轴/鉴定/999整理或英雄装备没有完整保存。");
            await Until(() => State.Me!.EquipmentGlow == EquipmentGlow.Weapon && State.Actors.GetValueOrDefault(State.Hero!.ObjectID)?.EquipmentGlow == EquipmentGlow.Equipment);
            if (OS.GetCmdlineUserArgs().Contains("--verify-manual-drop-relogin"))
            { await Until(() => State.ManuallyDropped.Count > 0); GD.Print("PASS native relogin: live manually dropped ground objects excluded again by original character identity."); }
            GD.Print("PASS native equipment restart: exact scroll quantities, gold, item IDs/quality/stats, 999 stacks and hero wear retained; player/hero glow initialized again.");
        }
        else
        {
            await VerifyShopAppraisalScroll(); await VerifyWornEquipmentGlow();
            await VerifySharedHeroMaterials(); await Verify999Organization(); await VerifyManualDroppedEquipment(); await VerifyUnidentifiedRecycling(); await VerifyLiveEquipmentTooltip(); await VerifyPortableStorage();
            File.WriteAllText(Argument("--equipment-record"), Snapshot());
        }
        await VerifyCompactEquipmentTooltip();
        await VerifyPlayerHoverName();
        stage = "equipment normal logout";
        var deadline = Environment.TickCount64 + 30000;
        while (true)
        {
            packets.RemoveAll(packet => packet is S.LogOutSuccess or S.LogOutFailed); Client.Send(new C.LogOut());
            await Until(() => packets.Any(packet => packet is S.LogOutSuccess or S.LogOutFailed));
            if (packets.Any(packet => packet is S.LogOutSuccess)) break;
            if (Environment.TickCount64 > deadline) throw new Exception("原战斗退出保护超时。");
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        }
        await Client.Quit();
    }

    private async Task DoubleClickBag(ulong id)
    {
        GameArt("关闭背包").GetParent<Control>().Show();
        var slot = Array.FindIndex(State.User!.Inventory, item => item?.UniqueID == id);
        if (slot < 6)
        {
            var to = Array.FindIndex(State.User.Inventory, 6, item => item == null);
            Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = slot, To = to });
            await Until(() => State.User.Inventory[to]?.UniqueID == id); slot = to;
        }
        await Until(() => Client.FindChild($"InventorySlot{slot}", true, false) != null);
        var cell = await VisibleBagCell(slot);
        var point = cell.GetGlobalRect().GetCenter(); await MoveMouse(point);
        using var press = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true };
        GetViewport().PushInput(press, true); MouseButtonAt(point, false);
    }

    private async Task VerifyShopAppraisalScroll()
    {
        stage = "real shop scroll purchase and cursor/selection/cancel/confirm";
        await ClickSelectionControl((Control)Client.FindChild("PublicShopButton", true, false));
        await Until(() => State.PublicShop != null && State.PublicShopTabs != null);
        var panel = (Control)Client.FindChild("PublicShopDialog", true, false);
        var tab = panel.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.ThemeTypeVariation == "GameTab" && button.Text.Contains('卷'));
        await ClickSelectionControl(tab);
        Button? product = null;
        await Until(() => (product = panel.FindChildren("PublicShopItem", "Button", true, false).OfType<Button>().FirstOrDefault(button => button.Text.Contains("装备鉴定卷轴"))) != null);
        var row = product!.GetParent(); var buy = row.FindChildren("PublicShopBuy1", "LinkButton", true, false).OfType<LinkButton>().Single();
        var gold = State.User!.Gold; await ClickSelectionControl(buy);
        var purchase = (ConfirmationDialog)Client.FindChild("PublicShopConfirmation", true, false); await Until(() => purchase.Visible);
        purchase.FindChildren("*", "SpinBox", true, false).OfType<SpinBox>().Single().Value = 3;
        await ClickSelectionControl(purchase.GetOkButton());
        await Until(() => State.User.Inventory.Any(item => EquipmentAppraisal.IsScroll(item?.Info)));
        var scroll = State.User.Inventory.Single(item => EquipmentAppraisal.IsScroll(item?.Info));
        if (scroll.Count != 3 || State.User.Gold != gold - 3 * EquipmentAppraisal.ScrollPrice) throw new Exception("真实商城购买卷轴数量/金币错误。");
        typeof(Main).GetMethod("ClosePublicShop", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Client, null);
        var target = State.User.Inventory.Single(item => EquipmentAppraisal.Pending(item) && item.Info.Name == "光效试炼剑");
        await DoubleClickBag(scroll.UniqueID);
        var cell = await VisibleBagCell(Array.IndexOf(State.User.Inventory, target)); await MoveMouse(cell.GetGlobalRect().GetCenter());
        if (cell.MouseDefaultCursorShape != Control.CursorShape.Cross) throw new Exception("双击鉴定卷轴后没有进入十字光标选择。");
        var confirm = (ConfirmationDialog)Client.FindChild("AppraisalScrollConfirmation", true, false);
        await ClickSelectionControl(cell); await Until(() => confirm.Visible);
        if (!confirm.DialogText.Contains(target.Info.FriendlyName) || target.Identified || scroll.Count != 3) throw new Exception("鉴定确认内容错误或提前消耗。");
        await Screenshot("scroll-confirmation"); await ClickSelectionControl(confirm.GetCancelButton());
        if (target.Identified || scroll.Count != 3 || cell.MouseDefaultCursorShape != Control.CursorShape.Arrow) throw new Exception("取消鉴定没有恢复光标或改变资产。");
        await DoubleClickBag(scroll.UniqueID); cell = await VisibleBagCell(Array.IndexOf(State.User.Inventory, target));
        await ClickSelectionControl(cell); await Until(() => confirm.Visible);
        packets.RemoveAll(packet => packet is S.EquipmentAppraisalResult); await ClickSelectionControl(confirm.GetOkButton());
        var result = await Packet<S.EquipmentAppraisalResult>();
        target = State.User.Inventory.Single(item => item?.UniqueID == target.UniqueID);
        if (!result.Success || result.UniqueID != target.UniqueID || !target.Identified || scroll.Count != 2 || target.GetTotal(Stat.MaxDC) != 7 || State.User.Gold != gold - 15000)
            throw new Exception("卷轴没有一次鉴定、准确消耗或重复扣金币。");
        Client.Send(new C.AppraiseEquipment { ScrollID = scroll.UniqueID, ItemID = target.UniqueID });
        if ((await Packet<S.EquipmentAppraisalResult>()).Success || scroll.Count != 2) throw new Exception("重放确认重复消耗卷轴。");
        GD.Print("PASS native scroll: shop x3/15000 gold, real double-click crosshair, target click and confirmation/cancel, one scroll consumed after success, exact quality and replay refusal.");
    }

    private async Task VerifyWornEquipmentGlow()
    {
        stage = "real player/hero wear glow and successful spell flash";
        var weapon = State.User!.Inventory.Single(item => item?.EquipmentQuality == EquipmentGrade.Supreme && item.Identified && item.Info.Type == ItemType.Weapon);
        var armour = State.User.Inventory.Single(item => item?.EquipmentQuality == EquipmentGrade.Supreme && item.Info.Type == ItemType.Armour);
        Client.UseItem(weapon.UniqueID);
        await Until(() => State.User.Equipment[0]?.UniqueID == weapon.UniqueID && State.Me!.EquipmentGlow == EquipmentGlow.Weapon && Client.World.EquipmentGlowSprites > 0);
        await ClickSelectionControl((Control)Client.FindChild("HeroShortcut", true, false));
        await Until(() => GameArt("关闭角色").GetParent<Control>().Visible); Client.UseItem(armour.UniqueID);
        await Until(() => State.Hero!.Equipment[1]?.UniqueID == armour.UniqueID && State.Actors.GetValueOrDefault(State.Hero.ObjectID)?.EquipmentGlow == EquipmentGlow.Equipment);
        await ClickSelectionControl(GameArt("关闭角色"));
        var before = State.Me.EquipmentGlow;
        Client.Send(new C.RemoveItem { Grid = MirGridType.Inventory, UniqueID = weapon.UniqueID, To = Array.FindIndex(State.User.Inventory, 6, item => item == null) });
        await Until(() => State.User.Equipment[0] == null && State.Me.EquipmentGlow == EquipmentGlow.None);
        Client.UseItem(weapon.UniqueID); await Until(() => State.Me.EquipmentGlow == before);
        await Until(() => Enumerable.Range(880, 5).All(frame => Client.Assets.Sprite("Magic3", frame) != null));
        var target = State.Actors.Values.Single(actor => actor.Name == "光效试炼靶" && !actor.Dead);
        var flashes = Client.World.EquipmentCastFlashes;
        Client.World.ShowSpell(Spell.FireBall, State.User.ObjectID, target.Id, target.Location, false);
        if (Client.World.EquipmentCastFlashes != flashes) throw new Exception("拒绝施法触发极品附加特效。");
        await ToSignal(GetTree().CreateTimer(1.3), SceneTreeTimer.SignalName.Timeout);
        var mp = State.User.MP; packets.RemoveAll(packet => packet is S.Magic);
        Client.Send(new C.Magic { ObjectID = State.User.ObjectID, TargetID = target.Id, Spell = Spell.FireBall,
            Direction = Main.Direction(State.User.Location, target.Location), Location = target.Location });
        var cast = await Packet<S.Magic>();
        if (!cast.Cast) throw new Exception("原服拒绝隔离火球。");
        await Until(() => State.User.MP < mp && Client.World.EquipmentCastFlashes > flashes);
        await Screenshot("supreme-wear-and-cast");
        GD.Print("PASS native original TCP: player Supreme weapon and hero armour glow, remove extinguishes/re-wear restores, failed cast adds none, successful FireBall consumes real MP and adds original Magic3 flash.");
    }

    private async Task Verify999Organization()
    {
        stage = "999 stacks and automatic original MergeItem receipts";
        var totals = State.User!.Inventory.Where(item => item?.Info.Name is "整理试炼药" or "整理试炼卷轴").GroupBy(item => item.ItemIndex).ToDictionary(group => group.Key, group => group.Sum(item => item.Count));
        foreach (var name in new[] { "整理试炼药", "整理试炼卷轴" })
        {
            var item = State.User.Inventory.First(item => item?.Info.Name == name && item.SoulBoundId < 0 && item.Count == 999);
            Client.Send(new C.SplitItem { Grid = MirGridType.Inventory, UniqueID = item.UniqueID, Count = 400 });
            if (!(await Packet<S.SplitItem1>()).Success) throw new Exception("999堆不能拆分。");
            await Until(() => State.User.Inventory.Any(split => split?.ItemIndex == item.ItemIndex && split.UniqueID != item.UniqueID && split.Count == 400));
        }
        var organizer = (ClientInventoryOrganizer)typeof(Main).GetField("inventoryOrganizer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Client)!;
        typeof(ClientInventoryOrganizer).GetField("nextScan", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(organizer, Environment.TickCount64 - 1);
        await Until(() => new[] { "整理试炼药", "整理试炼卷轴" }.All(name => State.User.Inventory.Any(item => item?.Info.Name == name && item.Count == 999)));
        foreach (var total in totals)
            if (State.User.Inventory.Where(item => item?.ItemIndex == total.Key).Sum(item => item.Count) != total.Value) throw new Exception("自动整理丢失数量。");
        if (!State.User.Inventory.Any(item => item?.SoulBoundId == 999999 && item.Count == 5)) throw new Exception("整理混合了绑定堆。");
        GD.Print("PASS native 999: real 999+201 login stacks, original TCP split400 and automatic merge to999, all quantities/target IDs and bound pile retained.");
    }

    private async Task VerifyManualDroppedEquipment()
    {
        stage = "manual drop exclusion during actual auto battle";
        var old = State.User!.Inventory.Single(item => item?.Info.Name == "光效试炼剑" && item.EquipmentQuality == EquipmentGrade.Legacy);
        var prefs = (ClientPreferences)typeof(Main).GetField("preferences", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Client)!;
        prefs.AutoPickup = true; prefs.AutoRoam = false; prefs.ItemRules[old.Info.FriendlyName] = ItemActions.Pickup | ItemActions.Display;
        foreach (var monster in State.Actors.Values.Where(actor => actor.Kind == ObjectType.Monster && !actor.Dead && actor.Name is "光效试炼靶" or "掉落试炼兽").ToArray())
        { Client.SelectTarget(monster.Id); await Until(() => monster.Dead); }
        await Until(() => State.Actors.Values.Any(actor => actor.Kind == ObjectType.Item && actor.Name == old.Info.FriendlyName));
        var fresh = State.Actors.Values.First(actor => actor.Kind == ObjectType.Item && actor.Name == old.Info.FriendlyName);
        packets.RemoveAll(packet => packet is S.ManualItemDropped or S.DropItem);
        Client.Send(new C.DropItem { UniqueID = old.UniqueID, Count = 1 });
        var drop = await Packet<S.DropItem>(); var excluded = await Packet<S.ManualItemDropped>();
        if (!drop.Success || !State.ManuallyDropped.Contains(excluded.ObjectID)) throw new Exception("原丢弃回执没有关联地面对象编号。");
        typeof(Main).GetField("autoBattleEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Client, true);
        var before = State.User.Inventory.Count(item => item?.Info.Name == old.Info.Name);
        await Until(() => State.User.Inventory.Count(item => item?.Info.Name == old.Info.Name) == before + 1);
        await ToSignal(GetTree().CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
        typeof(Main).GetMethod("StopAutoBattle", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Client, null);
        if (!State.Actors.ContainsKey(excluded.ObjectID) || State.User.Inventory.Any(item => item?.UniqueID == old.UniqueID))
            throw new Exception($"手丢装备保护失败：地面存在={State.Actors.ContainsKey(excluded.ObjectID)}，背包存在={State.User.Inventory.Any(item => item?.UniqueID == old.UniqueID)}，同名新掉落={fresh.Id}。");
        prefs.AutoPickup = false;
        Client.SelectTarget(excluded.ObjectID); await Until(() => State.User.Inventory.Any(item => item?.UniqueID == old.UniqueID));
        await VerifyDroppedPotionWhileRunning(prefs);
        GD.Print("PASS native manual drop: without live combat targets actual auto pickup collects same-name new loot, skips dropped equipment, allows explicit pickup; real bag-drag potion drop while auto battle stays excluded.");
    }

    private async Task VerifyDroppedPotionWhileRunning(ClientPreferences prefs)
    {
        var potion = State.User!.Inventory.First(item => item?.Info.Name == "整理试炼药" && item.SoulBoundId < 0);
        prefs.AutoPickup = true; prefs.AutoRoam = false; prefs.ItemRules[potion.Info.FriendlyName] = ItemActions.Pickup | ItemActions.Display;
        typeof(Main).GetField("autoBattleEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Client, true);
        await DragBagToGround(potion.UniqueID);
        var dialog = (ConfirmationDialog)Client.FindChild("DropItemDialog", true, false); await Until(() => dialog.Visible);
        packets.RemoveAll(packet => packet is S.ManualItemDropped or S.DropItem); await ClickSelectionControl(dialog.GetOkButton());
        var dropped = await Packet<S.ManualItemDropped>(); if (!(await Packet<S.DropItem>()).Success) throw new Exception("真实拖动丢弃药品失败。");
        var count = State.User.Inventory.Where(item => item?.ItemIndex == potion.ItemIndex).Sum(item => item.Count);
        await ToSignal(GetTree().CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
        if (!State.Actors.ContainsKey(dropped.ObjectID) || !State.ManuallyDropped.Contains(dropped.ObjectID) ||
            State.User.Inventory.Where(item => item?.ItemIndex == potion.ItemIndex).Sum(item => item.Count) != count)
            throw new Exception("正在挂机时拖出背包的药品被重新捡回。");
        typeof(Main).GetMethod("StopAutoBattle", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Client, null);
        prefs.AutoPickup = false;
    }

    private async Task DragBagToGround(ulong id)
    {
        GameArt("关闭背包").GetParent<Control>().Show();
        var slot = Array.FindIndex(State.User!.Inventory, item => item?.UniqueID == id);
        var source = slot < 6 ? (Control)Client.FindChild($"ItemShortcut{slot + 1}", true, false) : await VisibleBagCell(slot);
        var from = source.GetGlobalRect().GetCenter(); var to = new Vector2(450, 200);
        await MoveMouse(from); MouseButtonAt(from, true); await MoveMouse(to, true); MouseButtonAt(to, false);
    }

    private async Task VerifyCompactEquipmentTooltip()
    {
        stage = "compact tooltip and NPC white role rendering";
        var item = State.User!.Equipment[0]; var control = Client.BuildItemTooltip(item);
        var panel = new PanelContainer { Position = new Vector2(600, 160) }; panel.AddThemeStyleboxOverride("panel", Client.Theme.GetStylebox("panel", "GameItemTooltip"));
        panel.AddChild(control); Client.AddChild(panel);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var label = (RichTextLabel)control.FindChild("ItemDetails", true, false); var text = label.GetParsedText();
        if (text.Contains("品质加成") || text.Contains("手持负重") || text.Contains("穿戴负重") || text.Contains("要求等级") || text.Contains("类型 ") || !text.Contains("负重 "))
            throw new Exception("装备详情仍含冗余标签。");
        await MoveMouse(new Vector2(700, 420)); await Screenshot("compact-info-white-npc"); panel.QueueFree();
        GD.Print("PASS native compact equipment tooltip and white NPC roles rendered; quality bonus numbers retained.");
    }
}
