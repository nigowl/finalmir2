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
    private LineEdit account = null!, password = null!, server = null!, characterName = null!, chat = null!;
    private OptionButton characterClass = null!, gender = null!;
    private Label status = null!, vitals = null!, gold = null!, location = null!, resourceStatus = null!;
    private RichTextLabel messages = null!;
    private VBoxContainer characterList = null!, npcText = null!, npcGoods = null!, skillList = null!;
    private Control equipmentSlots = null!;
    private Control characterCreation = null!;
    private OriginalArt paperdoll = null!, characterPreview = null!;
    private Label equipmentName = null!, attributes = null!, bagGold = null!;
    private readonly List<Button> equipmentButtons = new();
    private int selectedCharacter;
    private readonly List<(Button button, int slot)> inventorySlots = new();
    private readonly List<(TextureRect rect, string library, int index)> pictures = new();
    private readonly List<(Button button, UserItem item)> icons = new();
    private AudioStreamPlayer music = null!, sound = null!;
    private Point? destination;
    private uint actionTarget;
    private bool running, moving;
    private long moveTime, attackTime, spellTime;
    private long lastMoved;
    private Point confirmedLocation;
    private int revision = -1;
    private PanelType? shopMode;
    private ulong draggedItem;
    private bool connecting;
    private int musicGeneration;
    public event Action<Packet>? PacketReceived;

    public override void _Ready()
    {
        ConfigureDisplay();
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Ignore;
        GetTree().AutoAcceptQuit = false;
        var cache = Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? ProjectSettings.GlobalizePath("user://cache");
        Assets = new ResourceCache(cache);
        music = new AudioStreamPlayer { VolumeDb = -20 }; sound = new AudioStreamPlayer { VolumeDb = -12 };
        AddChild(music); AddChild(sound);
        music.Finished += () => music.Play();
        var theme = new Theme(); theme.DefaultFontSize = 13;
        Theme = theme;
        BuildLogin(); BuildSelection(); BuildGame();
        State.Message += text => Log(text);
        var address = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--server="))?[9..]
            ?? Environment.GetEnvironmentVariable("MIR2_SERVER") ?? "http://127.0.0.1:5080";
        server.Text = address;
        _ = Connect(address);
        if (OS.GetCmdlineUserArgs().Any(a => a is "--smoke" or "--verify-save" or "--reference-world"))
            AddChild(new NativeSmoke { Client = this });
    }

    private Label Text(Control parent, string text, float x, float y, float width = 250)
    {
        var label = new Label { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 24), MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", new GColor("eadbb6")); parent.AddChild(label); return label;
    }
    private Button Button(Control parent, string text, float x, float y, float width, Action action)
    {
        var button = new Button { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 28), FocusMode = FocusModeEnum.None };
        button.Pressed += () => { action(); _ = PlaySound(10103); }; parent.AddChild(button); return button;
    }
    private OriginalButton ArtButton(Control parent, string library, int index, float x, float y, string hint, Action action)
    {
        var button = new OriginalButton { Assets = Assets, Library = library, Index = index, Position = new Vector2(x, y), Size = new Vector2(24, 20), TooltipText = hint };
        button.Pressed += () => { action(); _ = PlaySound(10103); }; parent.AddChild(button); return button;
    }
    private LineEdit Edit(Control parent, float x, float y, float width, string placeholder, bool secret = false)
    {
        var edit = new LineEdit { Position = new Vector2(x, y), Size = new Vector2(width, 28), PlaceholderText = placeholder, Secret = secret };
        parent.AddChild(edit); return edit;
    }
    private TextureRect Picture(Control parent, string library, int index, float x, float y)
    {
        var image = new TextureRect { Position = new Vector2(x, y), MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Nearest };
        parent.AddChild(image); pictures.Add((image, library, index)); return image;
    }
    private Control Panel(Control parent, float x, float y, float width, float height)
    {
        var panel = new Panel { Position = new Vector2(x, y), Size = new Vector2(width, height) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new GColor(0.05f, 0.04f, 0.025f, 0.96f), BorderColor = new GColor("82704c"), BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1 });
        parent.AddChild(panel); return panel;
    }
    private static void CellStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new GColor(1, 1, 0.5f, 0.12f), BorderColor = new GColor("b59b5d"), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 });
        button.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new GColor(1, 1, 0.5f, 0.25f) });
    }
    private void BuildLogin()
    {
        login = new Control { Size = new Vector2(1024, 768), MouseFilter = MouseFilterEnum.Ignore }; AddChild(login);
        var frame = Panel(login, 348, 274, 328, 220);
        Picture(frame, "Prguse", 1084, 0, 0);
        Picture(frame, "Title", 30, 100, 12); Picture(frame, "Title", 31, 52, 83); Picture(frame, "Title", 32, 43, 105);
        account = Edit(frame, 85, 83, 136, "账号"); account.MaxLength = 15;
        password = Edit(frame, 85, 106, 136, "密码", true); password.MaxLength = 15;
        foreach (var edit in new[] { account, password })
        {
            edit.Size = new Vector2(136, 18);
            foreach (var state in new[] { "normal", "focus", "read_only" }) edit.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
            edit.TextSubmitted += _ => Login(account.Text, password.Text);
        }
        ArtButton(frame, "Title", 320, 227, 81, "登录", () => Login(account.Text, password.Text));
        ArtButton(frame, "Title", 323, 60, 163, "使用当前填写的账号和密码注册", () => Register(account.Text, password.Text));
        ArtButton(frame, "Title", 329, 166, 189, "退出游戏", () => _ = Quit());
        Text(login, "Godot 原生客户端 · 原 TCP 协议 · 原版资源按需缓存", 320, 520, 490);
        server = Edit(login, 308, 666, 300, "资源服务地址");
        Button(login, "连接", 620, 666, 85, () => _ = Connect(server.Text));
        status = Text(login, "连接中…", 230, 712, 700);
        BuildPasswordChange();
    }
    private void BuildSelection()
    {
        selection = new Control { Size = new Vector2(1024, 768), Visible = false, MouseFilter = MouseFilterEnum.Ignore }; AddChild(selection);
        characterPreview = new OriginalArt { Assets = Assets, State = State, Page = "select" }; selection.AddChild(characterPreview);
        Picture(selection, "Title", 40, 468, 20); Text(selection, "Crystal Mir 2", 432, 60, 155);
        characterList = new VBoxContainer { Position = new Vector2(637, 194), Size = new Vector2(278, 390) }; characterList.AddThemeConstantOverride("separation", 24); selection.AddChild(characterList);
        ArtButton(selection, "Title", 340, 132, 736, "进入游戏", () => { if (State.Characters.Count > selectedCharacter) Send(new C.StartGame { CharacterIndex = State.Characters[selectedCharacter].Index }); });
        ArtButton(selection, "Title", 343, 296, 736, "创建角色", () => characterCreation.Show());
        ArtButton(selection, "Title", 352, 788, 736, "退出", () => _ = Quit());
        var create = characterCreation = Panel(selection, 105, 630, 810, 70); create.Hide();
        characterName = Edit(create, 15, 18, 210, "新角色名称（3–15 位）"); characterName.MaxLength = 15;
        characterClass = new OptionButton { Position = new Vector2(240, 18), Size = new Vector2(120, 28) };
        foreach (var job in new[] { "战士", "法师", "道士" }) characterClass.AddItem(job); create.AddChild(characterClass);
        gender = new OptionButton { Position = new Vector2(375, 18), Size = new Vector2(90, 28) }; gender.AddItem("男"); gender.AddItem("女"); create.AddChild(gender);
        Button(create, "创建角色", 485, 18, 120, () => Send(new C.NewCharacter { Name = characterName.Text.Trim(), Class = (MirClass)characterClass.Selected, Gender = (MirGender)gender.Selected }));
        Button(create, "取消", 665, 18, 120, () => create.Hide());
    }
    private void BuildGame()
    {
        game = new Control { Size = new Vector2(1024, 768), Visible = false, MouseFilter = MouseFilterEnum.Ignore }; AddChild(game);
        var viewport = new Control { Size = new Vector2(1024, 768), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore }; game.AddChild(viewport);
        World = new WorldView { State = State, Assets = Assets }; viewport.AddChild(World);
        Picture(game, "Prguse", 1, 0, 616);
        game.AddChild(new OriginalArt { Assets = Assets, State = State });
        vitals = Text(game, "", 3, 652, 220);
        gold = Text(game, "", 665, 737, 180);
        location = Text(game, "", 7, 5, 560);
        resourceStatus = Text(game, "", 7, 28, 700);
        messages = new RichTextLabel { Position = new Vector2(235, 670), Size = new Vector2(620, 53), ScrollFollowing = true, MouseFilter = MouseFilterEnum.Ignore };
        game.AddChild(messages);
        chat = Edit(game, 235, 724, 415, "聊天：Enter 发送"); chat.MaxLength = Globals.MaxChatLength;
        chat.TextSubmitted += text => { if (text.Trim().Length > 0) Send(new C.Chat { Message = text.Trim() }); chat.Text = ""; chat.ReleaseFocus(); };
        ArtButton(game, "Prguse", 1903, 928, 692, "背包 I", () => inventory.Visible = !inventory.Visible);
        ArtButton(game, "Prguse", 1900, 905, 692, "角色 C", () => { if (equipment.Visible) equipment.Hide(); else ShowEquipmentPage(0); });
        ArtButton(game, "Prguse", 1906, 951, 692, "技能 K", () => ShowEquipmentPage(3));
        var menu = new PopupMenu(); game.AddChild(menu);
        foreach (var label in new[] { "拾取 G", "回城复活", "返回角色选择", "退出游戏", "组队 P", "交易 T", "行会 H" }) menu.AddItem(label);
        menu.IdPressed += id => { switch (id) { case 0: Send(new C.PickUp()); break; case 1: Send(new C.TownRevive()); break; case 2: Send(new C.LogOut()); break; case 3: _ = Quit(); break; case 4: OpenGroup(); break; case 5: OpenTrade(); break; case 6: OpenGuild(); break; } };
        ArtButton(game, "Prguse", 1960, 969, 651, "游戏菜单", () => menu.PopupCentered());
        var help = Text(game, "左键行走/攻击/NPC · 右键奔跑 · Shift+左键强制攻击 · F1–F8 技能 · 1–6 快捷栏", 230, 590, 780);
        help.AddThemeColorOverride("font_shadow_color", Colors.Black); help.AddThemeConstantOverride("shadow_offset_x", 1); help.AddThemeConstantOverride("shadow_offset_y", 1);
        MiniMap = new GameMiniMap { Assets = Assets, State = State, Position = new Vector2(834, 48), Size = new Vector2(180, 140), MouseFilter = MouseFilterEnum.Ignore };
        game.AddChild(MiniMap);
        BuildInventory(); BuildStorage();
        equipment = Panel(game, 760, 35, 264, 380); equipment.Visible = false;
        Picture(equipment, "Title", 504, 0, 0);
        paperdoll = new OriginalArt { Assets = Assets, State = State, Page = "equipment" }; equipment.AddChild(paperdoll);
        equipmentName = Text(equipment, "", 65, 12, 165);
        var closeEquipment = Button(equipment, "", 241, 3, 20, () => equipment.Hide()); CellStyle(closeEquipment);
        equipmentSlots = new Control { Size = new Vector2(264, 380), MouseFilter = MouseFilterEnum.Ignore }; equipment.AddChild(equipmentSlots);
        Vector2[] positions = [new(123,7),new(163,7),new(203,7),new(203,134),new(203,98),new(8,170),new(203,170),new(8,206),new(203,206),new(8,242),new(88,242),new(48,242),new(128,242),new(203,62)];
        for (var i = 0; i < positions.Length; i++)
        {
            var slot = i;
            var button = new Button { Position = positions[i] + new Vector2(8, 90), Size = new Vector2(36, 32), FocusMode = FocusModeEnum.None, ExpandIcon = true };
            CellStyle(button); button.Pressed += () => RemoveEquipment(slot); equipmentSlots.AddChild(button); equipmentButtons.Add(button);
        }
        attributes = Text(equipment, "", 20, 102, 230); attributes.Size = new Vector2(230, 265); attributes.Hide();
        for (var i = 0; i < 4; i++) { var page = i; var tab = Button(equipment, "", 8 + i * 62, 70, 62, () => ShowEquipmentPage(page)); tab.Size = new Vector2(62, 20); CellStyle(tab); }
        npc = Panel(game, 25, 45, 440, 225); npc.Visible = false;
        Picture(npc, "Prguse", 995, 0, 0);
        Text(npc, "NPC 对话 / 商店", 15, 7); Button(npc, "×", 400, 5, 32, () => { npc.Hide(); shopMode = null; });
        var scroll = new ScrollContainer { Position = new Vector2(12, 40), Size = new Vector2(415, 175) }; npc.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(content);
        npcText = new VBoxContainer(); content.AddChild(npcText); npcGoods = new VBoxContainer(); content.AddChild(npcGoods);
        skills = new ScrollContainer { Position = new Vector2(8, 92), Size = new Vector2(248, 280), Visible = false }; equipment.AddChild(skills);
        skillList = new VBoxContainer { CustomMinimumSize = new Vector2(230, 260) }; skills.AddChild(skillList);
        BuildQuests(); BuildGroup(); BuildTrade(); BuildGuild();
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
        inventory = Panel(game, 700, 35, 316, 236); inventory.Visible = false;
        Picture(inventory, "Title", 196, 0, 0); Text(inventory, "背包", 9, 5, 120);
        ArtButton(inventory, "Title", 739, 146, 7, "任务物品", () => { questBag.Visible = !questBag.Visible; RenderQuestBag(); });
        var closeBag = Button(inventory, "", 294, 3, 19, () => inventory.Hide()); closeBag.Size = new Vector2(19, 19); CellStyle(closeBag);
        bagGold = Text(inventory, "", 27, 207, 120);
        for (var slot = 0; slot < 40; slot++)
        {
            var index = slot + 6;
            var button = ItemButton(inventory, index, 9 + slot % 8 * 37, 37 + slot / 8 * 33);
            inventorySlots.Add((button, index));
        }
        Picture(game, "Prguse", 1932, 230, 618);
        for (var i = 0; i < 6; i++) inventorySlots.Add((ItemButton(game, i, 242 + i * 35, 621), i));
    }
    private Button ItemButton(Control parent, int slot, float x, float y)
    {
        var button = new Button { Position = new Vector2(x, y), Size = new Vector2(36, 32), FocusMode = FocusModeEnum.None, ExpandIcon = true };
        CellStyle(button);
        button.GuiInput += input =>
        {
            if (input is not InputEventMouseButton e || e.ButtonIndex != MouseButton.Left || State.User == null) return;
            if (e.Pressed)
            {
                draggedItem = slot < State.User.Inventory.Length ? State.User.Inventory[slot]?.UniqueID ?? 0 : 0;
                if (e.DoubleClick && draggedItem != 0) { UseItem(draggedItem); draggedItem = 0; }
            }
            else if (draggedItem != 0)
            {
                var destinationButton = inventorySlots.FirstOrDefault(pair => pair.button.IsVisibleInTree() && pair.button.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
                var from = Array.FindIndex(State.User.Inventory, item => item?.UniqueID == draggedItem);
                var storageSlot = StorageCellAtMouse();
                var tradeSlot = TradeCellAtMouse();
                var guildSlot = GuildCellAtMouse();
                if (guildSlot >= 0) StoreGuildItem(from, guildStorageRow * 8 + guildSlot);
                else if (tradeSlot >= 0) OfferTradeItem(from, tradeSlot);
                else if (storageSlot >= 0) StoreInventory(from, storagePage * Globals.StorageGridSize + storageSlot);
                else if (destinationButton.button != null && from >= 0 && destinationButton.slot != from)
                    Send(new C.MoveItem { Grid = MirGridType.Inventory, From = from, To = destinationButton.slot });
                draggedItem = 0;
            }
        };
        parent.AddChild(button); return button;
    }

    public async Task Connect(string address)
    {
        if (connecting) return;
        connecting = true;
        try
        {
            Connection.Dispose();
            ResetLoginPassword();
            State.Reset();
            foreach (var (rect, _, _) in pictures) if (IsInstanceValid(rect)) rect.Texture = null;
            HandshakeReady = false; status.Text = "连接资源与原 TCP 服务端…";
            await Assets.Initialize(address);
            await Connection.Connect(Assets.Host, Assets.GamePort);
            _ = PlaySound(10146, true);
        }
        catch (Exception error) { status.Text = error.Message; Log(error.Message); }
        finally { connecting = false; }
    }
    public void Login(string id, string secret)
    {
        if (!ValidateCredentials(id, secret)) return;
        account.Text = id; password.Text = secret;
        Send(new C.Login { AccountID = id, Password = secret });
    }
    public void Register(string id, string secret)
    {
        if (!ValidateCredentials(id, secret)) return;
        account.Text = id; password.Text = secret;
        Send(new C.NewAccount { AccountID = id, Password = secret });
    }
    private bool ValidateCredentials(string id, string secret)
    {
        if (!HandshakeReady) { status.Text = "尚未连接，请启动后端后重新连接。"; return false; }
        if (!Regex.IsMatch(id, "^[A-Za-z0-9]{3,15}$") || !Regex.IsMatch(secret, "^[A-Za-z0-9]{5,15}$"))
        { status.Text = "账号须为3–15位，密码5–15位，均限英文字母或数字。"; return false; }
        return true;
    }
    public void Send(Packet packet) { if (HandshakeReady || packet is C.ClientVersion) _ = Connection.Send(packet); }
    public override void _Process(double delta)
    {
        UpdateDisplay(delta);
        for (var handled = 0; handled < 1024 && Connection.Received.TryDequeue(out var packet); handled++)
        {
            State.Apply(packet); Handle(packet); PacketReceived?.Invoke(packet);
        }
        while (Connection.Errors.TryDequeue(out var error)) { Connection.Received.Clear(); State.Reset(); HandshakeReady = false; ResetLoginPassword(); SetScene("login"); status.Text = error; Log(error); }
        if (Assets.Errors.TryDequeue(out var resourceError)) { resourceStatus.Text = resourceError; GD.PrintErr(resourceError); }
        pictures.RemoveAll(p => !IsInstanceValid(p.rect));
        foreach (var (rect, library, index) in pictures)
            if (rect.Texture == null) { var image = Assets.Sprite(library, index); if (image != null) { rect.Texture = image.Texture; rect.Size = image.Size; } }
        foreach (var (button, item) in icons)
            if (IsInstanceValid(button) && button.Icon == null && item.Info != null) button.Icon = Assets.Sprite("Items", item.Info.Image)?.Texture;
        if (State.User != null && scene == "game")
        {
            var user = State.User; var stats = State.CalculateStats();
            vitals.Text = $"HP {user.HP}/{Math.Max(user.HP, stats[Stat.HP])}\nMP {user.MP}/{Math.Max(user.MP, stats[Stat.MP])}\n\n{user.Name} Lv.{user.Level}";
            gold.Text = $"金币 {user.Gold}";
            bagGold.Text = user.Gold.ToString(); equipmentName.Text = $"{user.Name}  Lv.{user.Level}";
            location.Text = $"{State.Map?.Title}  {user.Location.X},{user.Location.Y}";
            resourceStatus.Text = World.Loading ? "地图按需加载中…" : World.LoadError.Length > 0 ? World.LoadError : $"微端缓存 · {Assets.Downloads} 个文件 / {Assets.DownloadBytes / 1048576f:F1} MB · {State.Ping} ms";
            if (revision != State.InventoryRevision) { RenderInventory(); RenderStorage(); RenderQuestBag(); RenderTrade(); RenderGuildStorage(); revision = State.InventoryRevision; }
            ProcessStorage(); ProcessQuests(); ProcessGroup(); ProcessTrade(); ProcessGuild(); ProcessActions();
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (scene == "game" || !Assets.Ready) return;
        var background = Assets.Sprite(scene == "login" ? "ChrSel" : "Prguse", scene == "login" ? 0 : 65);
        if (background != null) DrawTexture(background.Texture, Vector2.Zero);
    }
    private void Handle(Packet packet)
    {
        TradePacket(packet);
        GuildPacket(packet);
        switch (packet)
        {
            case S.Connected: Send(new C.ClientVersion { VersionHash = [] }); break;
            case S.Disconnect p: HandshakeReady = false; Connection.Dispose(); ResetLoginPassword(); SetScene("login"); status.Text = $"服务端断开连接：{p.Reason}。点击连接后重新登录。"; break;
            case S.ClientVersion p: HandshakeReady = p.Result == 1; status.Text = HandshakeReady ? "原 TCP 服务端已连接" : "客户端版本不匹配"; break;
            case S.NewAccount p:
                if (p.Result == 8) Login(account.Text, password.Text); else status.Text = $"注册失败：{p.Result}（7 表示账号已存在）";
                break;
            case S.Login p:
                status.Text = p.Result == 5 ? "管理端要求修改密码，请设置新密码后重新登录。" : $"登录失败：{p.Result}（3 账号不存在，4 密码错误）";
                if (p.Result == 5) passwordChange.Show();
                break;
            case S.ChangePassword p: LoginPasswordResult(p); break;
            case S.ChangePasswordBanned p: ResetLoginPassword(); status.Text = p.Reason; break;
            case S.LoginBanned p: status.Text = p.Reason; break;
            case S.LoginSuccess: SetScene("selection"); RenderCharacters(); _ = PlaySound(10147, true); break;
            case S.NewCharacterSuccess: characterCreation.Hide(); RenderCharacters(); break;
            case S.NewCharacter p: Log($"角色创建失败：{p.Result}"); break;
            case S.StartGame p: if (p.Result != 4) Log($"进入游戏失败：{p.Result}"); break;
            case S.UserInformation p: SetScene("game"); moving = false; confirmedLocation = p.Location; lastMoved = 0; RenderInventory(); RenderSkills(); SetMapMusic(); break;
            case S.MapInformation p: _ = World.Load(p.FileName); break;
            case S.MapChanged p: npc.Hide(); shopMode = null; CloseStorage(); CloseQuests(); destination = null; actionTarget = 0; moving = false; lastMoved = 0; confirmedLocation = p.Location; _ = World.Load(p.FileName); SetMapMusic(); break;
            case S.UserLocation p: moving = false; lastMoved = p.Location == confirmedLocation ? 0 : Environment.TickCount64; confirmedLocation = p.Location; break;
            case S.DamageIndicator p: World.ShowDamage(p.ObjectID, p.Damage); break;
            case S.Magic p:
                if (p.Cast && State.User != null) { World.ShowSpell(p.Spell, State.User.ObjectID, p.TargetID, p.Target); _ = PlaySound(20000 + (int)p.Spell * 10); }
                break;
            case S.ObjectMagic p:
                if (p.Cast && (p.ObjectID != State.User?.ObjectID || p.SelfBroadcast)) World.ShowSpell(p.Spell, p.ObjectID, p.TargetID, p.Target);
                break;
            case S.NPCResponse p: RenderNpc(p.Page); break;
            case S.NPCGoods p: RenderGoods(p); break;
            case S.NPCStorage: OpenStorage(); break;
            case S.StorageUnlockResult: case S.StoragePasswordResult: StoragePasswordResult(packet); break;
            case S.StoreItem: case S.TakeBackItem: storageBusy = false; RenderStorage(); break;
            case S.MoveItem p: if (p.Grid == MirGridType.Storage) { storageBusy = false; RenderStorage(); } break;
            case S.NPCSell: shopMode = PanelType.Sell; Log("双击背包中的物品出售一件。"); inventory.Show(); break;
            case S.NPCRepair: shopMode = PanelType.Repair; Log("点击装备进行修理。"); ShowEquipmentPage(0); break;
            case S.LogOutSuccess: SetScene("selection"); RenderCharacters(); break;
            case S.LogOutFailed: Log("战斗中暂时无法退出角色，请稍候。"); break;
            case S.ReturnToLogin: HandshakeReady = true; SetScene("login"); break;
            case S.NewMagic: case S.MagicLeveled: RenderSkills(); break;
            case S.UseItem p: if (p.Success) _ = PlaySound(10107); break;
            case S.LevelChanged: _ = PlaySound(10156); break;
        }
    }
    private void SetScene(string name)
    {
        scene = name; login.Visible = name == "login"; selection.Visible = name == "selection"; game.Visible = name == "game";
        destination = null; actionTarget = 0; GetViewport().GuiReleaseFocus();
        CloseStorage(); CloseQuests(); inventory.Hide(); equipment.Hide(); npc.Hide(); questBag.Hide(); CloseGroup(); CloseTradeUi(); CloseGuildUi(); shopMode = null;
    }
    private void RenderCharacters()
    {
        Clear(characterList);
        selectedCharacter = Math.Clamp(selectedCharacter, 0, Math.Max(0, State.Characters.Count - 1));
        characterPreview.Character = State.Characters.ElementAtOrDefault(selectedCharacter);
        if (State.Characters.Count == 0) characterCreation.Show();
        for (var i = 0; i < State.Characters.Count; i++)
        {
            var index = i; var character = State.Characters[i];
            var row = new Control { CustomMinimumSize = new Vector2(278, 80) }; characterList.AddChild(row);
            Picture(row, "Title", 660 + (int)character.Class + (i == selectedCharacter ? 5 : 0), 0, 0);
            Text(row, character.Name, 107, 9, 165); Text(row, character.Level.ToString(), 107, 28, 30);
            Text(row, character.Class switch { MirClass.Warrior => "战士", MirClass.Wizard => "法师", MirClass.Taoist => "道士", _ => character.Class.ToString() }, 178, 28, 100);
            var button = Button(row, "", 0, 0, 278, () => { selectedCharacter = index; RenderCharacters(); }); button.Size = new Vector2(278, 80); CellStyle(button);
        }
    }
    private static void Clear(Node node) { foreach (var child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); } }
    private string ItemText(UserItem item) => item.Info == null ? $"物品 {item.ItemIndex}" :
        $"{item.Info.FriendlyName} ×{item.Count}\n要求 {item.Info.RequiredType} {item.Info.RequiredAmount} · {item.Info.RequiredClass}\n耐久 {item.CurrentDura / 1000f:0.###}/{item.MaxDura / 1000f:0.###}\n" +
        string.Join("\n", item.Info.Stats.Values.Select(s => $"{s.Key}: {item.GetTotal(s.Key)}")) + "\n" + item.Info.ToolTip;
    private void RenderInventory()
    {
        if (State.User == null) return;
        icons.Clear();
        foreach (var (button, slot) in inventorySlots)
        {
            var item = slot < State.User.Inventory.Length ? State.User.Inventory[slot] : null;
            button.Icon = null; button.Text = item?.Count > 1 ? item.Count.ToString() : "";
            button.TooltipText = item != null ? ItemText(item) : "空格";
            if (item != null) icons.Add((button, item));
        }
        for (var i = 0; i < State.User.Equipment.Length; i++)
        {
            var item = State.User.Equipment[i]; var button = equipmentButtons[i];
            button.Icon = null; button.TooltipText = item == null ? ((EquipmentSlot)i).ToString() : ItemText(item);
            if (item != null) icons.Add((button, item));
        }
    }
    private void RemoveEquipment(int slot)
    {
        var item = State.User?.Equipment[slot]; if (item == null) return;
        if (shopMode == PanelType.Repair) { Send(new C.RepairItem { UniqueID = item.UniqueID }); return; }
        var to = Array.IndexOf(State.User!.Inventory, null);
        if (to >= 0) Send(new C.RemoveItem { Grid = MirGridType.Inventory, UniqueID = item.UniqueID, To = to }); else Log("背包已满。");
    }
    private void ShowEquipmentPage(int page)
    {
        equipment.Show(); paperdoll.Visible = page == 0; equipmentSlots.Visible = page == 0;
        attributes.Visible = page is 1 or 2; skills.Visible = page == 3;
        if (page == 3) RenderSkills();
        else if (attributes.Visible && State.User is { } user)
        {
            var stats = State.CalculateStats();
            attributes.Text = $"HP  {user.HP}/{stats[Stat.HP]}\nMP  {user.MP}/{stats[Stat.MP]}\n" +
                string.Join("\n", new[] { Stat.MinAC, Stat.MaxAC, Stat.MinMAC, Stat.MaxMAC, Stat.MinDC, Stat.MaxDC, Stat.MinMC, Stat.MaxMC, Stat.MinSC, Stat.MaxSC, Stat.Accuracy, Stat.Agility }.Select(s => $"{s}   {stats[s]}")) +
                $"\n经验 {user.Experience}/{user.MaxExperience}";
        }
    }
    public void UseItem(ulong id)
    {
        var item = State.User?.Inventory.FirstOrDefault(i => i?.UniqueID == id);
        if (item?.Info == null) return;
        if (State.Trading) { OfferTradeItem(Array.FindIndex(State.User!.Inventory, i => i?.UniqueID == id)); return; }
        if (GuildStorageOpen) { StoreGuildItem(Array.FindIndex(State.User!.Inventory, i => i?.UniqueID == id)); return; }
        if (storage.Visible) { StoreInventory(Array.FindIndex(State.User!.Inventory, i => i?.UniqueID == id)); return; }
        if (shopMode == PanelType.Sell) { Send(new C.SellItem { UniqueID = id, Count = 1 }); return; }
        int? slot = item.Info.Type switch
        {
            ItemType.Weapon => 0, ItemType.Armour => 1, ItemType.Helmet => 2, ItemType.Torch => 3,
            ItemType.Necklace => 4, ItemType.Bracelet => State.User!.Equipment[5] == null ? 5 : 6,
            ItemType.Ring => State.User!.Equipment[7] == null ? 7 : 8, ItemType.Amulet => 9,
            ItemType.Belt => 10, ItemType.Boots => 11, ItemType.Stone => 12, ItemType.Mount => 13, _ => null
        };
        Send(slot.HasValue ? new C.EquipItem { Grid = MirGridType.Inventory, UniqueID = id, To = slot.Value }
            : new C.UseItem { Grid = MirGridType.Inventory, UniqueID = id });
    }
    private void RenderSkills()
    {
        Clear(skillList);
        if (State.User == null) return;
        for (var i = 0; i < State.User.Magics.Count; i++)
        {
            var index = i; var magic = State.User.Magics[i];
            var row = new HBoxContainer(); skillList.AddChild(row);
            var button = new Button { Text = $"{magic.Name} Lv.{magic.Level + 1}", CustomMinimumSize = new Vector2(140, 32) };
            button.Pressed += () => Cast(index); row.AddChild(button);
            var key = new OptionButton(); key.AddItem("无");
            for (var k = 1; k <= 16; k++) key.AddItem(k <= 8 ? $"F{k}" : $"Shift+F{k - 8}");
            key.Selected = magic.Key <= 16 ? magic.Key : 0;
            key.ItemSelected += selected => { BindMagic(index, (byte)selected); RenderSkills(); }; row.AddChild(key);
        }
    }
    public void BindMagic(int index, byte key)
    {
        if (State.User == null || index < 0 || index >= State.User.Magics.Count || key > 16) return;
        var magic = State.User.Magics[index];
        Send(new C.MagicKey { Spell = magic.Spell, Key = key, OldKey = magic.Key });
        foreach (var other in State.User.Magics) if (other.Key == key) other.Key = 0;
        magic.Key = key;
    }
    private void RenderNpc(List<string> page)
    {
        Clear(npcText); Clear(npcGoods); shopMode = null; npc.Visible = page.Count > 0;
        foreach (var raw in page)
        {
            var line = Regex.Replace(raw, @"\{([^{}]+)/[A-Za-z]+\}", "$1");
            var row = new HBoxContainer(); npcText.AddChild(row); var offset = 0;
            foreach (Match link in Regex.Matches(line, @"<([^<>]+)/@([^<>]+)>"))
            {
                if (link.Index > offset) row.AddChild(new Label { Text = line[offset..link.Index] });
                var key = "[@" + link.Groups[2].Value + "]";
                var button = new Button { Text = link.Groups[1].Value };
                button.Pressed += () => Send(new C.CallNPC { ObjectID = State.NpcId, Key = key }); row.AddChild(button);
                offset = link.Index + link.Length;
            }
            if (offset < line.Length) row.AddChild(new Label { Text = line[offset..] });
        }
    }
    private void RenderGoods(S.NPCGoods packet)
    {
        Clear(npcGoods); npc.Show();
        foreach (var item in packet.List)
        {
            State.Bind(item);
            var single = item.Clone(); single.Count = 1;
            var price = (uint)Math.Clamp(single.Price() * (double)packet.Rate, 0, uint.MaxValue);
            var button = new Button { Text = $"{item.Info?.FriendlyName} ×1 · {price} 金币", CustomMinimumSize = new Vector2(395, 31), TooltipText = ItemText(item) };
            button.Pressed += () => Send(new C.BuyItem { ItemIndex = item.UniqueID, Count = 1, Type = packet.Type }); npcGoods.AddChild(button);
        }
    }
    public void Log(string text)
    {
        if (messages == null) return;
        messages.AppendText(text + "\n");
        if (messages.GetLineCount() > 100) messages.Text = messages.Text[(messages.Text.IndexOf('\n') + 1)..];
        if (scene != "game" && status != null) status.Text = text;
    }
    private async Task PlaySound(int id, bool background = false)
    {
        var generation = background ? ++musicGeneration : 0;
        try
        {
            var audio = await Assets.Sound(id); if (audio == null) return;
            if (!IsInsideTree() || (background && generation != musicGeneration)) { audio.Dispose(); return; }
            var player = background ? music : sound;
            player.Stop(); var previous = player.Stream; player.Stream = audio; previous?.Dispose(); player.Play();
        }
        catch (Exception error) { GD.PrintErr(error.Message); }
    }
    private void SetMapMusic()
    {
        musicGeneration++;
        music.Stop(); var previous = music.Stream; music.Stream = null; previous?.Dispose();
        if (State.Map?.Music > 0) _ = PlaySound(State.Map.Music, true);
    }
    public static MirDirection Direction(Point from, Point to)
    {
        var x = Math.Sign(to.X - from.X); var y = Math.Sign(to.Y - from.Y);
        return (x, y) switch { (0, -1) => MirDirection.Up, (1, -1) => MirDirection.UpRight, (1, 0) => MirDirection.Right,
            (1, 1) => MirDirection.DownRight, (0, 1) => MirDirection.Down, (-1, 1) => MirDirection.DownLeft, (-1, 0) => MirDirection.Left, _ => MirDirection.UpLeft };
    }
    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    public void SetDestination(Point point, bool run = false) { destination = point; actionTarget = 0; running = run; }
    public void SelectTarget(uint id) { actionTarget = id; World.Selected = id; destination = null; }
    private void ProcessActions()
    {
        var me = State.Me; var user = State.User;
        if (me == null || user == null || me.Dead || World.Loading || World.Map == null || !HandshakeReady) return;
        var now = Environment.TickCount64;
        if (moving && now - moveTime < 900) return;
        if (now - moveTime < 610) return;
        if (actionTarget != 0)
        {
            if (!State.Actors.TryGetValue(actionTarget, out var target) || target.Hidden) { actionTarget = 0; return; }
            var distance = Distance(user.Location, target.Location);
            if (target.Kind == ObjectType.Merchant && distance <= 5)
            { State.NpcId = target.Id; Send(new C.CallNPC { ObjectID = target.Id, Key = "[@MAIN]" }); actionTarget = 0; return; }
            if (target.Kind == ObjectType.Item && distance == 0) { Send(new C.PickUp()); actionTarget = 0; return; }
            if (target.Kind == ObjectType.Monster && distance <= 1)
            {
                if (target.Dead) { Send(new C.Harvest { Direction = Direction(user.Location, target.Location) }); actionTarget = 0; }
                else Attack(Direction(user.Location, target.Location));
                return;
            }
            destination = target.Location;
            if (target.Kind != ObjectType.Item)
            {
                var adjacent = Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => new Point(target.Location.X + dx, target.Location.Y + dy)))
                    .Where(p => p != target.Location && World.Walkable(p)).OrderBy(p => Distance(user.Location, p)).ToArray();
                if (adjacent.Length == 0) return;
                destination = adjacent[0];
            }
        }
        if (destination == null) return;
        if (user.Location == destination) { destination = null; return; }
        // The original server requires a recent walking step before it accepts running.
        var step = World.NextStep(destination.Value, running && now - lastMoved < 700);
        if (step == null) { destination = null; return; }
        var dir = Direction(user.Location, step.Value);
        Send(Distance(user.Location, step.Value) == 2 ? new C.Run { Direction = dir } : new C.Walk { Direction = dir });
        moveTime = now; moving = true;
    }
    public void Attack(MirDirection direction)
    {
        if (State.Me == null || State.User == null || State.Me.Dead) return;
        var speed = Math.Max(550, 1400 - State.CalculateStats()[Stat.AttackSpeed] * 60 - Math.Min(370, State.User.Level * 14));
        if (Environment.TickCount64 - attackTime < speed) return;
        var spell = Spell.None;
        foreach (var candidate in new[] { Spell.Slaying, Spell.Thrusting, Spell.HalfMoon, Spell.CrossHalfMoon, Spell.DoubleSlash, Spell.TwinDrakeBlade, Spell.FlamingSword })
        {
            if (!State.ActiveSkills.Contains(candidate)) continue;
            if (candidate == Spell.Thrusting && !State.Actors.Values.Any(a => !a.Dead && a.Kind == ObjectType.Monster && a.Location == Functions.PointMove(State.User.Location, direction, 2))) continue;
            var magic = State.User.Magics.FirstOrDefault(m => m.Spell == candidate);
            if (magic != null && magic.BaseCost + magic.LevelCost * magic.Level <= State.User.MP) spell = candidate;
        }
        Send(new C.Attack { Direction = direction, Spell = spell }); attackTime = Environment.TickCount64;
        if (spell is Spell.Slaying or Spell.TwinDrakeBlade or Spell.FlamingSword) State.ActiveSkills.Remove(spell);
        State.Me.Direction = direction; State.Me.Action = MirAction.Attack1; State.Me.ActionTime = attackTime;
    }
    public void Cast(int index)
    {
        if (State.User == null || index >= State.User.Magics.Count || Environment.TickCount64 < spellTime) return;
        var magic = State.User.Magics[index];
        if (magic.Spell is Spell.Fencing or Spell.SpiritSword or Spell.Slaying or Spell.FatalSword or Spell.MPEater or Spell.Hemorrhage or Spell.Focus or Spell.Meditation) { Log("此技能为被动技能。"); return; }
        if (magic.Spell is Spell.Thrusting or Spell.HalfMoon or Spell.CrossHalfMoon or Spell.DoubleSlash or Spell.TwinDrakeBlade or Spell.FlamingSword or Spell.CounterAttack)
        {
            var enabled = !State.ActiveSkills.Contains(magic.Spell);
            Send(new C.SpellToggle { Spell = magic.Spell, CanUse = enabled });
            if (enabled) State.ActiveSkills.Add(magic.Spell); else State.ActiveSkills.Remove(magic.Spell);
            Log($"{magic.Name}: {(enabled ? "开启" : "关闭")}"); return;
        }
        destination = null; actionTarget = 0; moveTime = Environment.TickCount64;
        var targetId = World.Selected;
        var point = State.Actors.TryGetValue(targetId, out var selected) ? selected.Location : World.CellAt(World.GetGlobalMousePosition());
        if (magic.Spell == Spell.Healing) { targetId = State.User.ObjectID; point = State.User.Location; }
        if (State.Me != null) State.Me.Direction = Direction(State.User.Location, point);
        Send(new C.Magic { ObjectID = State.User.ObjectID, Spell = magic.Spell, Direction = Direction(State.User.Location, point), TargetID = targetId, Location = point });
        spellTime = Environment.TickCount64 + Math.Max(550, magic.Delay);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (scene != "game" || State.User == null) return;
        if (input is InputEventMouseButton mouse && mouse.Pressed && mouse.Position.Y < 616)
        {
            var actor = World.ActorAt(mouse.Position);
            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (Input.IsPhysicalKeyPressed(Key.Shift)) Attack(Direction(State.User.Location, World.CellAt(mouse.Position)));
                else if (actor != null) SelectTarget(actor.Id);
                else SetDestination(World.CellAt(mouse.Position));
            }
            else if (mouse.ButtonIndex == MouseButton.Right)
            {
                var point = World.CellAt(mouse.Position);
                if (Distance(State.User.Location, point) <= 2) Face(point);
                else SetDestination(point, true);
            }
        }
        if (input is not InputEventKey key || !key.Pressed || key.Echo) return;
        switch (key.PhysicalKeycode)
        {
            case Key.I: inventory.Visible = !inventory.Visible; break;
            case Key.C: if (equipment.Visible) equipment.Hide(); else ShowEquipmentPage(0); break;
            case Key.K: ShowEquipmentPage(3); break;
            case Key.Q: if (questList.Visible) CloseQuests(); else OpenQuestJournal(); break;
            case Key.G: Send(new C.PickUp()); break;
            case Key.P: if (group.Visible) group.Hide(); else OpenGroup(); break;
            case Key.T: OpenTrade(); break;
            case Key.H: if (guild.Visible) CloseGuild(); else OpenGuild(); break;
            case Key.Enter: chat.GrabFocus(); break;
            case Key.Space: Attack(State.User.Direction); break;
            case Key.Escape: destination = null; actionTarget = 0; npc.Hide(); shopMode = null; CloseStorage(); CloseQuests(); questBag.Hide(); CloseGroup(); tradeMenu.Hide(); CancelTrade(); if (guild.Visible) CloseGuild(); break;
            default:
                if (key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key6)
                { var slot = (int)(key.PhysicalKeycode - Key.Key1); var item = State.User.Inventory[slot]; if (item != null) UseItem(item.UniqueID); }
                else if (key.PhysicalKeycode >= Key.F1 && key.PhysicalKeycode <= Key.F8)
                {
                    var binding = (byte)(key.PhysicalKeycode - Key.F1 + 1 + (key.ShiftPressed ? 8 : 0));
                    var index = State.User.Magics.FindIndex(m => m.Key == binding); if (index >= 0) Cast(index);
                }
                break;
        }
    }
    public override void _Notification(int what) { if (what == NotificationWMCloseRequest) _ = Quit(); }
    public async Task Quit()
    {
        StopAudio();
        await Connection.Send(new C.Disconnect()); await Task.Delay(150); GetTree().Quit();
    }
    private void StopAudio()
    {
        musicGeneration++;
        foreach (var player in new[] { music, sound }) { player.Stop(); var stream = player.Stream; player.Stream = null; stream?.Dispose(); }
    }
    public override void _ExitTree()
    {
        Connection.Dispose();
        StopAudio();
        Assets.Dispose();
    }
}
