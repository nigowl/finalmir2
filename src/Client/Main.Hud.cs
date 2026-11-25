using Crystal.Native;
using Godot;
using C = ClientPackets;
using GColor = Godot.Color;

public partial class Main
{
    private string lastResourceError = "";
    private PopupMenu gameMenu = null!;
    private OriginalButton gameMenuButton = null!;
    private int equipmentPage;
    private readonly List<OriginalButton> equipmentTabs = new();
    private Label bagWeight = null!, bagFree = null!;
    private Label hudWeight = null!, hudFree = null!;
    private Label attributesRight = null!, attributeExperience = null!;

    private void BuildHud()
    {
        Picture(game, "Prguse", 1, 0, 616).Name = "HudBackground";
        game.AddChild(new OriginalArt { Assets = Assets, State = State });
        var level = Text(game, "", 5, 724, 90); level.Name = "PlayerLevel"; level.AddThemeFontSizeOverride("font_size", 10); level.Size = new Vector2(90, 16);
        var name = Text(game, "", 6, 736, 90); name.Name = "PlayerName"; name.HorizontalAlignment = HorizontalAlignment.Center; name.Size = new Vector2(90, 16);
        name.AddThemeFontSizeOverride("font_size", 10);
        gold = Text(game, "", 919, 735, 99); gold.Size = new Vector2(99, 14); gold.AddThemeFontSizeOverride("font_size", 10);
        hudWeight = Text(game, "", 919, 717, 68); hudWeight.Name = "HudWeight"; hudWeight.Size = new Vector2(68, 14);
        hudFree = Text(game, "", 994, 717, 26); hudFree.Name = "HudFree"; hudFree.Size = new Vector2(26, 14);
        foreach (var label in new[] { gold, hudWeight, hudFree }) { label.VerticalAlignment = VerticalAlignment.Center; label.ClipText = true; label.AddThemeFontSizeOverride("font_size", 10); }
        location = Text(game, "", 7, 5, 830); location.Name = "WorldStatus"; location.Size = new Vector2(830, 22);
        location.AddThemeFontSizeOverride("font_size", 11); location.ClipText = true;
        location.AddThemeColorOverride("font_shadow_color", Colors.Black); location.AddThemeConstantOverride("shadow_offset_x", 1); location.AddThemeConstantOverride("shadow_offset_y", 1);
        chatFrame = (Panel)Panel(game, 230, 668, 629, 68); chatFrame.Name = "ChatFrame";
        messages = new RichTextLabel { Name = "ChatMessages", Position = new Vector2(234, 671), Size = new Vector2(621, 42), ScrollFollowing = true, MouseFilter = MouseFilterEnum.Stop };
        messages.AddThemeFontSizeOverride("normal_font_size", ClientUi.Small);
        game.AddChild(messages);
        chat = Edit(game, 231, 716, 627, "聊天：Enter 发送，Esc 返回游戏"); chat.MaxLength = Globals.MaxChatLength;
        var chatStyle = new StyleBoxFlat { BgColor = new GColor(.08f, .07f, .04f, .9f), ContentMarginLeft = 3, ContentMarginRight = 3 };
        foreach (var state in new[] { "normal", "focus", "read_only" }) chat.AddThemeStyleboxOverride(state, chatStyle);
        chat.AddThemeFontSizeOverride("font_size", 10); chat.Size = new Vector2(627, 19);
        chat.GuiInput += input => { if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) { chat.ReleaseFocus(); chat.AcceptEvent(); } };
        chat.TextSubmitted += SubmitChat;
        BuildChatChannels();
        BuildChatResize();
        quickBars = new Control { Name = "QuickBars", Position = new Vector2(0, -24), Size = new Vector2(1024, 768), MouseFilter = MouseFilterEnum.Ignore }; game.AddChild(quickBars);
        BuildSkillBar();
        ArtButton(game, "Prguse", 1903, 928, 692, "背包 I", () => inventory.Visible = !inventory.Visible);
        ArtButton(game, "Prguse", 1900, 905, 692, "角色 C", () => ToggleEquipmentPage(0));
        ArtButton(game, "Prguse", 1906, 951, 692, "技能 K", () => ToggleEquipmentPage(3));
        BuildGameMenu();
        BuildHudShortcuts();
        MiniMap = new GameMiniMap { Assets = Assets, State = State };
        game.AddChild(MiniMap);
        MiniMap.SetExpanded(false);
        BuildItemNotices();
        MiniMap.TeleportRequested += point =>
        {
            if (ConfirmGroundMove(point, false, true)) return;
            SendMapTeleport(point);
        };
        MiniMap.DestinationSelected += point =>
        {
            if (World.Loading || World.PathTo(point, ignoreActors: true).Length <= 1) { Log("目标不可通行或无法到达，请选择其他坐标。"); return; }
            if (ConfirmGroundMove(point, true)) { MiniMap.SetExpanded(false); return; }
            SetDestination(point, true); MiniMap.SetExpanded(false);
            Log($"自动寻路至 {point.X},{point.Y}，Esc 取消。");
        };
    }

    private void BuildEquipment()
    {
        equipment = Panel(game, 760, 35, 264, 380); equipment.Visible = false; equipment.ClipContents = true;
        Picture(equipment, "Title", 504, 0, 0, nativeFrame: true);
        paperdoll = new OriginalArt { Assets = Assets, State = State, Page = "equipment" }; equipment.AddChild(paperdoll);
        equipmentName = WindowTitle(equipment, "", 208); equipmentName.Position = new Vector2(28, 4); equipmentName.HorizontalAlignment = HorizontalAlignment.Center;
        ArtButton(equipment, "Prguse2", 360, 241, 3, "关闭角色", CloseEquipment);
        equipmentSlots = new Control { Size = new Vector2(264, 380), MouseFilter = MouseFilterEnum.Ignore }; equipment.AddChild(equipmentSlots);
        Vector2[] positions = [new(123,7),new(163,7),new(203,7),new(203,134),new(203,98),new(8,170),new(203,170),new(8,206),new(203,206),new(8,242),new(48,242),new(88,242),new(128,242),new(203,62)];
        for (var i = 0; i < positions.Length; i++)
        {
            var slot = i;
            var button = new ItemTipButton { Position = positions[i] + new Vector2(8, 90), Size = new Vector2(36, 32), FocusMode = FocusModeEnum.None, ExpandIcon = true };
            ItemCellStyle(button); button.Pressed += () => RemoveEquipment(slot); equipmentSlots.AddChild(button); equipmentButtons.Add(button);
        }
        attributes = Text(equipment, "", 20, 102, 108); attributes.Name = "PlayerAttributes";
        attributesRight = Text(equipment, "", 138, 102, 108); attributesRight.Name = "PlayerAttributesRight";
        attributeExperience = Text(equipment, "", 20, 300, 226); attributeExperience.Name = "PlayerExperience";
        foreach (var label in new[] { attributes, attributesRight, attributeExperience })
        { label.Size = new Vector2(label.Size.X, label == attributeExperience ? 42 : 190); label.VerticalAlignment = VerticalAlignment.Top; label.AddThemeFontSizeOverride("font_size", ClientUi.Body); label.Hide(); }
        for (var i = 0; i < 4; i++)
        {
            var page = i; var caption = new[] { "人物", "属性", "状态", "技能" }[i];
            var tab = ArtButton(equipment, "Title", i == 0 ? 500 : -1, 11 + i * (58 + ClientUi.Gap), 68, caption, () => ShowEquipmentPage(page), caption);
            TabStyle(tab, 58); tab.SetPressedNoSignal(i == 0);
            tab.HoverIndex = tab.PressedIndex = 500 + i; equipmentTabs.Add(tab);
        }
    }

    private void UpdateHud()
    {
        var user = State.User!; var stats = State.CalculateStats();
        RequestHealthDisplay();
        game.GetNode<Label>("PlayerLevel").Text = $"{user.Level}级";
        game.GetNode<Label>("PlayerName").Text = user.Name;
        gold.Text = user.Gold.ToString("N0");
        if (equipment.Visible) RefreshEquipmentStats(HeroInventoryContext ? State.CalculateHeroStats() : stats);
        hudWeight.Text = (stats[Stat.BagWeight] - State.InventoryWeight).ToString("0.##"); hudWeight.TooltipText = "剩余背包负重";
        var bagCapacity = Math.Max(0, user.Inventory.Length - 6);
        hudFree.Text = user.Inventory.Skip(6).Count(item => item == null).ToString();
        hudFree.TooltipText = $"背包空闲：{hudFree.Text} / {bagCapacity} 格；快捷栏空闲：{user.Inventory.Take(6).Count(item => item == null)} / {Math.Min(6, user.Inventory.Length)} 格";
        bagGold.Text = user.Gold.ToString(); equipmentName.Text = EquipmentSubject is { } subject ? $"{(HeroInventoryContext ? "英雄 · " : "")}{subject.Name}  {subject.Level}级" : "英雄未召唤";
        bagWeight.Text = $"{State.InventoryWeight:0.##}/{stats[Stat.BagWeight]}"; bagWeight.TooltipText = "背包负重 / 上限";
        bagFree.Text = hudFree.Text; bagFree.TooltipText = hudFree.TooltipText;
        inventory.GetNode<Label>("WindowTitle").Text = HeroInventoryContext ? "背包·英雄" : "背包";
        var loading = World.Loading ? " · 地图加载中" : World.LoadError.Length > 0 ? " · 地图加载失败" : "";
        location.Text = $"{State.Map?.Title} [{State.Map?.FileName} #{State.Map?.MapIndex}] · 坐标 {user.Location.X},{user.Location.Y} · 缓存 {Assets.CachedFiles} / 下载 {Assets.Downloads} ({Assets.DownloadBytes / 1048576f:F1}MB) / 等待 {Assets.PendingDownloads} · {State.Ping}ms{loading}{(autoBattleEnabled ? " · 挂机中" : "")}{(lastResourceError.Length > 0 ? " · 资源异常" : "")}";
        location.TooltipText = World.LoadError.Length > 0 ? World.LoadError : lastResourceError;
    }
}
