using Crystal.Native;
using Godot;

public partial class Main
{
    private int displayScreen = -1;
    private float displayScale;
    private double displayCheck;

    private void ConfigureDisplay()
    {
        var window = GetWindow();
        window.GuiEmbedSubwindows = true;
        window.ContentScaleSize = new Vector2I(1024, 768);
        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        window.DpiChanged += () => { displayScreen = -1; UpdateDisplay(1); };
        UpdateDisplay(1);
    }
    private void UpdateDisplay(double delta)
    {
        if (DisplayServer.GetName() == "headless") return;
        displayCheck += delta;
        if (displayCheck < 1) return;
        displayCheck = 0;
        var window = GetWindow();
        var screen = window.CurrentScreen;
        var scale = DisplayServer.ScreenGetScale((int)DisplayServer.ScreenOfMainWindow);
        if (OS.GetName() == "Windows") scale = Math.Max(scale, DisplayServer.ScreenGetDpi(screen) / 96f);
        if (screen == displayScreen && Math.Abs(scale - displayScale) < .01f) return;
        displayScreen = screen; displayScale = scale;
        if (window.Mode != Window.ModeEnum.Windowed) return;
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        if (usable.Size.X <= 0 || usable.Size.Y <= 0 || !float.IsFinite(scale) || scale <= 0) return;
        var size = DisplaySizing.Fit(usable.Size.X, usable.Size.Y, scale);
        window.Size = new Vector2I(size.Width, size.Height);
        window.Position = usable.Position + (usable.Size - window.Size) / 2;
        GD.Print($"Display fit: screen={screen}, scale={scale:0.##}, usable={usable.Size}, window={window.Size}, canvas=1024x768");
    }
}
