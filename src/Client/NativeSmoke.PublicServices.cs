using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyPublicServices()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("公共服务回归必须使用隔离fixture。");
        stage = "public services login";
        await Until(() => Client.HandshakeReady); Client.Login("NativeGM", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(role => role.Name == "NativeGM").Index });
        await Until(() => State.User != null && !Client.World.Loading && State.QuestNavigation.Count >= 2);
        var start = State.User!.Location;
        stage = "public shop original artwork and confirmed purchase";
        var icon = (OriginalButton)Client.FindChild("PublicShopButton", true, false);
        if (icon.Index != 826 || icon.Position != new Vector2(922, 654) || icon.Size != Vector2.One * ClientUi.HudActionSize || icon.TooltipText != "商城")
            throw new Exception("商城图标未保持原中心、统一尺寸或名称。");
        icon.EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => State.PublicShop != null);
        var panel = (Control)Client.FindChild("PublicShopDialog", true, false);
        var tabs = panel.FindChildren("*", "Button", true, false).OfType<Button>().Where(button => button.ThemeTypeVariation == "GameTab").ToArray();
        var rows = (VBoxContainer)Client.FindChild("PublicShopRows", true, false);
        await Until(() => rows.GetChildCount() > 0 && rows.GetChild<Button>(0).Icon != null);
        if (tabs.Length != 5 || !tabs.Select(tab => tab.Text).SequenceEqual(new[] { "药水", "卷轴", "符毒", "补给", "技能" }) || tabs.Select(tab => tab.Size.X).Distinct().Count() != 1 || panel.GetThemeStylebox("panel") is not StyleBoxTexture)
            throw new Exception("公共商店未复用原边框或分类页签不对齐。");
        var confirm = (ConfirmationDialog)Client.FindChild("PublicShopConfirmation", true, false);
        var quantity = confirm.FindChildren("*", "SpinBox", true, false).Cast<SpinBox>().Single();
        var header = rows.GetChild<Button>(0); header.ButtonPressed = true;
        LinkButton Buy(int amount) => rows.FindChildren($"PublicShopBuy{amount}", "LinkButton", true, false).OfType<LinkButton>().First();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        foreach (var amount in new[] { 1, 10, 100 })
            if (Buy(amount).Text != $"x{amount}" || Buy(amount).Underline != LinkButton.UnderlineMode.OnHover) throw new Exception("商城数量入口未使用悬停文字按钮。");
        if (Buy(10).Position.X - Buy(1).GetRect().End.X != 3 || Buy(100).Position.X - Buy(10).GetRect().End.X != 3)
            throw new Exception("商城购买数量按钮间距不是3px。");
        for (var i = 1; i < tabs.Length; i++)
            if (tabs[i].Position.X - tabs[i - 1].GetRect().End.X != ClientUi.Gap) throw new Exception("商城页签间距不是4px。");
        var menu = GameArt("菜单 Ctrl+E");
        if (menu is not MenuIconButton || menu.Index != 1960 || menu.Position != new Vector2(972, 654) || menu.Size != icon.Size)
            throw new Exception("菜单图标未保持原中心或与商城同尺寸。");
        var gold = State.User.Gold;
        Buy(100).EmitSignal(BaseButton.SignalName.Pressed);
        if (quantity.Value != 100) throw new Exception("x100未带入确认数量。");
        confirm.EmitSignal(ConfirmationDialog.SignalName.Canceled); confirm.Hide();
        if (State.User.Gold != gold) throw new Exception("取消购买改变金币。");
        Buy(10).EmitSignal(BaseButton.SignalName.Pressed);
        if (quantity.Value != 10) throw new Exception("x10未带入确认数量。");
        quantity.Value = 2;
        var offer = State.PublicShop!.Items.First(item => item.Category == 0);
        var count = State.User.Inventory.Where(item => item?.ItemIndex == offer.ItemIndex).Sum(item => item.Count);
        packets.Clear(); confirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed); confirm.EmitSignal(ConfirmationDialog.SignalName.Confirmed); confirm.Hide();
        await Until(() => packets.OfType<S.PublicShopResult>().Any(result => result.Success));
        if (State.User.Gold != gold - offer.Price * 2 || State.User.Inventory.Where(item => item?.ItemIndex == offer.ItemIndex).Sum(item => item.Count) != count + 2 ||
            packets.OfType<S.LoseGold>().Count() != 1) throw new Exception("公共商店没有只成交两件或未即时入包。");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var notices = (VBoxContainer)Client.FindChild("ItemNotices", true, false);
        var labels = notices.FindChildren("*", "Label", true, false).Cast<Label>().ToArray();
        if (labels.Length == 0 || labels.Any(label => label.HorizontalAlignment != HorizontalAlignment.Right ||
            label.GetParent<Control>().GetThemeStylebox("panel") is not StyleBoxEmpty || label.MouseFilter != Control.MouseFilterEnum.Ignore))
            throw new Exception("物品消息未右对齐、仍有背景或拦截鼠标。");
        await CombatScreenshot("public-shop");
        tabs[2].EmitSignal(BaseButton.SignalName.Pressed);
        foreach (var group in rows.GetChildren().OfType<Button>()) group.ButtonPressed = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!rows.FindChildren("PublicShopItem", "Button", true, false).Cast<Button>().Any(button => button.Text.Contains("500次/包")))
            throw new Exception("毒符没有显示分规格次数。");
        await CombatScreenshot("public-shop-amulets");
        tabs[3].EmitSignal(BaseButton.SignalName.Pressed);
        rows.GetChild<Button>(0).ButtonPressed = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (rows.FindChildren("PublicShopItem", "Button", true, false).Cast<Button>().Count(button => button.Text.Contains("魔血石")) != 3)
            throw new Exception("商城没有三规格魔血石。");
        if (rows.GetChildren().OfType<Button>().Any(button => button.Text.Contains("沃玛角")))
            throw new Exception("单规格沃玛角仍要求展开二级。");
        if (!rows.FindChildren("PublicShopItem", "Button", true, false).Cast<Button>()
            .Any(button => button.IsVisibleInTree() && button.Text.Contains($"沃玛角\n{1_000_000:N0} 金币")))
            throw new Exception("补给页未显示100万金币的沃玛角。");
        await Until(() => rows.GetChild<Button>(0).Icon != null && rows.FindChildren("PublicShopItem", "Button", true, false).Cast<Button>().Where(button => button.Text.Contains("魔血石")).All(button => button.Icon != null));
        await CombatScreenshot("public-shop-stones");
        icon.EmitSignal(BaseButton.SignalName.Pressed);
        stage = "game menu list icon and close control";
        menu.EmitSignal(BaseButton.SignalName.Pressed);
        var popup = (PopupMenu)Client.FindChild("GameMenu", true, false);
        var menuClose = (OriginalButton)popup.FindChild("GameMenuClose", true, false);
        await Until(() => popup.Visible && menuClose.Position == new Vector2(popup.Size.X - 28, 4));
        await Until(() => Client.Assets.Sprite("Prguse2", 360) != null);
        await Screenshot("game-menu-close");
        var closePoint = (Vector2)popup.Position + menuClose.GetGlobalRect().GetCenter();
        await MoveMouse(closePoint);
        MouseButtonAt(closePoint, true); MouseButtonAt(closePoint, false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (popup.Visible) throw new Exception("游戏菜单右上角关闭按钮无效。");
        stage = "shared game tabs and actual mouse switches";
        VerifySharedTabSpacing();
        var bag = GameArt("关闭背包").GetParent<Control>();
        bag.Show();
        await Until(() => bag.GetChildren().OfType<TextureRect>().Single().Texture != null);
        if (State.User.Inventory.Length != Globals.BaseInventorySize || bag.FindChildren("InventorySlot*", "Button", true, false).Count != ItemPresentation.BagSlots(State.User.Inventory).Length)
            throw new Exception("客户端背包格数未和服务端的80格一致。");
        await CombatScreenshot("inventory-80"); bag.Hide();
        var equipment = GameArt("关闭角色").GetParent<Control>();
        var roleTabs = equipment.GetChildren().OfType<OriginalButton>().Where(button => button.ToggleMode).OrderBy(button => button.Position.X).ToArray();
        roleTabs[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Until(() => equipment.GetChildren().OfType<TextureRect>().Single().Texture != null);
        CheckOriginalTabArtwork(equipment, roleTabs[0]);
        await VerifyTabMouseStates(roleTabs);
        OpenSkills(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await VerifySkillRowAlignment(); GameArt("关闭角色").EmitSignal(BaseButton.SignalName.Pressed);
        var settings = SettingsPanel();
        var settingsTabs = settings.GetChildren().OfType<TabContainer>().Single();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var bar = settingsTabs.GetTabBar();
        for (var i = 0; i < bar.TabCount; i++)
        {
            settingsTabs.CurrentTab = i;
            if (i > 0 && bar.GetTabRect(i).Position.X - bar.GetTabRect(i - 1).End.X != ClientUi.Gap)
                throw new Exception("设置选项卡间距不统一。");
        }
        await CombatScreenshot("shared-settings-tabs"); CloseSettingsPanel(settings);
        await VerifyShinsuFrames();
        stage = "per-monster experience from original gain packet";
        packets.Clear(); Client.SelectTarget(State.Actors.Values.First(actor => actor.Kind == ObjectType.Monster && actor.Name == "NativeTarget" && !actor.Dead).Id);
        await Until(() => packets.OfType<S.GainExperience>().Any(packet => packet.Amount > 0));
        var experience = packets.OfType<S.GainExperience>().First(packet => packet.Amount > 0).Amount;
        var messages = Client.FindChildren("*", "RichTextLabel", true, false).Cast<RichTextLabel>();
        if (!messages.Any(label => label.GetParsedText().Contains($"获得经验 +{experience:N0}"))) throw new Exception("真实击杀经验没有显示在聊天框。");
        Client.SetDestination(State.User.Location);
        stage = "hovered quest navigation and normal walking exit";
        var tracker = (RichTextLabel)Client.FindChild("QuestTracker", true, false);
        await Until(() => tracker.Text.Contains("url=2:1"));
        tracker.EmitSignal(RichTextLabel.SignalName.MetaHoverStarted, "2:1");
        if (!tracker.Text.Contains("[color=#ffffff][url=2:1][u]")) throw new Exception("任务链接悬停没有变色及下划线。");
        await CombatScreenshot("quest-navigation-hover");
        tracker.EmitSignal(RichTextLabel.SignalName.MetaHoverEnded, "2:1");
        tracker.EmitSignal(RichTextLabel.SignalName.MetaClicked, "2:1");
        await Until(() => State.Map?.MapIndex == 2 && !Client.World.Loading);
        await Until(() => State.QuestNavigation.GetValueOrDefault(2)?.OriginMap == 2);
        await Until(() => State.User.Location == State.QuestNavigation[2].Targets.Single(target => target.Id == 1).NextLocation);
        tracker.EmitSignal(RichTextLabel.SignalName.MetaClicked, "2:0");
        await Until(() => State.Map?.MapIndex == 1 && !Client.World.Loading);
        await Until(() => packets.OfType<S.NPCResponse>().Any());
        if (State.User.Gold != gold - offer.Price * 2 || !State.Quests.ContainsKey(2)) throw new Exception("任务导航改变金币或伪造完成任务。");
        await CombatScreenshot("quest-navigation-npc");
        GD.Print("PASS native public services: original mall/menu icons, five spaced tabs and header/footer, grouped variants, x1/x10/x100 editable confirmation, cancel-safe instant purchase, transparent right-aligned notices, real kill XP chat, hover links, cross-map monster navigation and return to NPC via walking exits.");
        await Client.Quit();
    }
}
