using System.Text.RegularExpressions;
using Godot;
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
    private Button npcBack = null!;
    private uint npcDialogueId;

    private void BuildNpc()
    {
        npc = Panel(game, 25, 45, 440, 225); npc.Name = "NpcDialog"; npc.Hide(); npc.ClipContents = true;
        Picture(npc, "Prguse", 995, 0, 0);
        Text(npc, "NPC 对话 / 商店", 15, 5);
        ArtButton(npc, "Prguse2", 360, 413, 3, "关闭 NPC", () => { npc.Hide(); shopMode = null; });
        npcScroll = new ScrollContainer { Name = "NpcScroll", Position = new Vector2(12, 33), Size = new Vector2(415, 156),
            ClipContents = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        npc.AddChild(npcScroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 3); npcScroll.AddChild(content);
        npcText = new VBoxContainer { Name = "NpcText", SizeFlagsHorizontal = SizeFlags.ExpandFill }; npcText.AddThemeConstantOverride("separation", 1); content.AddChild(npcText);
        npcGoods = new VBoxContainer { Name = "NpcGoods", SizeFlagsHorizontal = SizeFlags.ExpandFill }; npcGoods.AddThemeConstantOverride("separation", 2); content.AddChild(npcGoods);
        repairPanel = new Control { Name = "NpcRepair", CustomMinimumSize = new Vector2(395, 76) }; content.AddChild(repairPanel); repairPanel.Hide();
        Text(repairPanel, "拖入背包物品或双击选择；已装备物品请先卸下。", 0, 0, 395).AddThemeFontSizeOverride("font_size", 10);
        repairCell = new Button { Name = "RepairCell", Position = new Vector2(0, 28), Size = new Vector2(36, 32), ExpandIcon = true, FocusMode = FocusModeEnum.None };
        CellStyle(repairCell); repairPanel.AddChild(repairCell); repairCell.Pressed += () => { repairItem = 0; RefreshRepair(); };
        repairInfo = Text(repairPanel, "", 45, 26, 234); repairInfo.Size = new Vector2(234, 44); repairInfo.AddThemeFontSizeOverride("font_size", 10);
        repairConfirm = Button(repairPanel, "修理", 303, 30, 80, SubmitRepair);
        npcBack = Button(npc, "返回", 331, 194, 91, () => Send(new C.CallNPC { ObjectID = npcDialogueId, Key = "[@MAIN]" })); npcBack.Hide();
        npcBack.Size = new Vector2(91, 24);
    }

    private void RenderNpc(List<string> page)
    {
        Clear(npcText); Clear(npcGoods); shopMode = null; repairPanel.Hide(); npcBack.Hide(); repairItem = pendingRepair = 0;
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
                var button = new LinkButton { Text = link.Groups[1].Value, Underline = LinkButton.UnderlineMode.OnHover, FocusMode = FocusModeEnum.None };
                button.AddThemeFontSizeOverride("font_size", 11); button.AddThemeColorOverride("font_color", new Color("e9c76f"));
                button.AddThemeColorOverride("font_hover_color", new Color("fff1ad")); button.AddThemeColorOverride("font_pressed_color", Colors.White);
                button.Pressed += () => Send(new C.CallNPC { ObjectID = npcDialogueId, Key = key }); row.AddChild(button);
                offset = link.Index + link.Length;
            }
            AddNpcWords(row, line[offset..]);
        }
    }

    private static void AddNpcWords(Control row, string text)
    {
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var label = new Label { Text = word, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 11); row.AddChild(label);
        }
    }

    private void RenderGoods(S.NPCGoods packet)
    {
        Clear(npcText); Clear(npcGoods); repairPanel.Hide(); npc.Show(); npcBack.Show(); npcScroll.ScrollVertical = 0;
        foreach (var item in packet.List)
        {
            State.Bind(item);
            var single = item.Clone(); single.Count = 1;
            var price = (uint)Math.Clamp(single.Price() * (double)packet.Rate, 0, uint.MaxValue);
            var button = new Button { Text = $"{item.Info?.FriendlyName} ×1 · {price} 金币", CustomMinimumSize = new Vector2(0, 26),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None,
                ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = ItemText(item) };
            CellStyle(button, 7);
            button.AddThemeFontSizeOverride("font_size", 11);
            button.Pressed += () => Send(new C.BuyItem { ItemIndex = item.UniqueID, Count = 1, Type = packet.Type }); npcGoods.AddChild(button);
        }
    }

    private void OpenRepair(float rate, bool special)
    {
        shopMode = special ? PanelType.SpecialRepair : PanelType.Repair; repairRate = rate;
        repairItem = pendingRepair = 0; Clear(npcGoods); repairPanel.Show(); npc.Show(); inventory.Show();
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
        repairCell.Icon = null; repairCell.TooltipText = item == null ? "将背包物品拖到此格" : ItemText(item);
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
        if (!npc.Visible || shopMode is not (PanelType.Repair or PanelType.SpecialRepair) || repairRevision == State.InventoryRevision) return;
        repairRevision = State.InventoryRevision; RefreshRepair();
    }
}
