using Godot;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Control publicShop = null!;
    private VBoxContainer publicShopRows = null!;
    private Label publicShopStatus = null!;
    private readonly List<Button> publicShopTabs = new();
    private readonly List<byte> publicShopTabIds = new();
    private HBoxContainer publicShopTabBar = null!;
    private string publicShopTabSignature = "";
    private readonly ClientPublicShop publicPurchases = new();
    private ConfirmationDialog publicShopConfirm = null!;
    private Label publicShopConfirmText = null!;
    private SpinBox publicShopCount = null!;
    private PublicShopEntry? publicShopSelection;
    private int publicShopCategory, publicShopRevision = -1;
    private uint publicShopGold;
    private long publicShopRequestedAt;
    private bool publicShopTimeoutShown;

    private void BuildPublicShop()
    {
        var icon = ArtButton(game, "Prguse", 826, 922, 654, "商城", TogglePublicShop); icon.Name = "PublicShopButton";
        icon.ArtSize = ClientUi.HudActionSize; icon.Size = Vector2.One * icon.ArtSize;
        publicShop = Panel(game, 292, 112, 440, 398); publicShop.Name = "PublicShopDialog"; publicShop.Hide();
        WindowTitle(publicShop, "商城", 370);
        ArtButton(publicShop, "Prguse2", 360, 413, 4, "关闭商城", ClosePublicShop);
        publicShopTabBar = new HBoxContainer { Position = new Vector2(22, 40), Size = new Vector2(396, 24) };
        publicShopTabBar.AddThemeConstantOverride("separation", ClientUi.Gap); publicShop.AddChild(publicShopTabBar);
        SyncPublicShopTabs();
        publicShopStatus = Text(publicShop, "", 22, 74, 396); publicShopStatus.Name = "PublicShopStatus";
        var scroll = new ScrollContainer { Position = new Vector2(22, 98), Size = new Vector2(396, 256), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        publicShop.AddChild(scroll);
        publicShopRows = new VBoxContainer { Name = "PublicShopRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        publicShopRows.AddThemeConstantOverride("separation", 5); scroll.AddChild(publicShopRows);
        Text(publicShop, "随身补给 · 金币结算 · 即时入包", 22, 368, 290);
        Button(publicShop, "刷新", 337, 366, 80, RequestPublicShop); Draggable(publicShop);
        publicShopConfirm = new ConfirmationDialog { Name = "PublicShopConfirmation", Title = "确认购买", MinSize = new Vector2I(340, 160) };
        game.AddChild(publicShopConfirm); publicShopConfirm.GetOkButton().Text = "购买"; publicShopConfirm.GetCancelButton().Text = "取消";
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 8); publicShopConfirm.AddChild(content);
        publicShopConfirmText = new Label { CustomMinimumSize = new Vector2(310, 45) }; content.AddChild(publicShopConfirmText);
        publicShopCount = new SpinBox { MinValue = 1, Step = 1, Prefix = "数量 " }; content.AddChild(publicShopCount);
        publicShopCount.ValueChanged += _ => RefreshPublicShopConfirmation();
        publicShopConfirm.Confirmed += () =>
        {
            if (publicShopSelection is not { } selected) return;
            var packet = publicPurchases.Buy(State, selected, (ushort)publicShopCount.Value, Environment.TickCount64);
            publicShopSelection = null;
            if (packet == null) { Log("无法购买，请检查金币、角色状态及上一次购买回执。", ChatType.Hint); return; }
            publicShopTimeoutShown = false; Send(packet); RenderPublicShop();
        };
        publicShopConfirm.Canceled += () => publicShopSelection = null;
    }

    private void TogglePublicShop()
    {
        if (publicShop.Visible) { ClosePublicShop(); return; }
        if (!Assets.SupportsPublicShop) { Log("商城需要更新服务端。", ChatType.Hint); return; }
        publicShop.Show(); publicShop.MoveToFront(); RequestPublicShop();
    }
    private void ClosePublicShop() { publicShop.Hide(); publicShopConfirm.Hide(); publicShopSelection = null; }
    private void RequestPublicShop()
    {
        if (publicPurchases.Pending) { RenderPublicShop(); return; }
        publicShopRequestedAt = Environment.TickCount64; publicShopStatus.Text = "正在读取商品…";
        publicShopConfirm.Hide(); publicShopSelection = null;
        Send(Assets.SupportsPublicShopLayout ? new C.PublicShopCatalogRequest() : new C.PublicShopRequest());
    }
    private void RefreshPublicShopConfirmation()
    {
        if (publicShopSelection is not { } entry || !State.Items.TryGetValue(entry.ItemIndex, out var info)) return;
        publicShopConfirmText.Text = $"{info.FriendlyName} ×{publicShopCount.Value:0}{(info.Type == ItemType.Amulet ? "包" : "")} · {PublicShopRules.UnitText(info)}\n单价 {entry.Price:N0} 金币 · 合计 {(ulong)entry.Price * (ushort)publicShopCount.Value:N0} 金币";
    }
    private void RenderPublicShop()
    {
        icons.RemoveAll(pair => !GodotObject.IsInstanceValid(pair.button) || publicShopRows.IsAncestorOf(pair.button));
        Clear(publicShopRows);
        SyncPublicShopTabs();
        for (var i = 0; i < publicShopTabs.Count; i++) publicShopTabs[i].SetPressedNoSignal(publicShopTabIds[i] == publicShopCategory);
        publicShopGold = State.User?.Gold ?? 0;
        publicShopStatus.Text = publicPurchases.Pending ? "正在等待购买回执…" : $"金币 {State.User?.Gold:N0}";
        RenderPublicShopGroups();
        if (publicShopRows.GetChildCount() == 0) publicShopRows.AddChild(new Label { Text = "当前分类暂无商品。" });
    }
    private void SyncPublicShopTabs()
    {
        var tabs = State.PublicShopTabs?.Tabs ?? PublicShopRules.DefaultTabNames.Select((name, id) => new PublicShopTab { Id = (byte)id, Name = name }).ToArray();
        var signature = string.Join("|", tabs.Select(tab => $"{tab.Id}:{tab.Name}"));
        if (signature == publicShopTabSignature) return;
        publicShopTabSignature = signature; Clear(publicShopTabBar); publicShopTabs.Clear(); publicShopTabIds.Clear();
        if (!tabs.Any(tab => tab.Id == publicShopCategory)) publicShopCategory = tabs[0].Id;
        var width = (396 - (tabs.Length - 1) * ClientUi.Gap) / tabs.Length;
        foreach (var entry in tabs)
        {
            var category = entry.Id;
            var tab = Button(publicShopTabBar, entry.Name, 0, 0, width, () => { publicShopCategory = category; RenderPublicShop(); });
            tab.TooltipText = entry.Name; tab.ClipText = true; TabStyle(tab, width); publicShopTabs.Add(tab); publicShopTabIds.Add(category);
        }
    }
    private void PublicShopPacket(Packet packet)
    {
        publicPurchases.Observe(packet);
        if (packet is S.PublicShop) { publicShopRequestedAt = 0; if (publicShop.Visible) RenderPublicShop(); }
        if (packet is S.PublicShopResult && publicShop.Visible) RenderPublicShop();
        if (packet is S.MapChanged or S.Death or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin) ClosePublicShop();
    }
    private void ProcessPublicShop()
    {
        if (publicShopRequestedAt > 0 && Environment.TickCount64 - publicShopRequestedAt >= 5000)
        { publicShopRequestedAt = 0; publicShopStatus.Text = "商品读取超时，请点击刷新。"; }
        if (!publicShopTimeoutShown && publicPurchases.TimedOut(Environment.TickCount64))
        { publicShopTimeoutShown = true; Log("商城购买回执超时，请重连后核对物品和金币；不会重复提交。", ChatType.Hint); }
        if (publicShopRevision != State.PublicShopRevision || publicShop.Visible && publicShopGold != State.User?.Gold)
        { publicShopRevision = State.PublicShopRevision; if (publicShop.Visible) RenderPublicShop(); }
    }
}
