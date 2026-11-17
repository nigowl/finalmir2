using Godot;
using Crystal.Native;

public partial class Main
{
    private VBoxContainer itemNotices = null!;
    private readonly List<(PanelContainer Panel, long Until)> itemNoticeRows = new();

    private void BuildItemNotices()
    {
        itemNotices = new VBoxContainer { Name = "ItemNotices", Size = new Vector2(220, 0), MouseFilter = MouseFilterEnum.Ignore };
        itemNotices.AddThemeConstantOverride("separation", 4); game.AddChild(itemNotices);
    }

    private void ShowItemNotice(string text, ChatType type)
    {
        if (itemNotices == null) return;
        if (itemNoticeRows.Count == 4)
        { itemNoticeRows[0].Panel.QueueFree(); itemNoticeRows.RemoveAt(0); }
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(.06f, .05f, .035f, .68f),
            BorderColor = new Color("786342"), BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4 });
        itemNotices.AddChild(panel);
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 3,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", ClientUi.Small); label.AddThemeColorOverride("font_color", ChatColour(type));
        panel.AddChild(label); itemNoticeRows.Add((panel, Environment.TickCount64 + 3000));
    }

    private void ProcessItemNotices(long now)
    {
        if (itemNotices == null) return;
        for (var index = itemNoticeRows.Count - 1; index >= 0; index--)
        {
            var row = itemNoticeRows[index];
            if (scene != "game" || now >= row.Until) { row.Panel.QueueFree(); itemNoticeRows.RemoveAt(index); }
            else row.Panel.Modulate = new Color(1, 1, 1, Math.Clamp((row.Until - now) / 350f, 0, 1));
        }
        itemNotices.Position = new Vector2(796, MiniMap.GetRect().End.Y + 6);
        itemNotices.Visible = scene == "game" && !MiniMap.Expanded && itemNoticeRows.Count > 0;
    }
}
