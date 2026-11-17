using System.Text.RegularExpressions;
using Godot;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private ScrollContainer npcScroll = null!;
    private Control repairPanel = null!;
    private Button repairCell = null!, repairConfirm = null!;
    private Label repairInfo = null!;
    private float repairRate;
    private ulong repairItem, pendingRepair;
    private int repairRevision = -1;
    private uint npcDialogueId;

    private void BuildNpc()
    {
        npc = Panel(game, 25, 45, 440, 225); npc.Name = "NpcDialog"; npc.Hide(); npc.ClipContents = true;
        Picture(npc, "Prguse", 995, 0, 0);
        WindowTitle(npc, "NPC对话 / 商店", 370);
        ArtButton(npc, "Prguse2", 360, 413, 3, "关闭 NPC", () => { npc.Hide(); shopMode = null; InvalidateNpcPurchase(); });
        npcScroll = new ScrollContainer { Name = "NpcScroll", Position = new Vector2(12, 33), Size = new Vector2(415, 156),
            ClipContents = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        npc.AddChild(npcScroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 3); npcScroll.AddChild(content);
        npcText = new VBoxContainer { Name = "NpcText", SizeFlagsHorizontal = SizeFlags.ExpandFill }; npcText.AddThemeConstantOverride("separation", 1); content.AddChild(npcText);
        npcGoods = new VBoxContainer { Name = "NpcGoods", SizeFlagsHorizontal = SizeFlags.ExpandFill }; npcGoods.AddThemeConstantOverride("separation", 2); content.AddChild(npcGoods);
        repairPanel = new Control { Name = "NpcRepair", CustomMinimumSize = new Vector2(395, 76) }; content.AddChild(repairPanel); repairPanel.Hide();
        Text(repairPanel, "拖入背包物品或双击选择；已装备物品请先卸下。", 0, 0, 395).AddThemeFontSizeOverride("font_size", 10);
        repairCell = new ItemTipButton { Name = "RepairCell", Position = new Vector2(0, 28), Size = new Vector2(36, 32), ExpandIcon = true, FocusMode = FocusModeEnum.None };
        CellStyle(repairCell); repairPanel.AddChild(repairCell); repairCell.Pressed += () => { repairItem = 0; RefreshRepair(); };
        repairInfo = Text(repairPanel, "", 45, 26, 234); repairInfo.Size = new Vector2(234, 44); repairInfo.AddThemeFontSizeOverride("font_size", 10);
        repairConfirm = Button(repairPanel, "修理", 303, 31, 80, SubmitRepair);
        BuildNpcTransactions();
    }

    private void RenderNpc(List<string> page)
    {
        Clear(npcText); ClearNpcGoods(); shopMode = null; repairPanel.Hide(); InvalidateNpcPurchase(); repairItem = pendingRepair = 0;
        npcScroll.ScrollVertical = 0; npc.Visible = page.Count > 0;
        foreach (var raw in page)
        {
            var line = Regex.Replace(raw, @"\{([^{}]+)/[A-Za-z]+\}", "$1");
            var links = Regex.Matches(line, @"<([^<>]+)/@([^<>]+)>");
            if (links.Count == 0)
            {
                var label = new Label { Text = line, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    CustomMinimumSize = new Vector2(0, line.Length == 0 ? 5 : 17), MouseFilter = MouseFilterEnum.Ignore };
                label.AddThemeFontSizeOverride("font_size", 11); npcText.AddChild(label); continue;
            }
            var row = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("h_separation", 5); row.AddThemeConstantOverride("v_separation", 0); npcText.AddChild(row);
            var offset = 0;
            foreach (Match link in links)
            {
                AddNpcWords(row, line[offset..link.Index]);
                var key = "[@" + link.Groups[2].Value + "]";
                row.AddChild(NpcLink(link.Groups[1].Value, () => Send(new C.CallNPC { ObjectID = npcDialogueId, Key = key })));
                offset = link.Index + link.Length;
            }
            AddNpcWords(row, line[offset..]);
        }
        npcQuestButton = NpcLink("查看 任务", () => OpenQuestJournal(npcDialogueId));
        npcQuestButton.Name = "NpcQuestLink"; npcQuestButton.Visible = State.QuestsAt(npcDialogueId).Any(); npcText.AddChild(npcQuestButton);
    }

    private static LinkButton NpcLink(string text, Action action)
    {
        var button = new LinkButton { Text = text, Underline = LinkButton.UnderlineMode.OnHover, FocusMode = FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", ClientUi.Body); button.AddThemeColorOverride("font_color", new Color("e9c76f"));
        button.AddThemeColorOverride("font_hover_color", new Color("fff1ad")); button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.Pressed += action; return button;
    }

    private static void AddNpcWords(Control row, string text)
    {
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var label = new Label { Text = word, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 11); row.AddChild(label);
        }
    }

    internal void RenderGoods(S.NPCGoods packet)
    {
        Clear(npcText); ClearNpcGoods(); InvalidateNpcPurchase(); repairPanel.Hide(); npc.Show(); npcScroll.ScrollVertical = 0;
        foreach (var item in packet.List) State.Bind(item);
        foreach (var group in packet.List.Where(item => item.Info != null).GroupBy(item => (item.Info.Type, item.Info.FriendlyName)))
        {
            var prices = group.Select(item => ShopPrice(item, packet.Rate)).ToArray();
            var range = prices.Min() == prices.Max() ? $"{prices[0]:N0}" : $"{prices.Min():N0}-{prices.Max():N0}";
            var header = new Button { Name = "GoodsGroup", Text = $"＋ {group.Key.FriendlyName} · {range} 金币", ToggleMode = true,
                CustomMinimumSize = new Vector2(0, 28), SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
            CellStyle(header, 7); npcGoods.AddChild(header);
            header.ExpandIcon = true; header.AddThemeConstantOverride("icon_max_width", 24); icons.Add((header, group.First()));
            var entries = new VBoxContainer { Name = "GoodsEntries", Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill }; npcGoods.AddChild(entries);
            header.Toggled += expanded => { entries.Visible = expanded; header.Text = $"{(expanded ? "－" : "＋")} {group.Key.FriendlyName} · {range} 金币"; };
            foreach (var item in group)
            {
            var button = new ItemTipButton { Name = "GoodsItem", Text = $"{item.Info.Name} · {ShopPrice(item, packet.Rate):N0} 金币", CustomMinimumSize = new Vector2(0, 28),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None,
                ExpandIcon = true, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            CellStyle(button, 16); button.AddThemeConstantOverride("icon_max_width", 24);
            ItemTooltip(button, item);
            button.Pressed += () => ConfirmNpcPurchase(item, packet); entries.AddChild(button); icons.Add((button, item));
            }
        }
    }

    private static uint ShopPrice(UserItem item, float rate)
    { var single = item.Clone(); single.Count = 1; return (uint)Math.Clamp(single.Price() * (double)rate, 0, uint.MaxValue); }

    private void ClearNpcGoods()
    { icons.RemoveAll(pair => npcGoods.IsAncestorOf(pair.button)); Clear(npcGoods); }

    private void OpenRepair(float rate, bool special)
    {
        shopMode = special ? PanelType.SpecialRepair : PanelType.Repair; repairRate = rate;
        repairItem = pendingRepair = 0; ClearNpcGoods(); InvalidateNpcPurchase(); repairPanel.Show(); npc.Show(); inventory.Show();
        RefreshRepair();
    }

    private void SelectRepairItem(ulong id)
    {
        if (shopMode is not (PanelType.Repair or PanelType.SpecialRepair) || pendingRepair != 0) return;
        var item = State.User?.Inventory.FirstOrDefault(item => item?.UniqueID == id);
        if (item?.Info == null || item.MaxDura == 0 || item.Info.Bind.HasFlag(BindMode.DontRepair) ||
            shopMode == PanelType.SpecialRepair && item.Info.Bind.HasFlag(BindMode.NoSRepair))
        { Log("该物品不能进行此类修理。"); return; }
        if (pendingDrop == id || automation.PendingItem == id) return;
        repairItem = id; RefreshRepair();
    }

    private void RefreshRepair()
    {
        var item = State.User?.Inventory.FirstOrDefault(item => item?.UniqueID == repairItem);
        icons.RemoveAll(pair => pair.button == repairCell);
        repairCell.Icon = null; ItemTooltip(repairCell, item, "将背包物品拖到此格");
        repairConfirm.Disabled = item == null || pendingRepair != 0 || item.CurrentDura >= item.MaxDura;
        if (item == null) { repairItem = 0; repairInfo.Text = "← 修理物品放置处"; return; }
        icons.Add((repairCell, item));
        var price = (uint)Math.Clamp(item.RepairPrice() * (double)repairRate * (shopMode == PanelType.SpecialRepair ? 3 : 1), 0, uint.MaxValue);
        repairInfo.Text = $"{item.Info.FriendlyName} · {item.CurrentDura / 1000f:0.###}/{item.MaxDura / 1000f:0.###}\n费用 {price:N0} 金币（点击修理）";
    }

    private void SubmitRepair()
    {
        if (repairItem == 0 || pendingRepair != 0 || pendingDrop == repairItem || State.Me?.Dead != false ||
            State.User?.Inventory.Any(item => item?.UniqueID == repairItem) != true) return;
        pendingRepair = repairItem;
        Send(shopMode == PanelType.SpecialRepair ? new C.SRepairItem { UniqueID = repairItem } : new C.RepairItem { UniqueID = repairItem }); RefreshRepair();
    }

    private void ProcessNpc()
    {
        ProcessNpcTransaction();
        if (!npc.Visible || shopMode is not (PanelType.Repair or PanelType.SpecialRepair) || repairRevision == State.InventoryRevision) return;
        repairRevision = State.InventoryRevision; RefreshRepair();
    }
}
