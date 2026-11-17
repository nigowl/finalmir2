using Godot;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyQuestUiOnly()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal))
            throw new InvalidOperationException("任务界面检查必须使用隔离fixture。");
        await EnterUiFixture(); stage = "quest footer buttons and aligned green available status";
        await Until(() => Client.Theme.GetStylebox("pressed", "GameTab") is StyleBoxTexture &&
            Client.Assets.Sprite("Prguse", 950) != null && Client.Assets.Sprite("Prguse", 960) != null && Client.Assets.Sprite("Prguse", 961) != null);
        var original = State.QuestDefinitions[3];
        // 仅替换本地显示样本，不发送接取/交付或改变服务器任务与资产。
        var sample = new ClientQuestInfo { Index = original.Index, NPCIndex = original.NPCIndex, FinishNPCIndex = original.FinishNPCIndex,
            Name = "铁匠的第二次考验", Group = "比奇省", Description = ["外头还是不太平，能帮我再一个忙吗？"],
            MinLevelNeeded = original.MinLevelNeeded, MaxLevelNeeded = original.MaxLevelNeeded, ClassNeeded = original.ClassNeeded,
            RewardsSelectItem = State.QuestDefinitions[1].RewardsSelectItem.ToList(), RewardGold = 38, RewardExp = 150 };
        State.QuestDefinitions[3] = sample;
        try
        {
            Client.OpenQuestJournal(sample.NPCIndex);
            var rows = (VBoxContainer)Client.FindChild("QuestRows", true, false);
            Button SelectedRow() => rows.GetChildren().OfType<Button>().Single(button => button.Text.Contains(sample.Name));
            SelectedRow().EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var scroll = rows.GetParent<ScrollContainer>();
            await Until(() => scroll.GetParent().GetChildren().OfType<TextureRect>().All(picture => picture.Texture != null));
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (scroll.Position != new Vector2(9, 34) || scroll.Size != new Vector2(280, 95) || rows.GetThemeConstant("separation") != 0)
                throw new Exception("NPC任务列表未对齐底图五行区域。");
            foreach (var row in rows.GetChildren().OfType<Button>())
                if (row.Size.Y != 19 || row.Text.Contains('\n') || row.Position.Y % 19 != 0 || row.GetThemeStylebox("normal").ContentMarginLeft != 57)
                    throw new Exception($"任务名称/状态跨行或没有对齐背景列：{row.GetRect()}。");
            var available = SelectedRow().GetNode<Label>("QuestStatus");
            if (available.Text != "可接" || available.GetThemeColor("font_color") != new Color("78d888") ||
                available.Position != new Vector2(23, 0) || available.Size != new Vector2(32, 19))
                throw new Exception($"可接状态颜色/行列错误：{available.Text}, {available.GetThemeColor("font_color")}, {available.GetRect()}, font={available.GetThemeFontSize("font_size")}, min={available.GetCombinedMinimumSize()}。");
            var choices = Client.FindChildren("QuestRewardChoice", "Button", true, false).OfType<Button>().ToArray();
            if (choices.Length != sample.RewardsSelectItem.Count || choices.Length < 2) throw new Exception("任务奖励选择未显示。");
            var rect = choices[0].GetGlobalRect(); choices[0].EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            choices = Client.FindChildren("QuestRewardChoice", "Button", true, false).OfType<Button>().ToArray();
            if (!choices[0].ButtonPressed || !choices[0].Text.StartsWith("已选：") || choices[0].GetGlobalRect() != rect)
                throw new Exception("奖励选择未高亮或选中后文字区域移动。");
            foreach (var button in scroll.GetParent().FindChildren("*", "Button", true, false).OfType<Button>().Where(button => button is not OriginalButton))
            {
                if (button.Text is "接受任务" or "领取奖励" or "关闭")
                {
                    if (button.GetThemeStylebox("normal") is not StyleBoxTexture || button.GetThemeStylebox("hover") is not StyleBoxTexture ||
                        button.Size.Y != ClientUi.ButtonHeight || button.Position.Y != 435)
                        throw new Exception("任务底栏没有恢复统一图形按钮或偏离原底栏。");
                    continue;
                }
                if (button.GetThemeStylebox("normal") is not StyleBoxFlat normal || normal.BgColor.A != 0 ||
                    button.GetThemeStylebox("hover") is not StyleBoxFlat hover || hover.BorderWidthBottom != 1 || hover.BgColor.A != 0)
                    throw new Exception("任务窗口仍有图形文字按钮或缺少悬停下划线。");
            }
            await MoveMouse(choices[1].GetGlobalRect().GetCenter()); await CombatScreenshot("quest-text-actions");
            sample.Name = new string('测', 100) + "任务";
            Client.OpenQuestJournal(sample.NPCIndex); SelectedRow().EmitSignal(BaseButton.SignalName.Pressed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!SelectedRow().ClipText || SelectedRow().GetGlobalRect().End.X > scroll.GetGlobalRect().End.X || scroll.GetHScrollBar().Visible)
                throw new Exception("长任务名称撑出原背景或出现横向滚动。");
            Client.OpenQuestJournal();
            var active = rows.GetChildren().OfType<Button>().First(); active.EmitSignal(BaseButton.SignalName.Pressed);
            var abandon = Client.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "放弃任务");
            abandon.EmitSignal(BaseButton.SignalName.Pressed);
            var confirm = Client.FindChildren("*", "ConfirmationDialog", true, false).OfType<ConfirmationDialog>().Single(dialog => dialog.Title == "确认放弃任务");
            if (abandon.GetThemeStylebox("normal") is not StyleBoxTexture || confirm.GetOkButton().GetThemeStylebox("normal") is not StyleBoxTexture ||
                confirm.GetCancelButton().GetThemeStylebox("normal") is not StyleBoxTexture)
                throw new Exception("任务/确认底栏没有使用统一按钮。");
            confirm.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
            if (!State.Quests.Any()) throw new Exception("取消操作改变任务。");
            await CombatScreenshot("quest-journal-text");
        }
        finally { State.QuestDefinitions[3] = original; }
        GD.Print("PASS native quest UI: original five 19px rows/columns, aligned green available status, original footer/confirmation button skins, clipped long titles and stable text reward/filter interactions; no quest asset requests.");
        await Client.Quit();
    }
}
