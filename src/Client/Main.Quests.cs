using Godot;
using Crystal.Native;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private Control questList = null!, questDetail = null!, questBag = null!, questTabs = null!;
    private TextureRect diaryArt = null!, npcQuestArt = null!;
    private Label diaryTitle = null!, npcQuestTitle = null!;
    private readonly List<Button> questFilterButtons = new();
    private ScrollContainer questScroll = null!, questRewardScroll = null!;
    private VBoxContainer questRows = null!, questRewards = null!;
    private RichTextLabel questDescription = null!, questTracker = null!;
    private Label questHeading = null!, questCount = null!;
    private LinkButton? npcQuestButton;
    private Button questAccept = null!, questFinish = null!;
    private Button questAbandon = null!;
    private ConfirmationDialog abandonConfirmation = null!;
    private readonly List<Button> questBagButtons = new();
    private int selectedQuest, selectedReward = -1, questRevision = -1, questLevel = -1, abandonQuest;
    private uint questNpc;
    private bool completedQuestPage;

    private void BuildQuests()
    {
        ArtButton(game, "Prguse", 1909, 974, 692, "任务 Q", () => { if (questList.Visible) CloseQuests(); else OpenQuestJournal(); });
        questTracker = new RichTextLabel { Name = "QuestTracker", Position = new Vector2(8, 34), Size = new Vector2(225, 230), MouseFilter = MouseFilterEnum.Ignore, ScrollActive = false };
        questTracker.AddThemeColorOverride("default_color", new Color("f4e2b1"));
        questTracker.AddThemeColorOverride("font_shadow_color", Colors.Black);
        questTracker.AddThemeConstantOverride("shadow_offset_x", 1); questTracker.AddThemeConstantOverride("shadow_offset_y", 1); game.AddChild(questTracker);
        game.MoveChild(questTracker, 1);
        questList = Panel(game, 10, 70, 314, 466); questList.Hide();
        diaryArt = Picture(questList, "Prguse", 961, 0, 0); npcQuestArt = Picture(questList, "Prguse", 950, 0, 0);
        diaryTitle = WindowTitle(questList, "任务日志", 180); diaryTitle.Name = "QuestWindowTitle";
        npcQuestTitle = WindowTitle(questList, "NPC任务", 180); npcQuestTitle.Name = "NpcQuestWindowTitle";
        questCount = Text(questList, "", 210, 4, 75);
        ArtButton(questList, "Prguse2", 360, 289, 3, "关闭任务", CloseQuests);
        questTabs = new Control { Size = new Vector2(300, 65), MouseFilter = MouseFilterEnum.Ignore }; questList.AddChild(questTabs);
        questFilterButtons.Add(Button(questTabs, "进行中", 12, 36, 85, () => { questNpc = 0; completedQuestPage = false; selectedQuest = 0; RenderQuests(); }));
        questFilterButtons.Add(Button(questTabs, "已完成", 108, 36, 85, () => { questNpc = 0; completedQuestPage = true; selectedQuest = 0; RenderQuests(); }));
        questFilterButtons.Add(Button(questTabs, "NPC", 204, 36, 85, () => OpenQuestJournal(npcDialogueId)));
        foreach (var filter in questFilterButtons) { TabStyle(filter, 85); QuestTextStyle(filter); }
        questScroll = new ScrollContainer { Position = new Vector2(12, 73), Size = new Vector2(280, 352), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; questList.AddChild(questScroll);
        questRows = new VBoxContainer { Name = "QuestRows", SizeFlagsHorizontal = SizeFlags.ExpandFill }; questRows.AddThemeConstantOverride("separation", 0); questScroll.AddChild(questRows);
        Button(questList, "关闭", 205, 435, 84, CloseQuests);
        questDetail = Panel(game, 334, 70, 314, 466); questDetail.Hide();
        Picture(questDetail, "Prguse", 960, 0, 0); WindowTitle(questDetail, "任务详情", 250);
        ArtButton(questDetail, "Prguse2", 360, 289, 3, "关闭详情", () => questDetail.Hide());
        questHeading = Text(questDetail, "", 12, 32, 280);
        questHeading.ClipText = true; questHeading.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        questDescription = new RichTextLabel { Position = new Vector2(12, 62), Size = new Vector2(279, 231), ScrollActive = true }; questDetail.AddChild(questDescription);
        var rewardScroll = questRewardScroll = new ScrollContainer { Position = new Vector2(12, 307), Size = new Vector2(285, 118) }; questDetail.AddChild(rewardScroll);
        questRewards = new VBoxContainer { CustomMinimumSize = new Vector2(266, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; rewardScroll.AddChild(questRewards);
        questAccept = Button(questDetail, "接受任务", 40, 435, 110, () => AcceptQuest(selectedQuest));
        questFinish = Button(questDetail, "领取奖励", 40, 435, 110, () => FinishQuest(selectedQuest, selectedReward));
        questAbandon = Button(questDetail, "放弃任务", 170, 435, 110, () =>
        { abandonQuest = selectedQuest; abandonConfirmation.DialogText = "放弃此任务并清理其任务物品？"; abandonConfirmation.PopupCentered(); });
        abandonConfirmation = new ConfirmationDialog { Title = "确认放弃任务" }; game.AddChild(abandonConfirmation); abandonConfirmation.GetOkButton().Text = "确定"; abandonConfirmation.GetCancelButton().Text = "取消";
        abandonConfirmation.Confirmed += () => AbandonQuest(abandonQuest);
        questBag = Panel(game, 700, 280, 316, 236); questBag.Hide();
        Picture(questBag, "Title", 196, 0, 0, nativeFrame: true); WindowTitle(questBag, "任务物品", 250);
        ArtButton(questBag, "Prguse2", 360, 289, 3, "关闭任务物品", () => questBag.Hide());
        for (var i = 0; i < 40; i++)
        {
            var button = new ItemTipButton { Position = new Vector2(9 + i % 8 * 37, 37 + i / 8 * 33), Size = new Vector2(36, 32), FocusMode = FocusModeEnum.None, ExpandIcon = true };
            CellStyle(button); questBag.AddChild(button); questBagButtons.Add(button);
        }
        foreach (var dialog in new[] { questList, questDetail, questBag }) Draggable(dialog);
    }
    public void OpenQuestJournal(uint npcId = 0)
    {
        questNpc = npcId; completedQuestPage = false; selectedQuest = 0; selectedReward = -1;
        questList.Position = npcId == 0 ? new Vector2(192, 60) : new Vector2(Math.Min(710, npc.Position.X + npc.Size.X + 47), npc.Position.Y);
        questDetail.Position = new Vector2(532, 60);
        questList.Show(); RenderQuests();
    }
    private void CloseQuests() { questList.Hide(); questDetail.Hide(); abandonConfirmation.Hide(); questNpc = 0; }
    private bool NearQuestNpc(uint id) => State.User is { } user && State.Me?.Dead == false &&
        State.Actors.TryGetValue(id, out var actor) && actor.Kind == ObjectType.Merchant && !actor.Hidden && Distance(user.Location, actor.Location) <= Globals.DataRange;
    public void AcceptQuest(int id)
    {
        if (State.QuestDefinitions.TryGetValue(id, out var quest) && State.CanAcceptQuest(quest) && NearQuestNpc(quest.NPCIndex))
            Send(new C.AcceptQuest { NPCIndex = quest.NPCIndex, QuestIndex = id });
    }
    public void FinishQuest(int id, int selectedItem = -1)
    {
        if (!State.Quests.TryGetValue(id, out var progress) || !progress.Completed || progress.QuestInfo is not { } info || !NearQuestNpc(info.FinishNPCIndex)) return;
        if (info.RewardsSelectItem.Count > 0 && (uint)selectedItem >= info.RewardsSelectItem.Count) { Log("请先选择一项任务奖励。"); return; }
        Send(new C.FinishQuest { QuestIndex = id, SelectedItemIndex = selectedItem });
    }
    public void AbandonQuest(int id) { if (State.Quests.ContainsKey(id)) Send(new C.AbandonQuest { QuestIndex = id }); }
    private static string QuestText(IEnumerable<string> lines) => string.Join("\n", lines.Select(line =>
        System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(line, @"\{([^{}]+)/[A-Za-z]+\}", "$1"), @"<([^<>]+)/[^<>]+>", "$1")));
    private void RenderQuests()
    {
        if (State.User == null) return;
        diaryArt.Visible = diaryTitle.Visible = questNpc == 0; npcQuestArt.Visible = npcQuestTitle.Visible = questNpc != 0;
        questCount.Text = $"{State.Quests.Count}/{Globals.MaxConcurrentQuests}";
        questTabs.Visible = questNpc == 0;
        for (var i = 0; i < questFilterButtons.Count; i++) questFilterButtons[i].SetPressedNoSignal(i == (completedQuestPage ? 1 : 0));
        questScroll.Position = questNpc == 0 ? new Vector2(12, 73) : new Vector2(9, 34);
        questScroll.Size = questNpc == 0 ? new Vector2(280, 352) : new Vector2(280, 95);
        var target = questNpc == 0 ? questDetail : questList;
        foreach (var control in new Control[] { questDescription, questRewardScroll, questAccept, questFinish })
            if (control.GetParent() != target) control.Reparent(target, false);
        questDescription.Position = questNpc == 0 ? new Vector2(12, 62) : new Vector2(10, 135);
        questDescription.Size = questNpc == 0 ? new Vector2(279, 231) : new Vector2(280, 160);
        Clear(questRows);
        var definitions = questNpc != 0 ? State.QuestsAt(questNpc) : completedQuestPage
            ? State.CompletedQuests.Order().Select(id => State.QuestDefinitions.GetValueOrDefault(id)).OfType<ClientQuestInfo>()
            : State.Quests.Values.Select(q => q.QuestInfo).OfType<ClientQuestInfo>().OrderBy(q => q.Group).ThenBy(q => q.Index);
        var quests = definitions.ToList();
        // Completed metadata is sent lazily by the original server; retain unknown IDs without inventing names.
        if (completedQuestPage && questNpc == 0)
            foreach (var id in State.CompletedQuests.Order().Where(id => !State.QuestDefinitions.ContainsKey(id))) questRows.AddChild(new Label { Text = $"任务 #{id} · 已完成" });
        if (quests.All(q => q.Index != selectedQuest)) { selectedQuest = quests.FirstOrDefault()?.Index ?? 0; selectedReward = -1; }
        foreach (var quest in quests)
        {
            var progress = State.Quests.GetValueOrDefault(quest.Index);
            var state = State.CompletedQuests.Contains(quest.Index) ? "已完成" : progress?.Completed == true ? "可交付" : progress != null ? "进行中" : "可接受";
            var row = QuestRow(quest, state);
            row.Pressed += () => { selectedQuest = quest.Index; selectedReward = -1; RenderQuests(); }; questRows.AddChild(row);
        }
        if (quests.Count == 0 && (!completedQuestPage || State.CompletedQuests.Count == 0)) questRows.AddChild(new Label { Text = questNpc != 0 ? "当前没有可接交的任务。" : "暂无任务。" });
        RenderQuestDetail();
    }
    private void RenderQuestDetail()
    {
        Clear(questRewards);
        if (!State.QuestDefinitions.TryGetValue(selectedQuest, out var quest)) { questDetail.Hide(); questDescription.Text = ""; questAccept.Hide(); questFinish.Hide(); return; }
        var progress = State.Quests.GetValueOrDefault(selectedQuest); var completed = State.CompletedQuests.Contains(selectedQuest);
        questDetail.Visible = questList.Visible && questNpc == 0;
        questHeading.Text = questHeading.TooltipText = quest.Name;
        questDescription.Text = QuestText(completed ? quest.CompletionDescription : progress?.Completed == true ? quest.ReturnDescription : progress != null ? quest.TaskDescription : quest.Description) +
            $"\n\n等级 {quest.MinLevelNeeded}–{quest.MaxLevelNeeded} · {quest.Type.ToLocalizedString()}" +
            (progress == null ? "" : "\n\n" + QuestText(progress.TaskList)) +
            (progress?.Completed == true ? "\n\n请返回交付 NPC 领取奖励。" : "");
        questRewards.AddChild(new Label { Text = $"金币 {quest.RewardGold} · 经验 {quest.RewardExp} · 积分 {quest.RewardCredit}" });
        void Reward(QuestItemReward reward, int choice)
        {
            var row = new HBoxContainer { TooltipText = ItemText(new UserItem(reward.Item) { Count = reward.Count }) }; questRewards.AddChild(row);
            var icon = new TextureRect { Texture = Assets.Sprite("Items", reward.Item.Image)?.Texture, CustomMinimumSize = new Vector2(24, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore }; row.AddChild(icon); pictures.Add((icon, "Items", reward.Item.Image));
            var label = $"{reward.Item.FriendlyName} ×{reward.Count}";
            if (choice < 0) row.AddChild(new Label { Text = "固定：" + label, VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = label });
            else
            {
                var button = QuestTextStyle(new Button { Name = "QuestRewardChoice", Text = (choice == selectedReward ? "已选：" : "选择：") + label, FocusMode = FocusModeEnum.None,
                    Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 24), ToggleMode = true, ButtonPressed = choice == selectedReward,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = label });
                button.Pressed += () => { selectedReward = choice; RenderQuestDetail(); }; row.AddChild(button);
            }
        }
        foreach (var reward in quest.RewardsFixedItem) Reward(reward, -1);
        for (var i = 0; i < quest.RewardsSelectItem.Count; i++) Reward(quest.RewardsSelectItem[i], i);
        questAccept.Visible = progress == null && !completed;
        questAccept.Disabled = !State.CanAcceptQuest(quest) || !NearQuestNpc(quest.NPCIndex);
        questFinish.Visible = progress?.Completed == true;
        questFinish.Disabled = !NearQuestNpc(quest.FinishNPCIndex) || (quest.RewardsSelectItem.Count > 0 && selectedReward < 0);
        questAbandon.Visible = progress != null && questNpc == 0;
    }
    private void RenderQuestBag()
    {
        icons.RemoveAll(pair => questBagButtons.Contains(pair.button));
        for (var i = 0; i < questBagButtons.Count; i++)
        {
            var button = questBagButtons[i]; var item = State.User?.QuestInventory?.ElementAtOrDefault(i);
            button.Icon = null; button.Text = item?.Count > 1 ? item.Count.ToString() : "";
            ItemTooltip(button, item, "任务物品栏");
            if (item != null) icons.Add((button, item));
        }
    }
    private void ProcessQuests()
    {
        if (GodotObject.IsInstanceValid(npcQuestButton)) npcQuestButton!.Visible = State.QuestsAt(npcDialogueId).Any();
        if (questNpc != 0 && !NearQuestNpc(questNpc)) CloseQuests();
        if (State.QuestDefinitions.TryGetValue(selectedQuest, out var selected))
        {
            questAccept.Disabled = !State.CanAcceptQuest(selected) || !NearQuestNpc(selected.NPCIndex);
            questFinish.Disabled = !NearQuestNpc(selected.FinishNPCIndex) || (selected.RewardsSelectItem.Count > 0 && selectedReward < 0);
        }
        if (questRevision == State.QuestRevision && questLevel == State.User!.Level) return;
        questRevision = State.QuestRevision; questLevel = State.User!.Level;
        questTracker.Text = string.Join("\n\n", State.Quests.Values.Where(q => q.QuestInfo != null).Take(3).Select(q =>
            q.QuestInfo.Name + (q.Completed ? " · 可交付" : "") + "\n" + QuestText(q.TaskList)));
        if (questList.Visible) RenderQuests();
    }
}
