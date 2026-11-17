using Godot;
using Crystal.Native;

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
        var roleTab = Assets.Sprite("Title", 500); var rolePanel = Assets.Sprite("Title", 504);
        var window = Assets.Sprite("Prguse", 995);
        var close = Assets.Sprite("Prguse2", 360); var closePressed = Assets.Sprite("Prguse2", 362);
        if (normal == null || hover == null || pressed == null || roleTab == null || rolePanel == null || window == null || close == null || closePressed == null) return;
        var buttonNormal = OriginalArt.ButtonSkin(normal); var buttonHover = OriginalArt.ButtonSkin(hover); var buttonPressed = OriginalArt.ButtonSkin(pressed);
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            Theme.SetFontSize("font_size", type, ClientUi.Body);
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
        Theme.SetTypeVariation("GamePercent", "LineEdit");
        Theme.SetStylebox("normal", "GamePercent", Theme.GetStylebox("normal", "OptionButton"));
        Theme.SetStylebox("focus", "GamePercent", Theme.GetStylebox("hover", "OptionButton"));
        PrepareWindowTheme(window, close.Texture, closePressed.Texture);
        PrepareTabTheme(rolePanel, roleTab);
        originalButtonThemeReady = true;
    }

    private void PrepareTabTheme(SpriteImage panel, SpriteImage selectedTab)
    {
        Theme.SetTypeVariation("GameTab", "Button");
        // 两态都取真正的60px按钮，去掉选中图右侧透明区及未选中图相邻页签的边线。
        var active = OriginalArt.TabSkin(selectedTab, new Rect2I(1, 0, 60, 20));
        var plain = OriginalArt.TabSkin(panel, new Rect2I(9, 70, 60, 20), active);
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            var selected = state is "pressed" or "hover_pressed";
            Theme.SetStylebox(state, "GameTab", new StyleBoxTexture { Texture = selected ? active : plain,
                TextureMarginLeft = 8, TextureMarginRight = 8, TextureMarginTop = 5, TextureMarginBottom = 3,
                ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 1, ContentMarginBottom = 1,
                ModulateColor = state == "hover" ? new Color(1.12f, 1.12f, 1.12f) : state == "disabled" ? new Color(1, 1, 1, .4f) : Colors.White });
        }
        Theme.SetColor("font_color", "GameTab", new Color("b9aa89"));
        Theme.SetColor("font_pressed_color", "GameTab", new Color("ffe3a1"));
        Theme.SetColor("font_hover_pressed_color", "GameTab", new Color("ffe3a1"));
        foreach (var state in new[] { "tab_selected", "tab_unselected", "tab_hovered", "tab_disabled" })
            Theme.SetStylebox(state, "TabContainer", Theme.GetStylebox(state == "tab_selected" ? "pressed" : state == "tab_hovered" ? "hover" : state == "tab_disabled" ? "disabled" : "normal", "GameTab"));
        Theme.SetColor("font_selected_color", "TabContainer", new Color("ffe3a1"));
        Theme.SetColor("font_unselected_color", "TabContainer", new Color("b9aa89"));
        foreach (var tab in FindChildren("*", "Button", true, false).OfType<Button>().Where(button => button.ThemeTypeVariation == "GameTab"))
            tab.SetDeferred(Control.PropertyName.Size, new Vector2(tab.Size.X, ClientUi.TabHeight));
    }

    private void PrepareWindowTheme(Crystal.Native.SpriteImage window, Texture2D close, Texture2D closePressed)
    {
        var frame = OriginalArt.FrameSkin(window);
        originalPanelSkin = WindowStyle(frame, 24);
        Theme.SetStylebox("panel", "GameItemTooltip", new StyleBoxFlat {
            BgColor = new Color("100e0bb8"), BorderColor = new Color("82704c"),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 8, ContentMarginBottom = 8 });
        var textTooltip = (StyleBoxFlat)Theme.GetStylebox("panel", "GameItemTooltip").Duplicate();
        textTooltip.BgColor = new Color("100e0bfa"); Theme.SetStylebox("panel", "TooltipPanel", textTooltip);
        Theme.SetFontSize("font_size", "TooltipLabel", ClientUi.Body);
        Theme.SetColor("font_color", "TooltipLabel", new Color("eadbb6"));
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
        Theme.SetConstant("h_separation", "PopupMenu", 8);
        Theme.SetStylebox("panel", "GameMiniMap", WindowStyle(OriginalArt.FrameSkin(window, true), 24, false));
        var embedded = (StyleBoxTexture)dialog.Duplicate(); embedded.ContentMarginTop = 28;
        embedded.ExpandMarginLeft = embedded.ExpandMarginRight = 8; embedded.ExpandMarginTop = 32; embedded.ExpandMarginBottom = 8;
        Theme.SetStylebox("embedded_border", "Window", embedded); Theme.SetStylebox("embedded_unfocused_border", "Window", embedded);
        Theme.SetColor("title_color", "Window", new Color("eadbb6")); Theme.SetFontSize("title_font_size", "Window", 12);
        Theme.SetIcon("close", "Window", close); Theme.SetIcon("close_pressed", "Window", closePressed);
        // 嵌入窗口已绘制完整边框，内容面板只保留内边距，避免重复套框。
        Theme.SetStylebox("panel", "AcceptDialog", new StyleBoxEmpty
        { ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 8, ContentMarginBottom = 0 });
        foreach (var confirmation in FindChildren("*", "AcceptDialog", true, false).OfType<AcceptDialog>())
        {
            confirmation.TransparentBg = true;
            confirmation.GetOkButton().CustomMinimumSize = new Vector2(80, ClientUi.ButtonHeight);
            if (confirmation is ConfirmationDialog choice) choice.GetCancelButton().CustomMinimumSize = new Vector2(80, ClientUi.ButtonHeight);
        }
    }

    private static StyleBoxTexture WindowStyle(Texture2D texture, int edge, bool centre = true) => new()
    {
        Texture = texture, DrawCenter = centre, TextureMarginLeft = edge, TextureMarginRight = edge,
        TextureMarginTop = edge, TextureMarginBottom = edge, ContentMarginLeft = edge, ContentMarginRight = edge,
        ContentMarginTop = edge, ContentMarginBottom = edge
    };

}
