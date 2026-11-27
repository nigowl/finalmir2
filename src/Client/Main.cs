using System.Text.RegularExpressions;
using Crystal.Native;
using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;
using GColor = Godot.Color;

public partial class Main : Control
{
    public readonly GameState State = new();
    public readonly GameConnection Connection = new();
    public ResourceCache Assets = null!;
    public WorldView World = null!;
    internal GameMiniMap MiniMap = null!;
    public bool HandshakeReady;
    private string scene = "login";
    private Control login = null!, selection = null!, game = null!, inventory = null!, equipment = null!, npc = null!, skills = null!;
    private LineEdit account = null!, password = null!, characterName = null!, chat = null!;
    private OptionButton characterClass = null!, gender = null!;
    private Label status = null!, gold = null!, location = null!;
    private RichTextLabel messages = null!;
    private VBoxContainer characterList = null!, npcText = null!, npcGoods = null!, skillList = null!;
    private Control equipmentSlots = null!;
    private Control characterCreation = null!;
    private OriginalArt paperdoll = null!, characterPreview = null!;
    private Label equipmentName = null!, attributes = null!, bagGold = null!;
    private Label selectionLocation = null!;
    private readonly List<Button> equipmentButtons = new();
    private int selectedCharacter;
    private readonly List<(Button button, int slot)> inventorySlots = new();
    private OriginalArt inventoryGrid = null!;
    private readonly List<(TextureRect rect, string library, int index)> pictures = new();
    private readonly List<(Button button, UserItem item)> icons = new();
    private Point? destination;
    private uint actionTarget;
    private bool running, moving;
    private long moveTime, lastAcceptedMoveTime, attackTime, spellTime;
    private long lastMoved;
    private double hudRefresh;
    private Point confirmedLocation;
    private int revision = -1;
    private PanelType? shopMode;
    private ulong draggedItem;
    private bool connecting;
    private bool startupLoginPending;
    private int musicGeneration;
    public event Action<Packet>? PacketReceived;

    public override void _Ready()
    {
        TranslationServer.SetLocale("zh_CN");
        ConfigureDisplay();
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Ignore;
        GetTree().AutoAcceptQuit = false;
        var cache = Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? ProjectSettings.GlobalizePath("user://cache");
        Assets = new ResourceCache(cache);
        BuildAudio();
        // 随微端携带完整中文字形，界面与场景绘字共用，避免依赖系统回退而出现方框。
        var font = GD.Load<FontFile>("res://Fonts/NotoSansCJKsc-Regular.otf");
        ThemeDB.FallbackFont = font;
        var theme = new Theme { DefaultFont = font, DefaultFontSize = ClientUi.Body };
        Theme = theme;
        GetWindow().Theme = theme;
        GetTree().NodeAdded += StyleOptionPopup;
        BuildScreenEffects(); BuildLogin(); BuildSelection(); BuildGame();
        Assets.Sprite("ChrSel", 0);
        Assets.Sprite("Prguse", 65);
        PrepareButtonTheme();
        var startupAccount = Environment.GetEnvironmentVariable("MIR2_LOGIN_ACCOUNT");
        var startupPassword = Environment.GetEnvironmentVariable("MIR2_LOGIN_PASSWORD");
        Environment.SetEnvironmentVariable("MIR2_LOGIN_ACCOUNT", null); Environment.SetEnvironmentVariable("MIR2_LOGIN_PASSWORD", null);
        if (startupAccount != null && startupPassword != null)
        { account.Text = startupAccount; password.Text = startupPassword; startupLoginPending = true; }
        State.ChatMessage += (text, type) => Log(text, type);
        State.ItemMessage += (text, type) => { if (scene == "game") ShowItemNotice(text, type); };
        var address = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--server="))?[9..]
            ?? Environment.GetEnvironmentVariable("MIR2_SERVER") ?? "http://127.0.0.1:5080";
        _ = Connect(address);
        if (OS.GetCmdlineUserArgs().Any(a => a is "--verify-item-prices" or "--verify-material-inventory" or "--verify-equipment-play" or "--verify-equipment-appraisal" or "--verify-hero-portrait" or "--verify-grid-movement" or "--verify-default-client" or "--verify-social" or "--verify-hero-management" or "--verify-hero-inventory" or "--verify-hero-recovery" or "--verify-gm-login" or "--verify-health-display" or "--verify-movement-state" or "--verify-npc-input" or "--verify-skill-removal" or "--verify-name-colours" or "--verify-guild-war" or "--verify-guild-creation" or "--verify-object-names" or "--verify-item-owners" or "--verify-shop-catalog")) AddChild(new NativeSmoke { Client = this });
        if (OS.GetCmdlineUserArgs().Any(a => a is "--verify-client-polish" or "--verify-city-travel" or "--verify-connection-failures" or "--verify-resource-startup" or "--verify-visual-fixes" or "--verify-locomotion" or "--verify-action-items" or "--verify-audio" or "--smoke" or "--verify-save" or "--reference-world" or "--verify-auto-login" or "--verify-ui" or "--verify-quest-ui" or "--verify-public-services" or "--verify-storage-pages" or "--profile-actors" or "--verify-actors" or "--profile-motion" or "--verify-smooth" or "--verify-movement" or "--verify-automation" or "--verify-accounts" or "--verify-combat" or "--verify-npc-templates" or "--verify-shadows" or "--verify-item-ui" or "--verify-inventory" or "--verify-item-weight" or "--verify-npc-shop" or "--verify-class-selection"))
            AddChild(new NativeSmoke { Client = this });
        if (OS.GetCmdlineUserArgs().Any(a => a is "--spell-effects" or "--class-combos")) AddChild(new NativeSpellSmoke { Client = this });
    }

