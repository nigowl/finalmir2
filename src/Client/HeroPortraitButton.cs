using Godot;

namespace Crystal.Native;

public partial class HeroPortraitButton : Button
{
    public Texture2D? Portrait { get; set; }
    public Texture2D? FrameSource { get; set; }
    public int Level { get; set; }
    public HeroSpawnState SpawnState { get; set; }
    public int Health { get; set; }
    public int MaximumHealth { get; set; }
    public int Mana { get; set; }
    public int MaximumMana { get; set; }
    private Texture2D? source;
    private ImageTexture? face;
    private Texture2D? rimSource;
    private ImageTexture? rim;
    private int drawnLevel = -1;
    private string levelText = "";
    internal Texture2D? FaceTexture => face;
    internal bool PortraitReady => source == Portrait && face != null;
    private Vector2 Centre => Size / 2;

    public override void _Ready()
    {
        Flat = true; FocusMode = FocusModeEnum.None; TextureFilter = TextureFilterEnum.Linear;
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
            AddThemeStyleboxOverride(state, new StyleBoxEmpty());
    }

    public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
    public override bool _HasPoint(Vector2 point) => point.DistanceSquaredTo(Centre) <= 40 * 40;

    public override void _Draw()
    {
        if (source != Portrait)
        {
            face?.Dispose(); face = null; source = Portrait;
            if (source != null)
            {
                using var original = source.GetImage();
                using var rounded = RoundPortrait(original);
                face = ImageTexture.CreateFromImage(rounded);
            }
        }
        if (rimSource != FrameSource)
        {
            rim?.Dispose(); rim = null; rimSource = FrameSource;
            if (rimSource != null) { using var original = rimSource.GetImage(); using var border = OriginalRim(original); rim = ImageTexture.CreateFromImage(border); }
        }
        if (Portrait == null) return;
        var centre = Centre; var highlighted = IsHovered() || IsPressed();
        DrawCircle(centre + Vector2.Down, 40, new Color("140e08d9"));
        DrawCircle(centre, 39, new Color("46331a"));
        if (face != null) DrawTextureRect(face, new Rect2(centre - Vector2.One * 30, Vector2.One * 60), false,
            SpawnState == HeroSpawnState.Summoned ? Colors.White : new Color(.64f, .64f, .64f));
        DrawVitalRing(Health, MaximumHealth, true, new Color("b02824"), new Color("df7460"));
        DrawVitalRing(Mana, MaximumMana, false, new Color("306bb8"), new Color("7195b6"));
        if (rim != null) DrawTextureRect(rim, new Rect2(centre - Vector2.One * 40, Vector2.One * 80), false, highlighted ? new Color(1.1f, 1.1f, 1.1f) : Colors.White);
        DrawArc(centre, 30.5f, 0, Mathf.Tau, 96, new Color("806137"), 1, true);
        var badge = new Rect2(centre + new Vector2(-12, 23), new Vector2(24, 13));
        DrawStyleBox(GetThemeStylebox("normal", "Button"), badge);
        if (drawnLevel != Level) { drawnLevel = Level; levelText = Level.ToString(); }
        var font = ThemeDB.FallbackFont;
        DrawString(font, badge.Position + new Vector2((24 - font.GetStringSize(levelText, fontSize: 9).X) / 2, 10), levelText, fontSize: 9, modulate: new Color("f0dba7"));
    }

    private void DrawVitalRing(int value, int maximum, bool left, Color colour, Color highlight)
    {
        const float gap = .10f, radius = 33;
        var start = left ? Mathf.Pi / 2 + gap : -Mathf.Pi / 2 + gap;
        var sweep = Mathf.Pi - 2 * gap;
        DrawArc(Centre, radius, start, start + sweep, 64, colour.Darkened(.83f), 4, true);
        var fraction = maximum > 0 ? Math.Clamp((double)value / maximum, 0, 1) : 0;
        if (fraction <= 0) return;
        // 两侧都从底部蓄满，生命与魔法的缺口随真实剩余比例从顶部扩大。
        var from = left ? start : start + sweep * (1 - (float)fraction);
        var to = left ? start + sweep * (float)fraction : start + sweep;
        DrawArc(Centre, radius, from, to, 64, colour, 3.5f, true);
        DrawArc(Centre, radius - 1, from, to, 64, highlight, 1, true);
    }

    private static Image OriginalRim(Image hud)
    {
        var image = Image.CreateEmpty(80, 80, false, Image.Format.Rgba8);
        for (var y = 0; y < 80; y++) for (var x = 0; x < 80; x++)
        {
            var delta = new Vector2(x - 39.5f, y - 39.5f); var distance = delta.Length();
            if (distance is < 35.5f or > 39.5f) continue;
            // 复用主HUD血魔球的雕金纹理，避免另绘现代扁平金环。
            image.SetPixel(x, y, hud.GetPixel(Mathf.RoundToInt(51 + delta.X * 1.25f), Mathf.RoundToInt(75 + delta.Y * 1.25f)));
        }
        return image;
    }

    internal static Image RoundPortrait(Image original)
    {
        var side = Math.Min(original.GetWidth(), original.GetHeight()) - 4;
        using var crop = original.GetRegion(new Rect2I((original.GetWidth() - side) / 2, (original.GetHeight() - side) / 2, side, side));
        var width = crop.GetWidth(); var background = new bool[width * width]; var pending = new Queue<Vector2I>();
        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= width || background[y * width + x]) return;
            var pixel = crop.GetPixel(x, y);
            if (pixel.A > .05f && Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > .08f) return;
            background[y * width + x] = true; pending.Enqueue(new Vector2I(x, y));
        }
        for (var edge = 0; edge < width; edge++) { Visit(edge, 0); Visit(edge, width - 1); Visit(0, edge); Visit(width - 1, edge); }
        while (pending.TryDequeue(out var point))
        { Visit(point.X - 1, point.Y); Visit(point.X + 1, point.Y); Visit(point.X, point.Y - 1); Visit(point.X, point.Y + 1); }
        // 只替换与外缘相连的旧黑底，保留眼睛和面部内部的暗部。
        for (var y = 0; y < width; y++) for (var x = 0; x < width; x++)
        {
            var backdrop = new Color("352b1f").Lerp(new Color("1b1510"), y / (float)(width - 1));
            var pixel = crop.GetPixel(x, y);
            crop.SetPixel(x, y, background[y * width + x] ? backdrop : backdrop.Lerp(pixel, pixel.A));
        }
        crop.Resize(60, 60, Image.Interpolation.Bilinear);
        for (var y = 0; y < 60; y++) for (var x = 0; x < 60; x++)
        {
            var pixel = crop.GetPixel(x, y); pixel.A = Math.Clamp(30 - new Vector2(x - 29.5f, y - 29.5f).Length(), 0, 1);
            crop.SetPixel(x, y, pixel);
        }
        return (Image)crop.Duplicate();
    }

    public override void _ExitTree() { face?.Dispose(); rim?.Dispose(); face = null; rim = null; source = rimSource = null; }
}
