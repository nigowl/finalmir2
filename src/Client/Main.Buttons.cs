using Godot;

public partial class Main
{
    private bool originalButtonThemeReady;
    private readonly List<Panel> skinnedPanels = new();
    private StyleBoxTexture? originalPanelSkin;

    private void PrepareButtonTheme()
    {
        if (originalButtonThemeReady || !Assets.Ready) return;
        var normal = Assets.Sprite("Title", 276);
        var hover = Assets.Sprite("Title", 277);
        var pressed = Assets.Sprite("Title", 278);
        var window = Assets.Sprite("Prguse", 995);
        var close = Assets.Sprite("Prguse2", 360); var closePressed = Assets.Sprite("Prguse2", 362);
        if (normal == null || hover == null || pressed == null || window == null || close == null || closePressed == null) return;
        var buttonNormal = OriginalArt.ButtonSkin(normal); var buttonHover = OriginalArt.ButtonSkin(hover); var buttonPressed = OriginalArt.ButtonSkin(pressed);
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            {
                var texture = state is "pressed" or "hover_pressed" ? buttonPressed : state == "hover" ? buttonHover : buttonNormal;
                Theme.SetStylebox(state, type, new StyleBoxTexture { Texture = texture,
                    TextureMarginLeft = 8, TextureMarginRight = 8, TextureMarginTop = 5, TextureMarginBottom = 5,
                    ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3,
                    ModulateColor = state == "disabled" ? new Color(1, 1, 1, .4f) : Colors.White });
            }
            Theme.SetStylebox("focus", type, new StyleBoxEmpty());
            Theme.SetColor("font_color", type, new Color("eadbb6"));
            Theme.SetColor("font_hover_color", type, new Color("fff1ad"));
            Theme.SetColor("font_pressed_color", type, Colors.White);
        }
        PrepareWindowTheme(window, close.Texture, closePressed.Texture);
        foreach (var state in new[] { "tab_selected", "tab_unselected", "tab_hovered" })
            Theme.SetStylebox(state, "TabContainer", Theme.GetStylebox(state == "tab_selected" ? "pressed" : state == "tab_hovered" ? "hover" : "normal", "Button"));
        originalButtonThemeReady = true;
    }

    private void PrepareWindowTheme(Crystal.Native.SpriteImage window, Texture2D close, Texture2D closePressed)
    {
        var frame = OriginalArt.FrameSkin(window);
        originalPanelSkin = WindowStyle(frame, 24);
        foreach (var panel in skinnedPanels.Where(panel => panel != chatFrame)) panel.AddThemeStyleboxOverride("panel", originalPanelSkin);
        var dialog = WindowStyle(OriginalArt.DialogSkin(window), 24); dialog.TextureMarginTop = 33; dialog.TextureMarginBottom = 34;
        settingsDialog.AddThemeStyleboxOverride("panel", dialog);
        var menuFrame = WindowStyle(frame, 24);
        menuFrame.ContentMarginLeft = menuFrame.ContentMarginRight = 28;
        menuFrame.ContentMarginTop = 32; menuFrame.ContentMarginBottom = 28;
        Theme.SetStylebox("panel", "PopupMenu", menuFrame);
        Theme.SetStylebox("hover", "PopupMenu", Theme.GetStylebox("hover", "Button"));
        Theme.SetColor("font_color", "PopupMenu", new Color("eadbb6"));
        Theme.SetColor("font_hover_color", "PopupMenu", new Color("fff1ad"));
        Theme.SetConstant("v_separation", "PopupMenu", 6);
        Theme.SetStylebox("panel", "GameMiniMap", WindowStyle(OriginalArt.FrameSkin(window, true), 12, false));
        var embedded = (StyleBoxTexture)dialog.Duplicate(); embedded.ContentMarginTop = 28;
        embedded.ExpandMarginLeft = embedded.ExpandMarginRight = 8; embedded.ExpandMarginTop = 32; embedded.ExpandMarginBottom = 8;
        Theme.SetStylebox("embedded_border", "Window", embedded); Theme.SetStylebox("embedded_unfocused_border", "Window", embedded);
        Theme.SetColor("title_color", "Window", new Color("eadbb6")); Theme.SetFontSize("title_font_size", "Window", 12);
        Theme.SetIcon("close", "Window", close); Theme.SetIcon("close_pressed", "Window", closePressed);
        // 嵌入窗口已绘制完整边框，内容面板只保留内边距，避免重复套框。
        Theme.SetStylebox("panel", "AcceptDialog", new StyleBoxEmpty
        { ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 8, ContentMarginBottom = 8 });
        foreach (var confirmation in FindChildren("*", "AcceptDialog", true, false).OfType<AcceptDialog>()) confirmation.TransparentBg = true;
    }

    private static StyleBoxTexture WindowStyle(Texture2D texture, int edge, bool centre = true) => new()
    {
        Texture = texture, DrawCenter = centre, TextureMarginLeft = edge, TextureMarginRight = edge,
        TextureMarginTop = edge, TextureMarginBottom = edge, ContentMarginLeft = edge, ContentMarginRight = edge,
        ContentMarginTop = edge, ContentMarginBottom = edge
    };

}