    private Label Text(Control parent, string text, float x, float y, float width = 250)
    {
        var label = new Label { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 24), VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", new GColor("eadbb6")); parent.AddChild(label); return label;
    }
    private Button Button(Control parent, string text, float x, float y, float width, Action action)
    {
        var button = new Button { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, ClientUi.ButtonHeight), FocusMode = FocusModeEnum.None };
        button.Pressed += () => { action(); _ = PlaySound(10103); }; parent.AddChild(button);
        // 先继承中文主题再定尺寸，避免默认字体的最小高度把按钮撑大后不再缩回。
        button.Size = new Vector2(width, ClientUi.ButtonHeight); return button;
    }
    private OriginalButton ArtButton(Control parent, string library, int index, float x, float y, string hint, Action action, string? caption = null)
    {
        var button = new OriginalButton { Assets = Assets, Library = library, Index = index, Text = caption ?? "", Position = new Vector2(x, y),
            Size = caption == null ? new Vector2(24, 20) : new Vector2(84, ClientUi.ButtonHeight), TooltipText = hint };
        button.Pressed += () => { action(); _ = PlaySound(10103); }; parent.AddChild(button);
        if (caption != null) button.Size = new Vector2(84, ClientUi.ButtonHeight);
        return button;
    }
    private LineEdit Edit(Control parent, float x, float y, float width, string placeholder, bool secret = false)
    {
        var edit = new LineEdit { Position = new Vector2(x, y), Size = new Vector2(width, 28), PlaceholderText = placeholder, Secret = secret };
        parent.AddChild(edit); edit.Size = new Vector2(width, 28); return edit;
    }
    private TextureRect Picture(Control parent, string library, int index, float x, float y, bool nativeFrame = false)
    {
        if (nativeFrame && parent is Panel panel)
        {
            panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty()); skinnedPanels.Remove(panel);
        }
        var image = new TextureRect { Position = new Vector2(x, y), MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Nearest };
        parent.AddChild(image); pictures.Add((image, library, index));
        PreparePicture(image, library, index);
        return image;
    }
    private void PreparePicture(TextureRect rect, string library, int index)
    {
        if (rect.Texture == null && Assets.Sprite(library, index) is { } image)
        {
            rect.Texture = library == "Title" && index == 196 && rect.GetParent() == inventory
                ? OriginalArt.InventorySkin(image) : library == "Title" && index == 196 && rect.GetParent() == questBag
                    ? OriginalArt.MaterialInventorySkin(image) : OriginalArt.InterfaceTexture(library, index, image);
            rect.Size = rect.Texture.GetSize();
        }
    }
    private Control Panel(Control parent, float x, float y, float width, float height)
    {
        var panel = new Panel { Position = new Vector2(x, y), Size = new Vector2(width, height) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new GColor(0.05f, 0.04f, 0.025f, 0.96f), BorderColor = new GColor("82704c"), BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1 });
        if (originalPanelSkin != null) panel.AddThemeStyleboxOverride("panel", originalPanelSkin);
        skinnedPanels.Add(panel); parent.AddChild(panel); return panel;
    }
    private static void ItemCellStyle(Button button) => CellStyle(button, ClientUi.SlotInset, ClientUi.SlotInset);
    private static void CellStyle(Button button, float inset = 1, float verticalInset = 1)
    {
        var normal = new StyleBoxEmpty { ContentMarginLeft = inset, ContentMarginRight = inset, ContentMarginTop = verticalInset, ContentMarginBottom = verticalInset };
        foreach (var state in new[] { "normal", "disabled", "focus" }) button.AddThemeStyleboxOverride(state, normal);
        var hover = new StyleBoxFlat { BgColor = new GColor(1, 1, 0.5f, 0.12f), BorderColor = new GColor("b59b5d"), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = inset, ContentMarginRight = inset, ContentMarginTop = verticalInset, ContentMarginBottom = verticalInset };
        button.AddThemeStyleboxOverride("hover", hover);
        var pressed = (StyleBoxFlat)hover.Duplicate(); pressed.BgColor = new GColor(1, 1, 0.5f, 0.25f);
        button.AddThemeStyleboxOverride("pressed", pressed); button.AddThemeStyleboxOverride("hover_pressed", pressed);
    }
    private void BuildLogin()
    {
        login = new Control { Size = new Vector2(1024, 768), MouseFilter = MouseFilterEnum.Ignore }; AddChild(login);
        var frame = Panel(login, 348, 274, 328, 220);
        Picture(frame, "Prguse", 1084, 0, 0);
        var loginTitle = WindowTitle(frame, "游戏连接", 250); loginTitle.Position = new Vector2(39, 12); loginTitle.HorizontalAlignment = HorizontalAlignment.Center;
        Text(frame, "账号", 48, 83, 32).Size = new Vector2(32, 18);
        Text(frame, "密码", 48, 106, 32).Size = new Vector2(32, 18);
        account = Edit(frame, 85, 83, 136, ""); account.MaxLength = 15;
        password = Edit(frame, 85, 106, 136, "", true); password.MaxLength = 15;
        foreach (var edit in new[] { account, password })
        {
            edit.Size = new Vector2(136, 18);
            foreach (var state in new[] { "normal", "focus", "read_only" }) edit.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            edit.TextSubmitted += _ => Login(account.Text, password.Text);
        }
        var loginAction = ArtButton(frame, "Title", 320, 227, 81, "登录", () => Login(account.Text, password.Text), "登录"); loginAction.Size = new Vector2(48, 44);
        var register = ArtButton(frame, "Title", 323, 60, 176, "打开账号注册", OpenRegistration, "注册账号");
        var quit = ArtButton(frame, "Title", 329, 166, 176, "退出游戏", () => _ = Quit(), "退出游戏"); register.Size = quit.Size = new Vector2(100, ClientUi.ButtonHeight);
        Text(login, "Godot 原生客户端 · 原 TCP 协议 · 原版资源按需缓存", 320, 520, 490);
        status = Text(login, "连接中…", 230, 712, 700);
        BuildAccountDialogs(); BuildConnectionDialog();
    }
    private void BuildSelection()
    {
        selection = new Control { Size = new Vector2(1024, 768), Visible = false, MouseFilter = MouseFilterEnum.Ignore }; AddChild(selection);
        Picture(selection, "Prguse", 65, 0, 0);
        characterPreview = new OriginalArt { Assets = Assets, State = State, Page = "select" }; selection.AddChild(characterPreview);
        var selectTitle = Text(selection, "选择角色", 445, 20, 134); selectTitle.HorizontalAlignment = HorizontalAlignment.Center; selectTitle.AddThemeFontSizeOverride("font_size", ClientUi.Title);
        var gameTitle = Text(selection, "传奇2", 416, 58, 192); gameTitle.HorizontalAlignment = HorizontalAlignment.Center; gameTitle.AddThemeFontSizeOverride("font_size", ClientUi.Title);
        selectionLocation = Text(selection, "", 199, 613, 174); selectionLocation.Size = new Vector2(174, 19);
        selectionLocation.HorizontalAlignment = HorizontalAlignment.Center; selectionLocation.ClipText = true; selectionLocation.AddThemeFontSizeOverride("font_size", 10);
        characterList = new VBoxContainer { Position = new Vector2(637, 194), Size = new Vector2(278, 390) }; characterList.AddThemeConstantOverride("separation", 0); selection.AddChild(characterList);
        var enter = ArtButton(selection, "Title", 340, 132, 736, "进入游戏", () => { if (pendingCharacterDeletion < 0 && State.Characters.Count > selectedCharacter) Send(new C.StartGame { CharacterIndex = State.Characters[selectedCharacter].Index }); }, "进入游戏");
        var create = ArtButton(selection, "Title", 343, 296, 736, "创建角色", OpenCharacterCreation, "创建角色");
        var quit = ArtButton(selection, "Title", 352, 788, 736, "退出", () => _ = Quit(), "退出游戏");
        enter.Size = create.Size = quit.Size = new Vector2(100, ClientUi.ButtonHeight);
        BuildCharacterCreation(); BuildCharacterDeletion();
    }
    private void BuildGame()
    {
        game = new Control { Size = new Vector2(1024, 768), Visible = false, MouseFilter = MouseFilterEnum.Ignore }; AddChild(game);
        var viewport = new Control { Size = new Vector2(1024, 768), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore }; game.AddChild(viewport);
        World = new WorldView { State = State, Assets = Assets }; World.SoundRequested += sound => _ = PlaySound(sound); viewport.AddChild(World);
        BuildHud();
        BuildInventory(); BuildStorage();
        BuildEquipment();
        BuildNpc();
        skills = new ScrollContainer { Position = new Vector2(12, 96), Size = new Vector2(240, 270), Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; equipment.AddChild(skills);
        skillList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; skills.AddChild(skillList);
        skillList.AddThemeConstantOverride("separation", ClientUi.SkillRowGap);
        BuildPublicShop(); BuildQuests(); BuildGroup(); BuildTrade(); BuildGuild(); BuildSocial();
        BuildSettings();
        BuildItemDrag();
        BuildSkillLearning(); BuildAppraisalScroll();
        BuildAutoBattleConfirmation();
        foreach (var dialog in new[] { inventory, equipment, npc }) Draggable(dialog);
    }
    private static void Draggable(Control panel)
    {
        var dragging = false;
        panel.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) dragging = button.Pressed && button.Position.Y < 28;
            if (dragging && input is InputEventMouseMotion motion)
                panel.Position = (panel.Position + motion.Relative).Clamp(Vector2.Zero, new Vector2(1024, 768) - panel.Size);
        };
    }
    private void BuildInventory()
    {
        inventory = Panel(game, 700, 35, 316, 236 + ClientUi.InventoryExtraHeight); inventory.Visible = false;
        Picture(inventory, "Title", 196, 0, 0, nativeFrame: true); WindowTitle(inventory, "背包", 90);
        var questTab = ArtButton(inventory, "Title", 739, 112, 4, "材料物品", () => { questBag.Visible = !questBag.Visible; RenderQuestBag(); }, "材料物品");
        questTab.Name = "InventoryQuestItems"; questTab.Size = new Vector2(82, ClientUi.ButtonHeight);
        questTab.HoverIndex = questTab.PressedIndex = 198;
        var warehouse = Button(inventory, "仓库物品", 198, 4, 82, OpenInventoryStorage); warehouse.Name = "InventoryStorage";
        ArtButton(inventory, "Prguse2", 360, 289, 3, "关闭背包", () => inventory.Hide());
        inventory.AddChild(new OriginalArt { Assets = Assets, State = State, Page = "inventory" });
        bagGold = Text(inventory, "", 40, 212 + ClientUi.InventoryExtraHeight, 111); bagGold.Size = new Vector2(111, 14); bagGold.AddThemeFontSizeOverride("font_size", 10);
        bagWeight = Text(inventory, "", 182, 212 + ClientUi.InventoryExtraHeight, 82); bagWeight.Name = "BagWeight"; bagWeight.Size = new Vector2(82, 14);
        bagWeight.HorizontalAlignment = HorizontalAlignment.Center; bagWeight.AddThemeFontSizeOverride("font_size", ClientUi.Small);
        bagFree = Text(inventory, "", 268, 212 + ClientUi.InventoryExtraHeight, 26); bagFree.Name = "BagFree"; bagFree.Size = new Vector2(26, 14); bagFree.HorizontalAlignment = HorizontalAlignment.Center; bagFree.AddThemeFontSizeOverride("font_size", 10);
        // 前六格独立显示在快捷栏，普通背包从第七格开始。
        var scroll = new ScrollContainer { Name = "InventoryScroll", Position = new Vector2(9, 37), Size = new Vector2(297, ClientUi.InventoryRows * 33),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        inventory.AddChild(scroll);
        var bar = scroll.GetVScrollBar();
        foreach (var state in new[] { "scroll", "grabber", "grabber_highlight", "grabber_pressed" })
            bar.AddThemeStyleboxOverride(state, new StyleBoxFlat { BgColor = new GColor(state == "scroll" ? "19150e" : "82704c"), ContentMarginLeft = 1, ContentMarginRight = 1 });
        inventoryGrid = new OriginalArt { Assets = Assets, State = State, Page = "inventory-grid", MouseFilter = MouseFilterEnum.Ignore }; scroll.AddChild(inventoryGrid);
        for (var i = 0; i < 6; i++)
        {
            var position = OriginalArt.QuickSlotPosition(i);
            var item = (OriginalItemButton)ItemButton(quickBars, i, position.X, position.Y);
            item.Name = $"ItemShortcut{i + 1}";
            item.Size = new Vector2(32, 32); item.SlotAssets = Assets;
            inventorySlots.Add((item, i));
            var key = Text(item, (i + 1).ToString(), 1, 0, 26);
            key.AddThemeFontSizeOverride("font_size", ClientUi.Micro); key.Size = new Vector2(26, 14);
            key.AddThemeColorOverride("font_shadow_color", Colors.Black); key.AddThemeConstantOverride("shadow_offset_x", 1); key.AddThemeConstantOverride("shadow_offset_y", 1);
        }
    }
    private void ResizeInventoryGrid(UserItem[] items)
    {
        var slots = ItemPresentation.BagSlots(items);
        if (inventoryGrid.GetChildCount() == slots.Length && inventorySlots.Where(pair => pair.button.GetParent() == inventoryGrid)
            .Select(pair => pair.slot).SequenceEqual(slots)) return;
        inventorySlots.RemoveAll(pair => pair.button.GetParent() == inventoryGrid);
        Clear(inventoryGrid);
        inventoryGrid.CustomMinimumSize = new Vector2(295, (slots.Length + ClientUi.InventoryColumns - 1) / ClientUi.InventoryColumns * 33);
        for (var slot = 0; slot < slots.Length; slot++)
        {
            var index = slots[slot];
            var button = ItemButton(inventoryGrid, index, slot % ClientUi.InventoryColumns * 37, slot / ClientUi.InventoryColumns * 33);
            button.Name = $"InventorySlot{index}"; inventorySlots.Add((button, index));
        }
    }
    private Button ItemButton(Control parent, int slot, float x, float y)
    {
        var button = new OriginalItemButton { Position = new Vector2(x, y), Size = new Vector2(36, 32), FocusMode = FocusModeEnum.None, ExpandIcon = true };
        ItemCellStyle(button);
        parent.AddChild(button); return button;
    }

    public void Login(string id, string secret)
    {
        if (accountRequest != null) { status.Text = "账号操作正在等待回执，请稍候。"; return; }
        if (!ValidateCredentials(id, secret)) return;
        account.Text = id; password.Text = secret;
        Send(new C.Login { AccountID = id, Password = secret });
    }
    private bool ValidateCredentials(string id, string secret, Label? feedback = null)
    {
        feedback ??= status;
        if (!HandshakeReady || !Connection.Connected) { feedback.Text = "尚未连接，请启动后端后重新连接。"; return false; }
        if (!Regex.IsMatch(id, "^[A-Za-z0-9]{3,15}$") || !Regex.IsMatch(secret, "^[A-Za-z0-9]{5,15}$"))
        { feedback.Text = "账号须为3–15位，密码5–15位，均限英文字母或数字。"; return false; }
        return true;
    }
    public void Send(Packet packet)
    {
        if (BlockAppraisalConflict(packet)) { Log("正在等待鉴定结果，请稍候。", ChatType.Hint); return; }
        if (BlockHeroItemConflict(packet)) { Log("正在等待英雄物品回执，请稍候。", ChatType.Hint); return; }
        if (packet is C.CallNPC call) { CloseNpcInput(); InvalidateNpcPurchase(); npcDialogueId = call.ObjectID; }
        if (HandshakeReady || packet is C.ClientVersion) { inventoryOrganizer.Sent(packet); _ = Connection.Send(packet); }
    }
    public override void _Process(double delta)
    {
        Assets.BeginFrame();
        PrepareButtonTheme();
        UpdateDisplay(delta);
        for (var handled = 0; handled < 1024 && Connection.Received.TryDequeue(out var packet); handled++)
        {
            AudioItemPacket(packet); State.Apply(packet); Handle(packet); PacketReceived?.Invoke(packet);
        }
        while (Connection.Errors.TryDequeue(out var error))
        {
            Connection.Received.Clear(); State.Reset(); inventoryOrganizer.Reset(); HandshakeReady = false; ResetAccountRequests(); SetScene("login"); status.Text = error; Log(error);
            if (connecting) connectionHandshake?.TrySetException(new IOException(error));
            else ShowConnectionFailure(error);
        }
        ProcessCharacterCreation(Environment.TickCount64);
        ProcessAccountRequests(Environment.TickCount64);
        UpdateConnectionDialog(Environment.TickCount64);
        if (Assets.Errors.TryDequeue(out var resourceError)) { lastResourceError = resourceError; GD.PrintErr(resourceError); }
        pictures.RemoveAll(p => !IsInstanceValid(p.rect));
        foreach (var (rect, library, index) in pictures) PreparePicture(rect, library, index);
        foreach (var (button, item) in icons)
            if (IsInstanceValid(button) && item.Info != null)
            {
                if (button is OriginalItemButton { SlotAssets: not null } slot) slot.SlotIcon ??= Assets.Sprite("Items", item.Info.Image)?.Texture;
                else if (button.Icon == null) button.Icon = Assets.Sprite("Items", item.Info.Image)?.Texture;
            }
        if (State.User != null && scene == "game")
        {
            hudRefresh -= delta;
            if (hudRefresh <= 0) { UpdateHud(); RefreshSkillBar(); RefreshCompanionPortraits(); hudRefresh = .1; }
            if (revision != State.InventoryRevision) { RenderInventory(); RenderStorage(); RenderQuestBag(); RenderTrade(); RenderGuildStorage(); if (settingsDialog.Visible && itemRuleOwnedOnly.ButtonPressed) FillItemRuleRows(); revision = State.InventoryRevision; }
            ProcessPublicShop(); ProcessQuestNavigation(); ProcessStorage(); ProcessQuests(); ProcessGroup(); ProcessTrade(); ProcessGuild(); ProcessSocial(); ProcessAutomation(); ProcessActions(); ProcessFootsteps();
            ProcessNpc(); ProcessAppraisal(); ProcessInventoryOrganization(Environment.TickCount64);
        }
        ProcessItemDrag();
        ProcessItemNotices(Environment.TickCount64);
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (scene != "login") return;
        var background = Assets.Sprite("ChrSel", 0);
        if (background != null) DrawTexture(background.Texture, Vector2.Zero);
    }
    private void Handle(Packet packet)
    {
        AppraisalPacket(packet);
        HeroManagementPacket(packet);
        if (packet is S.ManageHeroes or S.ChangeHero) RequestHeroRecovery();
        SkillLearningPacket(packet);
        // 英雄资料已在 GameState 处理，不能触发玩家登录时的资产锁和画面重置。
        if (packet is S.HeroInformation)
        {
            if (HeroInventoryContext) { RenderInventory(); RenderSkills(); }
            return;
        }
        NpcInputPacket(packet);
        HeroItemPacket(packet);
        inventoryOrganizer.Observe(packet);
        AutoRecyclePacket(packet);
        AutomationPacket(packet);
        AudioPacket(packet);
        TradePacket(packet);
        GuildPacket(packet);
        ItemDropPacket(packet);
        NpcTransactionPacket(packet);
        PublicShopPacket(packet); QuestNavigationPacket(packet);
        World.SpellPacket(packet);
        SkillPreparationPacket(packet);
        ClassSkillPacket(packet);
        switch (packet)
        {
            case S.Connected: Send(new C.ClientVersion { VersionHash = [] }); break;
            case S.Disconnect p:
                HandshakeReady = false; Connection.Dispose(); ResetAccountRequests(); SetScene("login");
                status.Text = $"服务端断开连接：{p.Reason}。";
                connectionHandshake?.TrySetException(new IOException(status.Text));
                if (!connecting) ShowConnectionFailure(status.Text);
                break;
            case S.ClientVersion p:
                HandshakeReady = p.Result == 1; status.Text = HandshakeReady ? "已连接服务器" : "客户端版本不匹配";
                if (HandshakeReady) { connectionDialog.Hide(); connectionHandshake?.TrySetResult(); }
                else connectionHandshake?.TrySetException(new InvalidDataException(status.Text));
                if (startupLoginPending) { startupLoginPending = false; if (HandshakeReady) Login(account.Text, password.Text); }
                break;
            case S.NewAccount p: RegistrationResult(p); break;
            case S.Login p:
                status.Text = p.Result == 5 ? "管理端要求修改密码，请设置新密码后重新登录。" : $"登录失败：{p.Result}（3 账号不存在，4 密码错误）";
                if (p.Result == 5) OpenPasswordChange(status.Text);
                break;
            case S.ChangePassword p: LoginPasswordResult(p); break;
            case S.ChangePasswordBanned p: LoginPasswordBanned(p); break;
            case S.LoginBanned p: status.Text = p.Reason; break;
            case S.LoginSuccess: SetScene("selection"); RenderCharacters(); _ = PlaySound(10147, true); break;
            case S.NewCharacterSuccess p: CharacterCreated(p.CharInfo); break;
            case S.CharacterLocation p:
                if (scene == "selection" && State.Characters.ElementAtOrDefault(selectedCharacter)?.Index == p.CharacterIndex)
                { selectionLocation.Text = p.Found ? $"{p.MapTitle} · {p.Location.X},{p.Location.Y}" : "新角色 · 尚未进入地图"; selectionLocation.TooltipText = selectionLocation.Text; }
                break;
            case S.NewCharacter p: CharacterCreationFailed(p.Result); break;
            case S.DeleteCharacterSuccess p: CharacterDeleted(p.CharacterIndex); break;
            case S.DeleteCharacter p: CharacterDeletionFailed(p.Result); break;
            case S.StartGame p: if (p.Result != 4) Log($"进入游戏失败：{p.Result}"); break;
            case S.UserInformation p: healthDisplayRequested = false; RequestHealthDisplay(); RequestEquipmentGlow(); SetScene("game"); MiniMap.SetExpanded(false); pendingAttack = null; destination = null; actionTarget = 0; moveTime = lastAcceptedMoveTime = 0; moving = false; confirmedLocation = p.Location; lastMoved = 0; ResetRouteRetry(); RenderInventory(); RenderSkills(); SetMapMusic(); break;
            case S.MapInformation p: _ = World.Load(p.FileName); break;
            case S.MapChanged p: doorRequests.Clear(); MiniMap.SetExpanded(false); MiniMap.Route = []; pendingAttack = null; moveTime = lastAcceptedMoveTime = 0; npc.Hide(); shopMode = null; CloseStorage(); CloseQuests(); destination = null; actionTarget = 0; moving = false; lastMoved = 0; confirmedLocation = p.Location; ResetRouteRetry(); _ = World.Load(p.FileName); SetMapMusic(); break;
            case S.Death: destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = []; moving = false; lastMoved = 0; ResetRouteRetry(); ShowDeathDialog(); break;
            case S.Revived: deathDialog.Hide(); break;
            case S.UserLocation p: ConfirmLocation(p.Location); break;
            case S.Pushed when State.User != null:
                confirmedLocation = State.User.Location; moving = false; lastMoved = lastAcceptedMoveTime = 0; pendingAttack = null; MiniMap.Route = []; ResetRouteRetry(); break;
            case S.Opendoor p: doorRequests.Remove(p.DoorIndex); break;
            case S.UserDash or S.UserBackStep or S.UserDashAttack when State.User != null:
                confirmedLocation = State.User.Location; moving = false; lastMoved = 0; ResetRouteRetry(); break;
            case S.DamageIndicator p: World.ShowDamage(p.ObjectID, p.Damage, p.Type); break;
            case S.Magic p:
                if (State.User != null) World.ShowSpell(p.Spell, State.User.ObjectID, p.TargetID, p.Target, p.Cast, p.Level, p.SecondaryTargetIDs);
                break;
            case S.ObjectMagic p:
                if (p.ObjectID != State.User?.ObjectID || p.SelfBroadcast || p.Spell == Spell.TwinDrakeBlade) World.ShowSpell(p.Spell, p.ObjectID, p.TargetID, p.Target, p.Cast, p.Level, p.SecondaryTargetIDs);
                break;
            case S.NPCResponse p: RenderNpc(p.Page); break;
            case S.NPCGoods p: RenderGoods(p); break;
            case S.NPCStorage: OpenStorage(); break;
            case S.StorageUnlockResult: case S.StoragePasswordResult: StoragePasswordResult(packet); break;
            case S.StoreItem: case S.TakeBackItem: storageBusy = false; RenderStorage(); break;
            case S.MoveItem p: if (p.Grid == MirGridType.Storage) { storageBusy = false; RenderStorage(); } break;
            case S.NPCSell: shopMode = PanelType.Sell; Log("双击背包物品，确认数量后出售。"); inventory.Show(); break;
            case S.NPCRepair p: OpenRepair(p.Rate, false); break;
            case S.NPCSRepair p: OpenRepair(p.Rate, true); break;
            case S.RepairItem p: if (p.UniqueID == pendingRepair) { pendingRepair = 0; RefreshRepair(); } break;
            case S.ItemRepaired p: if (p.UniqueID == repairItem) { repairItem = 0; RefreshRepair(); } break;
            case S.LogOutSuccess: SetScene("selection"); RenderCharacters(); break;
            case S.LogOutFailed: Log("战斗中暂时无法退出角色，请稍候。"); break;
            case S.ChangeAMode: RefreshAttackModes(); break;
            case S.ReturnToLogin: HandshakeReady = true; SetScene("login"); break;
            case S.ChangeHero or S.UpdateHeroSpawnState when HeroInventoryContext: RenderInventory(); RenderSkills(); break;
            case S.NewMagic: RenderSkills(); break;
            case S.MagicLeveled: case S.SpellToggle: RefreshSkillRows(); break;
            case S.RemoveMagic: RenderSkills(); RefreshSkillBar(); break;
            case S.MergeItem p: if (p.GridFrom == MirGridType.Storage || p.GridTo == MirGridType.Storage) { storageBusy = false; RenderStorage(); } break;
            case S.LevelChanged: _ = PlaySound(10156); break;
        }
    }
    private void SetScene(string name)
    {
        doorRequests.Clear();
        CloseNpcInput();
        StopKeyboardMovement();
        ResetAccountRequests();
        CancelQuestNavigation(); ClosePublicShop(); questNavigationRequests.Clear();
        StopAutoBattle(); automation.Reset(); automationNotice = ""; CloseSettings(); CloseGroundMove(); CancelItemDrag(); dropDialog.Hide(); dropCandidate = pendingDrop = 0;
        preparedSkill = null; skillEquipmentPending = 0; skillPreparationPaused = false; ResetClassSkills();
        if (heroWindow) equipment.Position = characterEquipmentPosition;
        heroWindow = false; paperdoll.Hero = false;
        scene = name; login.Visible = name == "login"; selection.Visible = name == "selection"; game.Visible = name == "game";
        deathDialog.Hide(); sessionDialog.Hide(); gameMenu.Hide();
        heroCreation.Hide(); heroManagement.Hide();
        if (name != "game") { heroCreationPending = heroCreationUnknown = false; heroToggleAt = 0; }
        if (name != "game") publicPurchases.Reset();
        if (name == "game" && State.Me?.Dead == true) ShowDeathDialog();
        ResetCharacterCreation();
        InvalidateNpcPurchase(); npcTransactionPending = false; npcPendingSale = 0;
        destination = null; actionTarget = 0; pendingAttack = null; moving = false; MiniMap.Route = []; MiniMap.SetExpanded(false); GetViewport().GuiReleaseFocus();
        CloseStorage(); CloseQuests(); inventory.Hide(); equipment.Hide(); npc.Hide(); questBag.Hide(); CloseGroup(); CloseTradeUi(); CloseGuildUi(); CloseSocial(); if (name != "game") ResetChat(); shopMode = null;
    }
    private void RenderCharacters()
    {
        Clear(characterList);
        selectedCharacter = Math.Clamp(selectedCharacter, 0, Math.Max(0, State.Characters.Count - 1));
        deleteCharacterButton.Disabled = State.Characters.Count == 0 || characterCreateSentAt != 0 || pendingCharacterDeletion >= 0;
        characterPreview.Character = State.Characters.ElementAtOrDefault(selectedCharacter);
        if (State.Characters.Count == 0) characterCreation.Show();
        for (var i = 0; i < Globals.MaxCharacterCount; i++)
        {
            var row = new Control { Name = $"CharacterSlot{i + 1}", CustomMinimumSize = new Vector2(278, 78) }; characterList.AddChild(row);
            if (i >= State.Characters.Count) continue;
            var index = i; var character = State.Characters[i];
            Picture(row, "Title", 658 + (int)character.Class, 0, 0);
            foreach (var (caption, x, y) in new[] { ("名字", 78, 8), ("等级", 78, 29), ("职业", 146, 29) })
            { var label = Text(row, caption, x, y, 28); label.Size = new Vector2(28, 18); label.AddThemeFontSizeOverride("font_size", ClientUi.Small); }
            var characterLabel = Text(row, character.Name, 114, 8, 164); characterLabel.Size = new Vector2(164, 18); characterLabel.ClipText = true; characterLabel.TooltipText = character.Name;
            Text(row, character.Level.ToString(), 114, 29, 24).Size = new Vector2(24, 18);
            Text(row, character.Class switch { MirClass.Warrior => "战士", MirClass.Wizard => "法师", MirClass.Taoist => "道士", _ => character.Class.ToLocalizedString() }, 182, 29, 96).Size = new Vector2(96, 18);
            var button = Button(row, "", 0, 0, 288, () => { selectedCharacter = index; RenderCharacters(); });
            button.Name = "CharacterChoice"; button.ToggleMode = true; button.ButtonPressed = index == selectedCharacter;
            CellStyle(button, 0, 0); button.Size = new Vector2(288, 54);
        }
        selectionLocation.Text = State.Characters.Count == 0 ? "" : Assets.SupportsCharacterLocation ? "正在读取角色位置…" : "位置查询需要更新服务端";
        if (Assets.SupportsCharacterLocation && State.Characters.ElementAtOrDefault(selectedCharacter) is { } selected)
            Send(new C.CharacterLocationRequest { CharacterIndex = selected.Index });
    }
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private void RenderInventory()
    {
        if (State.User == null) return;
        ResizeInventoryGrid(State.User.Inventory);
        icons.RemoveAll(pair => !GodotObject.IsInstanceValid(pair.button) || !npcGoods.IsAncestorOf(pair.button) && !publicShopRows.IsAncestorOf(pair.button));
        foreach (var (button, slot) in inventorySlots)
        {
            var item = slot < State.User.Inventory.Length ? State.User.Inventory[slot] : null;
            if (IsItemShortcut(button) && !ItemPresentation.CanQuickUse(item)) item = null;
            button.Icon = null; button.Text = ""; ((OriginalItemButton)button).Count = item?.Count ?? 0; ((OriginalItemButton)button).SlotIcon = null;
            ItemTooltip(button, item, IsItemShortcut(button) ? "仅可放入使用类道具" : "空格");
            if (item != null) icons.Add((button, item));
        }
        for (var i = 0; i < equipmentButtons.Count; i++)
        {
            var item = SubjectEquipment.ElementAtOrDefault(i); var button = equipmentButtons[i];
            button.Icon = null; ItemTooltip(button, item, ((EquipmentSlot)i).ToLocalizedString());
            if (item != null) icons.Add((button, item));
        }
    }
    private void RemoveEquipment(int slot)
    {
        if (SkillPreparationBusy) { Log("正在等待毒符换装回执，请稍候。"); return; }
        var item = SubjectEquipment.ElementAtOrDefault(slot); if (item == null) return;
        var to = Array.IndexOf(State.User!.Inventory, null);
        if (to >= 0) SendSubjectItem(new C.RemoveItem { Grid = SubjectGrid, UniqueID = item.UniqueID, To = to }); else Log("背包已满。");
    }
    private void ShowEquipmentPage(int page)
    {
        equipmentPage = page;
        for (var i = 0; i < equipmentTabs.Count; i++)
        { equipmentTabs[i].Index = i == page ? 500 + i : -1; equipmentTabs[i].SetPressedNoSignal(i == page); }
        equipment.Show(); paperdoll.Visible = page == 0; equipmentSlots.Visible = page == 0;
        attributes.Visible = attributesRight.Visible = attributeExperience.Visible = page is 1 or 2; skills.Visible = page == 3;
        if (page == 3) RenderSkills();
        else RefreshEquipmentStats(HeroInventoryContext ? State.CalculateHeroStats() : State.CalculateStats());
    }
    private void ToggleEquipmentPage(int page)
    {
        if (!HeroInventoryContext && equipment.Visible && equipmentPage == page) { CloseEquipment(); return; }
        if (HeroInventoryContext) CloseEquipment();
        heroWindow = false; paperdoll.Hero = false; equipmentTabs[0].Text = "人物";
        CancelItemContext(); ShowEquipmentPage(page); RenderInventory();
    }
    public void UseItem(ulong id)
    {
        if (appraisalPending != null) { Log("正在等待鉴定结果，请稍候。", ChatType.Hint); return; }
        if (SkillPreparationBusy) { Log("正在等待毒符换装回执，请稍候。"); return; }
        if (inventoryOrganizer.Locked(id) || automation.PendingItem == id || pendingDrop == id || recyclePending?.UniqueID == id) { Log("正在等待该物品的服务端回执。"); return; }
        var item = State.User?.Inventory.Concat(State.User.QuestInventory).FirstOrDefault(i => i?.UniqueID == id);
        if (item?.Info == null) return;
        if (EquipmentAppraisal.IsScroll(item.Info)) { BeginAppraisalSelection(item); return; }
        if (HeroInventoryContext) { UseSubjectItem(item); return; }
        if (State.Trading) { OfferTradeItem(Array.FindIndex(State.User!.Inventory, i => i?.UniqueID == id)); return; }
        if (GuildStorageOpen) { StoreGuildItem(Array.FindIndex(State.User!.Inventory, i => i?.UniqueID == id)); return; }
        if (storage.Visible) { StoreInventory(Array.FindIndex(State.User!.Inventory, i => i?.UniqueID == id)); return; }
        if (shopMode == PanelType.Sell) { ConfirmNpcSale(item); return; }
        if (shopMode is PanelType.Repair or PanelType.SpecialRepair) { SelectRepairItem(id); return; }
        UseSubjectItem(item);
    }
    private void UseSubjectItem(UserItem item)
    {
        if (item.Info.Type == ItemType.Book) { ConfirmSkillLearning(item); return; }
        var slots = ItemPresentation.Slots(item.Info.Type);
        var slot = slots.FirstOrDefault(index => SubjectEquipment.ElementAtOrDefault(index) == null, slots.FirstOrDefault(-1));
        SendSubjectItem(slot >= 0 ? new C.EquipItem { Grid = SubjectGrid, UniqueID = item.UniqueID, To = slot }
            : new C.UseItem { Grid = SubjectGrid, UniqueID = item.UniqueID });
    }
    public void BindMagic(int index, byte key)
    {
        var magics = SubjectMagics;
        if (index < 0 || index >= magics.Count || (HeroInventoryContext ? key != 0 && !SkillKeys.IsHero(key) : !SkillKeys.IsPlayer(key))) return;
        var magic = magics[index];
        if (magic.Key == key) return;
        if (HeroInventoryContext && State.HeroState != HeroSpawnState.Summoned) return;
        Send(new C.MagicKey { Spell = magic.Spell, Key = key, OldKey = magic.Key });
        foreach (var other in magics) if (other.Key == key) other.Key = 0;
        magic.Key = key;
    }
    public void Log(string text, ChatType type = ChatType.System)
    {
        if (messages == null) return;
        AppendChat(text, type);
        if (scene != "game" && status != null) status.Text = text;
    }
    private void SetMapMusic()
    {
        musicGeneration++;
        music.Stop(); music.Stream = null;
        if (State.Map?.Music > 0) _ = PlaySound(State.Map.Music, true);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (scene != "game" || State.User == null) return;
        if (input is InputEventKey && GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if (KeyboardInput(input)) return;
        StopBattleForInput(input);
        if (input is InputEventMouseButton mouse && mouse.Pressed && mouse.Position.Y < 616 && !MiniMap.Expanded)
        {
            var actor = World.ActorAt(mouse.Position);
            if (actor == null && !Input.IsPhysicalKeyPressed(Key.Shift) && mouse.ButtonIndex is MouseButton.Left or MouseButton.Right &&
                ConfirmGroundMove(World.CellAt(mouse.Position), mouse.ButtonIndex == MouseButton.Left ? preferences.DefaultRun : !preferences.DefaultRun))
            { GetViewport().SetInputAsHandled(); return; }
            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (Input.IsPhysicalKeyPressed(Key.Shift)) Attack(Direction(State.User.Location, World.CellAt(mouse.Position)));
                else if (actor != null) SelectTarget(actor.Id);
                else SetDestination(World.CellAt(mouse.Position), preferences.DefaultRun);
            }
            else if (mouse.ButtonIndex == MouseButton.Right)
            {
                var point = World.CellAt(mouse.Position);
                if (actor?.Kind == ObjectType.Player || !preferences.DefaultRun && Distance(State.User.Location, point) <= 2) Face(point);
                else SetDestination(point, !preferences.DefaultRun);
            }
        }
        if (input is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (MenuShortcut(key)) { GetViewport().SetInputAsHandled(); return; }
        switch (key.PhysicalKeycode)
        {
            case Key.I: inventory.Visible = !inventory.Visible; break;
            case Key.C: ToggleEquipmentPage(0); break;
            case Key.K: ToggleEquipmentPage(3); break;
            case Key.M: MiniMap.SetExpanded(!MiniMap.Expanded); break;
            case Key.Q: if (questList.Visible) CloseQuests(); else OpenQuestJournal(); break;
            case Key.G: Send(new C.PickUp()); break;
            case Key.P: if (group.Visible) group.Hide(); else OpenGroup(); break;
            case Key.T: OpenTrade(); break;
            case Key.H: if (guild.Visible) CloseGuild(); else OpenGuild(); break;
            case Key.Enter: chat.GrabFocus(); break;
            case Key.Space: Attack(State.User.Direction); break;
            case Key.Escape: CloseEquipment(); CancelQuestNavigation(); ClosePublicShop(); CloseSettings(); MiniMap.SetExpanded(false); MiniMap.Route = []; pendingAttack = null; destination = null; actionTarget = 0; npc.Hide(); shopMode = null; CloseStorage(); CloseQuests(); questBag.Hide(); CloseGroup(); CloseSocial(); tradeMenu.Hide(); CancelTrade(); if (guild.Visible) CloseGuild(); break;
            default:
                if (key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key6)
                { UseQuickItem((int)(key.PhysicalKeycode - Key.Key1)); }
                else if (key.PhysicalKeycode >= Key.F1 && key.PhysicalKeycode <= Key.F12)
                {
                    if ((int)(key.PhysicalKeycode - Key.F1 + 1) > Assets.FunctionKeys) return;
                    var binding = SkillKeys.FunctionKey((int)(key.PhysicalKeycode - Key.F1 + 1), key.ShiftPressed);
                    if (binding == 0) return;
                    if (HeroInventoryContext)
                    {
                        var number = (int)(key.PhysicalKeycode - Key.F1 + 1);
                        if (number <= 8 && !key.ShiftPressed) CastSubjectSkill(SubjectMagics.FindIndex(m => m.Key == number + 16));
                    }
                    else { var index = State.User.Magics.FindIndex(m => m.Key == binding); if (index >= 0) Cast(index); }
                }
                break;
        }
    }
    public override void _Notification(int what) { if (what == NotificationWMWindowFocusOut) StopKeyboardMovement(); if (what == NotificationWMCloseRequest) _ = Quit(); }
    public async Task Quit()
    {
        connectionCancellation?.Cancel();
        StopAudio();
        await Connection.Send(new C.Disconnect()); await Task.Delay(150); GetTree().Quit();
    }
    private void StopAudio()
    {
        musicGeneration++;
        effectGeneration++; soundPlayedAt.Clear();
        foreach (var player in effectPlayers.Prepend(music)) { player.Stop(); player.Stream = null; }
    }
    public override void _ExitTree()
    {
        GetTree().NodeAdded -= StyleOptionPopup;
        connectionCancellation?.Cancel();
        Connection.Dispose();
        StopAudio();
        Assets.Dispose();
    }
}
