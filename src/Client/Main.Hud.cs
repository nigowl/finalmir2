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
        Picture(game, "Prguse", 1, 0, 616);
        game.AddChild(new OriginalArt { Assets = Assets, State = State });
        vitals = Text(game, "", 0, 671, 101); vitals.Name = "PlayerVitals"; vitals.Size = new Vector2(101, 34);
        vitals.HorizontalAlignment = HorizontalAlignment.Center; vitals.AddThemeFontSizeOverride("font_size", 10);
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
        messages.AddThemeFontSizeOverride("normal_font_size", 11);
        game.AddChild(messages);
        chat = Edit(game, 231, 716, 627, "聊天：Enter 发送，Esc 返回游戏"); chat.MaxLength = Globals.MaxChatLength;
        var chatStyle = new StyleBoxFlat { BgColor = new GColor(.08f, .07f, .04f, .9f), ContentMarginLeft = 3, ContentMarginRight = 3 };
        foreach (var state in new[] { "normal", "focus", "read_only" }) chat.AddThemeStyleboxOverride(state, chatStyle);
        chat.AddThemeFontSizeOverride("font_size", 10); chat.Size = new Vector2(627, 19);
        chat.GuiInput += input => { if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) { chat.ReleaseFocus(); chat.AcceptEvent(); } };
        chat.TextSubmitted += text => { if (text.Trim().Length > 0) Send(new C.Chat { Message = text.Trim() }); chat.Text = ""; chat.ReleaseFocus(); };
        BuildChatResize();
        quickBars = new Control { Name = "QuickBars", Size = new Vector2(1024, 768), MouseFilter = MouseFilterEnum.Ignore }; game.AddChild(quickBars);
        BuildSkillBar();
        ArtButton(game, "Prguse", 1903, 928, 692, "背包 I", () => inventory.Visible = !inventory.Visible);
        ArtButton(game, "Prguse", 1900, 905, 692, "角色 C", () => ToggleEquipmentPage(0));
        ArtButton(game, "Prguse", 1906, 951, 692, "技能 K", () => ToggleEquipmentPage(3));
        BuildGameMenu();
        MiniMap = new GameMiniMap { Assets = Assets, State = State };
        game.AddChild(MiniMap);
        MiniMap.SetExpanded(false);
        MiniMap.DestinationSelected += point =>
        {
            if (World.Loading || !World.Walkable(point) || World.NextStep(point) == null) { Log("目标不可通行或无法到达，请选择其他坐标。"); return; }
            if (ConfirmGroundMove(point, true)) { MiniMap.SetExpanded(false); return; }
            SetDestination(point, true); MiniMap.SetExpanded(false);
            Log($"自动寻路至 {point.X},{point.Y}，Esc 取消。");
        };
    }

    private void BuildEquipment()
    {
        equipment = Panel(game, 760, 35, 264, 380); equipment.Visible = false; equipment.ClipContents = true;
        Picture(equipment, "Title", 504, 0, 0);
        paperdoll = new OriginalArt { Assets = Assets, State = State, Page = "equipment" }; equipment.AddChild(paperdoll);
        equipmentName = Text(equipment, "", 0, 12, 264); equipmentName.HorizontalAlignment = HorizontalAlignment.Center;
        ArtButton(equipment, "Prguse2", 360, 241, 3, "关闭角色", () => equipment.Hide());
        equipmentSlots = new Control { Size = new Vector2(264, 380), MouseFilter = MouseFilterEnum.Ignore }; equipment.AddChild(equipmentSlots);
        Vector2[] positions = [new(123,7),new(163,7),new(203,7),new(203,134),new(203,98),new(8,170),new(203,170),new(8,206),new(203,206),new(8,242),new(48,242),new(88,242),new(128,242),new(203,62)];
        for (var i = 0; i < positions.Length; i++)
        {
            var slot = i;
            var button = new ItemTipButton { Position = positions[i] + new Vector2(8, 90), Size = new Vector2(36, 32), FocusMode = FocusModeEnum.None, ExpandIcon = true };
            CellStyle(button); button.Pressed += () => RemoveEquipment(slot); equipmentSlots.AddChild(button); equipmentButtons.Add(button);
        }
        attributes = Text(equipment, "", 20, 102, 108); attributes.Name = "PlayerAttributes";
        attributesRight = Text(equipment, "", 138, 102, 108); attributesRight.Name = "PlayerAttributesRight";
        attributeExperience = Text(equipment, "", 20, 300, 226); attributeExperience.Name = "PlayerExperience";
        foreach (var label in new[] { attributes, attributesRight, attributeExperience })
        { label.Size = new Vector2(label.Size.X, label == attributeExperience ? 42 : 190); label.AddThemeFontSizeOverride("font_size", 11); label.Hide(); }
        for (var i = 0; i < 4; i++)
        {
            var page = i; var tab = ArtButton(equipment, "Title", i == 0 ? 500 : -1, 8 + i * 62, 70, new[] { "人物", "属性", "状态", "技能" }[i], () => ShowEquipmentPage(page));
            tab.Size = new Vector2(64, 20); tab.HoverIndex = tab.PressedIndex = 500 + i; equipmentTabs.Add(tab);
        }
    }

    private void UpdateHud()
    {
        var user = State.User!; var stats = State.CalculateStats();
        vitals.Text = $"生命 {user.HP}/{Math.Max(user.HP, stats[Stat.HP])}\n魔力 {user.MP}/{Math.Max(user.MP, stats[Stat.MP])}";
        game.GetNode<Label>("PlayerLevel").Text = $"{user.Level}级";
        game.GetNode<Label>("PlayerName").Text = user.Name;
        gold.Text = user.Gold.ToString("N0");
        RefreshEquipmentStats(stats);
        hudWeight.Text = (stats[Stat.BagWeight] - State.InventoryWeight).ToString(); hudWeight.TooltipText = "剩余背包负重";
        hudFree.Text = user.Inventory.Count(item => item == null).ToString(); hudFree.TooltipText = "剩余背包格位（含快捷栏）";
        bagGold.Text = user.Gold.ToString(); equipmentName.Text = $"{user.Name}  {user.Level}级";
        bagWeight.Text = $"{State.InventoryWeight}/{stats[Stat.BagWeight]}"; bagWeight.TooltipText = "背包负重 / 上限";
        bagFree.Text = user.Inventory.Count(item => item == null).ToString(); bagFree.TooltipText = "剩余背包格位（含快捷栏）";
        var loading = World.Loading ? " · 地图加载中" : World.LoadError.Length > 0 ? " · 地图加载失败" : "";
        location.Text = $"{State.Map?.Title} [{State.Map?.FileName} #{State.Map?.MapIndex}] · 坐标 {user.Location.X},{user.Location.Y} · 缓存 {Assets.CachedFiles} / 下载 {Assets.Downloads} ({Assets.DownloadBytes / 1048576f:F1}MB) / 等待 {Assets.PendingDownloads} · {State.Ping}ms{loading}{(autoBattleEnabled ? " · 挂机中" : "")}{(lastResourceError.Length > 0 ? " · 资源异常" : "")}";
        location.TooltipText = World.LoadError.Length > 0 ? World.LoadError : lastResourceError;
    }
}
