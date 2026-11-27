using System.Reflection;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifySharedHeroMaterials()
    {
        stage = "hero original shared-bag poison and amulet consumption";
        Client.SelectTarget(0);
        var hero = State.Hero!; var before = hero.MP;
        var materials = State.User!.Inventory.Where(item => item?.Info.Name.StartsWith("共用试炼毒符", StringComparison.Ordinal) == true).ToDictionary(item => item.Info.Shape);
        if (hero.Class != MirClass.Taoist || hero.Equipment.Any(item => item?.Info.Type == ItemType.Amulet)) throw new Exception("共用毒符夹具不能预先给英雄装备耗材。");
        packets.RemoveAll(packet => packet is S.ObjectMagic or S.DeleteItem);
        Client.Send(new C.ChangePMode { Mode = PetMode.Both });
        await Until(() => materials[1].Count < 2 && materials[2].Count < 2 && materials[0].Count < 3 && hero.MP < before);
        Client.Send(new C.ChangePMode { Mode = PetMode.MoveOnly });
        await Until(() => State.CurrentPetMode == PetMode.MoveOnly);
        if (!packets.OfType<S.ObjectMagic>().Any(packet => packet.ObjectID == hero.ObjectID && packet.Spell == Spell.Poisoning && packet.Cast) ||
            !packets.OfType<S.ObjectMagic>().Any(packet => packet.ObjectID == hero.ObjectID && packet.Spell == Spell.SoulFireBall && packet.Cast) ||
            materials.Values.Any(item => !packets.OfType<S.DeleteItem>().Any(packet => packet.UniqueID == item.UniqueID && packet.Count > 0)))
            throw new Exception("英雄没有通过原施法/物品回执消耗共用背包红毒、绿毒和护符。");
        await Screenshot("hero-shared-materials");
        GD.Print("PASS native Taoist hero: no equipped amulet/poison, original automatic Poisoning red+green and SoulFireBall consume exact owner bag IDs/counts and real hero MP.");
    }

    private async Task VerifyUnidentifiedRecycling()
    {
        stage = "explicit unidentified recycling and ordinary price";
        var item = State.User!.Inventory.Single(item => item?.Info.Name == "回收试炼剑");
        var prefs = (ClientPreferences)typeof(Main).GetField("preferences", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Client)!;
        prefs.AutoSell = true; prefs.ItemRules.Remove(item.Info.FriendlyName);
        await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
        if (!State.User.Inventory.Contains(item)) throw new Exception("未勾选就回收了未鉴定装备。");
        var gold = State.User.Gold; prefs.ItemRules[item.Info.FriendlyName] = ItemActions.Display | ItemActions.Recycle;
        packets.RemoveAll(packet => packet is S.RecycleItemResult);
        var result = await Packet<S.RecycleItemResult>();
        await Until(() => State.User.Inventory.All(value => value?.UniqueID != item.UniqueID));
        if (!result.Success || result.UniqueID != item.UniqueID || result.Gold != item.Price() / 2 || State.User.Gold != gold + result.Gold || item.Identified)
            throw new Exception("明确勾选的未鉴定装备未按普通属性半价回收。");
        Client.Send(new C.RecycleItem { RequestId = result.RequestId, UniqueID = result.UniqueID, Count = result.Count });
        await Packet<S.RecycleItemResult>();
        if (State.User.Gold != gold + result.Gold) throw new Exception("未鉴定回收重放重复增加金币。");
        prefs.AutoSell = false;
        GD.Print("PASS native unidentified recycling: unchecked retained, explicit item rule sends original request, base-value half price, hidden Supreme unrevealed, replay does not pay twice.");
    }

    private async Task VerifyPlayerHoverName()
    {
        stage = "hover another player/hero name";
        GameArt("关闭背包").GetParent<Control>().Hide(); GameArt("关闭角色").GetParent<Control>().Hide();
        await Until(() => State.Actors.ContainsKey(State.Hero!.ObjectID));
        var actor = State.Actors[State.Hero!.ObjectID]; Client.World.Selected = 0;
        Client.World.UpdateHover(Vector2.Zero, true);
        if (Client.World.NameVisible(actor)) throw new Exception("其他角色姓名在未悬停/选中时被强制常显。");
        var point = Client.World.Screen(actor.Location) + new Vector2(24, -58); await MoveMouse(point);
        Client.World.UpdateHover(point, false);
        if (Client.World.Hovered != actor.Id || !Client.World.NameVisible(actor)) throw new Exception($"鼠标悬停其他Player对象时未显示名称：目标={actor.Id}，实际={Client.World.ActorAt(point)?.Name}，位置={point}。");
        await Screenshot("hover-other-character-name");
        Client.World.UpdateHover(point, true);
        if (Client.World.NameVisible(actor)) throw new Exception("UI控件上悬停穿透显示角色名字。");
        GD.Print("PASS native hover name: other Player-kind actor revealed under cursor, original name colour/style, UI blocks hover and leaving hides again.");
    }

    private async Task VerifyLiveEquipmentTooltip()
    {
        stage = "live equipment tooltip after original wear receipts";
        var original = State.User!.Equipment[0];
        var candidate = State.User.Inventory.First(item => item?.Info.Type == ItemType.Weapon && item.EquipmentQuality == EquipmentGrade.Legacy);
        var tooltip = Client.BuildItemTooltip(candidate);
        var panel = new PanelContainer { Position = new Vector2(100, 150) }; panel.AddThemeStyleboxOverride("panel", Client.Theme.GetStylebox("panel", "GameItemTooltip"));
        panel.AddChild(tooltip); Client.AddChild(panel);
        try
        {
            await Until(() => tooltip.FindChild("EquippedComparison", true, false) != null);
            Client.UseItem(candidate.UniqueID); await Until(() => State.User.Equipment[0]?.UniqueID == candidate.UniqueID);
            await Until(() => tooltip.FindChild("EquippedComparison", true, false) == null);
            Client.UseItem(original.UniqueID); await Until(() => State.User.Equipment[0]?.UniqueID == original.UniqueID);
            await Until(() => tooltip.FindChild("EquippedComparison", true, false) != null);
            var columns = (Control)tooltip.FindChild("ItemComparison", true, false);
            if (!columns.GetChildren().OfType<ItemTooltipSeparator>().Any(line => line.Vertical) ||
                !columns.GetChildren().OfType<Control>().SelectMany(column => column.GetChildren()).OfType<ItemTooltipSeparator>().Any(line => !line.Vertical))
                throw new Exception("装备对比仍使用实线分隔。");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var label = (RichTextLabel)tooltip.FindChild("ItemDetails", true, false);
            using var screen = GetViewport().GetTexture().GetImage(); var scale = screen.GetWidth() / 1024f; var whites = 0;
            for (var y = (int)((label.GlobalPosition.Y + label.GetLineOffset(3)) * scale); y < (label.GlobalPosition.Y + label.GetLineOffset(4)) * scale; y++)
            for (var x = (int)(label.GlobalPosition.X * scale); x < (label.GlobalPosition.X + label.Size.X) * scale; x++)
            { var pixel = screen.GetPixel(x, y); if (pixel.R > .95f && pixel.G > .95f && pixel.B > .95f) whites++; }
            if (whites == 0) throw new Exception("普通装备属性未使用默认白色。");
            await Screenshot("live-white-dashed-comparison");
        }
        finally { panel.QueueFree(); }
        GD.Print("PASS native live tooltip: same open control removes comparison after equip and restores after replacement; original TCP receipts, white base pixels and faint dashed vertical/horizontal separators.");
    }
}
