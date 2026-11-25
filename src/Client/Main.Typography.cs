using Crystal.Native;
using Godot;

public partial class Main
{
    private Label WindowTitle(Control parent, string title, float width)
    {
        var label = Text(parent, title, ClientUi.Padding, (ClientUi.HeaderHeight - 24) / 2, width);
        label.Name = "WindowTitle"; label.Size = new Vector2(width, 24);
        label.AddThemeFontSizeOverride("font_size", ClientUi.Title);
        label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return label;
    }

    private static void TabStyle(Button button, float width)
    {
        button.ThemeTypeVariation = "GameTab"; button.ToggleMode = true; button.ActionMode = BaseButton.ActionModeEnum.Press;
        button.ClipText = true; button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        button.ButtonGroup = button.GetParent().GetChildren().OfType<Button>()
            .FirstOrDefault(sibling => sibling != button && sibling.ThemeTypeVariation == "GameTab")?.ButtonGroup ?? new ButtonGroup();
        button.CustomMinimumSize = new Vector2(width, ClientUi.TabHeight);
        button.SetDeferred(Control.PropertyName.Size, button.CustomMinimumSize);
    }
}
