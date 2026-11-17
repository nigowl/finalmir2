using Godot;
using Crystal.Native;

public partial class Main
{
    private static Button QuestTextStyle(Button button, float inset = 4)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new Color("e9c76f"),
                BorderWidthBottom = state is "hover" or "pressed" or "hover_pressed" ? 1 : 0,
                ContentMarginLeft = inset, ContentMarginRight = 4, ContentMarginTop = 0, ContentMarginBottom = 0 });
        button.AddThemeFontSizeOverride("font_size", ClientUi.Body);
        button.AddThemeColorOverride("font_color", new Color("e9c76f"));
        button.AddThemeColorOverride("font_hover_color", new Color("fff1ad"));
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color("867c68"));
        return button;
    }

    private Button QuestRow(ClientQuestInfo quest, string state)
    {
        var title = string.IsNullOrWhiteSpace(quest.Group) ? quest.Name : $"{quest.Group} · {quest.Name}";
        var row = QuestTextStyle(new Button { Text = title, TooltipText = $"{title}\n{state}", Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 19), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, ToggleMode = true,
            ButtonPressed = quest.Index == selectedQuest, FocusMode = FocusModeEnum.None }, 57);
        // NPC列表的底图每行19px：前两格放选中标记和状态，任务名单独占名称列。
        var mark = new Label { Name = "QuestSelectionMark", Text = row.ButtonPressed ? "›" : "",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        var status = new Label { Name = "QuestStatus", Text = state switch { "可接受" => "可接", "可交付" => "交付", "进行中" => "进行", _ => "完成" },
            Position = new Vector2(23, 0), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        status.AddThemeFontSizeOverride("font_size", ClientUi.Small);
        if (state == "可接受") status.AddThemeColorOverride("font_color", new Color("78d888"));
        row.AddChild(mark); row.AddChild(status);
        // 字体最小尺寸入树后才刷新，延后定行高以免默认字号撑高Label导致基线下沉。
        mark.SetDeferred(Control.PropertyName.Size, new Vector2(23, 19));
        status.SetDeferred(Control.PropertyName.Size, new Vector2(32, 19)); return row;
    }
}
