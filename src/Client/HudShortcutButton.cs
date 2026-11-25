using Godot;

public partial class HudShortcutButton : OriginalButton
{
    private readonly Texture2D?[] skins = new Texture2D?[3];
    private string skinVersion = "";

    public override void _Draw()
    {
        if (skinVersion != Assets.Version)
        {
            foreach (var skin in skins) skin?.Dispose();
            Array.Clear(skins); skinVersion = Assets.Version;
        }
        var state = IsPressed() ? 2 : IsHovered() ? 1 : 0;
        if (skins[state] == null && Assets.Sprite("Prguse", 1906 + state) is { } frame && Assets.Sprite(Library, Index + state) is { } glyph)
        {
            using var background = frame.Texture.GetImage(); using var source = glyph.Texture.GetImage();
            using var skin = Image.CreateEmpty(20, 20, false, Image.Format.Rgba8);
            // 共用右侧20px按钮的无图案侧边/蓝色渐变，只取旧社交图标中心的浅色符号。
            for (var y = 0; y < 20; y++) for (var x = 0; x < 20; x++)
                skin.SetPixel(x, y, background.GetPixel(x < 3 || x >= 17 ? x : 2, y));
            var inset = source.GetWidth() > 20 ? new Rect2I(4, 3, source.GetWidth() - 8, source.GetHeight() - 6) : new Rect2I(4, 4, 12, 12);
            using var symbol = source.GetRegion(inset);
            for (var y = 0; y < symbol.GetHeight(); y++) for (var x = 0; x < symbol.GetWidth(); x++)
            {
                var pixel = symbol.GetPixel(x, y);
                if (pixel.R < .5f || pixel.G < .5f || pixel.B < .35f || pixel.B - pixel.R > .25f) symbol.SetPixel(x, y, Colors.Transparent);
            }
            var size = new Vector2I(16, 14); symbol.Resize(size.X, size.Y, Image.Interpolation.Nearest);
            skin.BlendRect(symbol, new Rect2I(Vector2I.Zero, size), new Vector2I(2, 3));
            skins[state] = ImageTexture.CreateFromImage(skin);
        }
        Size = new Vector2(20, 20);
        if (skins[state] is { } texture) DrawTexture(texture, Vector2.Zero, Disabled ? new Color(1, 1, 1, .4f) : Colors.White);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("a4863c"), false);
    }
}
