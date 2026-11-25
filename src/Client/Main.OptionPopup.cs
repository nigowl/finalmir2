using Godot;

public partial class Main
{
    private void StyleOptionPopup(Node node)
    {
        if (node is not OptionButton option || !IsAncestorOf(option)) return;
        var popup = option.GetPopup();
        popup.ThemeTypeVariation = "GameOptionPopup";
        popup.TransparentBg = true;
    }
}
