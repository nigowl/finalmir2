using Crystal.Native;
using Godot;

public partial class OriginalArt
{
    public static Texture2D InterfaceTexture(string library, int index, SpriteImage image) => (library, index) switch
    {
        ("Title", 504) => CharacterPanelSkin(image),
        ("Title", 196) or ("Prguse", 180) => DialogSkin(image),
        ("Title", >= 658 and <= 663) => CharacterRowSkin(image),
        _ => image.Texture
    };

    public static Texture2D DialogSkin(SpriteImage image)
    {
        using var original = image.Texture.GetImage();
        using var source = original.GetRegion(original.GetUsedRect());
        using var corner = source.GetRegion(new Rect2I(0, 0, 32, 33));
        // 原 NPC 底图已经画了关闭图标，通用框须换成花角，关闭图标由控件负责。
        corner.FlipX(); source.BlitRect(corner, new Rect2I(0, 0, 32, 33), new Vector2I(source.GetWidth() - 32, 0));
        return ImageTexture.CreateFromImage(source);
    }

    public static Texture2D ButtonSkin(SpriteImage image)
    {
        using var source = image.Texture.GetImage();
        using var skin = Image.CreateEmpty(24, source.GetHeight(), false, Image.Format.Rgba8);
        // 去掉烘焙的“关闭”文字，只保留同一按钮的完整边饰与棕色底。
        skin.BlitRect(source, new Rect2I(0, 0, 12, source.GetHeight()), Vector2I.Zero);
        skin.BlitRect(source, new Rect2I(source.GetWidth() - 12, 0, 12, source.GetHeight()), new Vector2I(12, 0));
        return ImageTexture.CreateFromImage(skin);
    }

    public static Texture2D TabSkin(SpriteImage image, Rect2I area, Texture2D? outline = null)
    {
        using var source = image.Texture.GetImage();
        using var tab = source.GetRegion(area);
        using var skin = Image.CreateEmpty(17, tab.GetHeight(), false, Image.Format.Rgba8);
        // 原角色页签两侧各8像素没有文字，拼成可伸展底图，完整保留圆角、金边和纵向高光。
        skin.BlitRect(tab, new Rect2I(0, 0, 8, tab.GetHeight()), Vector2I.Zero);
        skin.BlitRect(tab, new Rect2I(8, 0, 1, tab.GetHeight()), new Vector2I(8, 0));
        skin.BlitRect(tab, new Rect2I(tab.GetWidth() - 8, 0, 8, tab.GetHeight()), new Vector2I(9, 0));
        if (outline != null)
        {
            using var mask = outline.GetImage();
            for (var y = 0; y < skin.GetHeight(); y++)
            for (var x = 0; x < skin.GetWidth(); x++)
                if (mask.GetPixel(x, y).A == 0) skin.SetPixel(x, y, Colors.Transparent);
        }
        return ImageTexture.CreateFromImage(skin);
    }

    public static Texture2D CharacterPanelSkin(SpriteImage image)
    {
        using var source = image.Texture.GetImage();
        // 用正文区域的完整横截面接续边框，不能只填中间而切断两侧边线并留下旧页签折角。
        for (var y = 68; y < 100; y++)
            source.BlitRect(source, new Rect2I(0, 100, source.GetWidth(), 1), new Vector2I(0, y));
        return ImageTexture.CreateFromImage(source);
    }

    public static Texture2D CharacterRowSkin(SpriteImage image)
    {
        using var source = image.Texture.GetImage();
        // 标签区与空白处共用同一纵向底色，仅复制无字列，不触碰职业图标、边框和值字段。
        foreach (var area in new[] { new Rect2I(74, 5, 34, 19), new Rect2I(74, 26, 34, 20), new Rect2I(143, 26, 34, 20) })
        for (var x = area.Position.X; x < area.End.X; x++)
            source.BlitRect(source, new Rect2I(65, area.Position.Y, 1, area.Size.Y), new Vector2I(x, area.Position.Y));
        return ImageTexture.CreateFromImage(source);
    }

    public static Texture2D FrameSkin(SpriteImage image, bool transparent = false)
    {
        using var original = image.Texture.GetImage();
        // 原图右侧有2px透明留白，先裁掉，避免九宫格把空列复制到右边线和右下角。
        using var source = original.GetRegion(original.GetUsedRect());
        using var skin = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        skin.Fill(transparent ? Colors.Transparent : source.GetPixel(source.GetWidth() / 2, 70));
        // 四角各保留完整 24 像素花纹，直边独立延展，不能只裁最外面几条线。
        foreach (var x in new[] { 0, 40 })
        foreach (var y in new[] { 0, 40 })
        {
            using var corner = source.GetRegion(new Rect2I(x == 0 || y == 0 ? 0 : source.GetWidth() - 24, y == 0 ? 0 : source.GetHeight() - 24, 24, 24));
            if (x == 40 && y == 0) corner.FlipX();
            RemoveCornerMatte(corner, x != 0, y != 0);
            skin.BlendRect(corner, new Rect2I(0, 0, 24, 24), new Vector2I(x, y));
        }
        for (var offset = 24; offset < 40; offset++)
        {
            skin.BlitRect(source, new Rect2I(source.GetWidth() / 2, 0, 1, 4), new Vector2I(offset, 0));
            skin.BlitRect(source, new Rect2I(source.GetWidth() / 2, source.GetHeight() - 4, 1, 4), new Vector2I(offset, 60));
            skin.BlitRect(source, new Rect2I(0, 70, 4, 1), new Vector2I(0, offset));
            skin.BlitRect(source, new Rect2I(source.GetWidth() - 4, 70, 4, 1), new Vector2I(60, offset));
        }
        return ImageTexture.CreateFromImage(skin);
    }

    private static void RemoveCornerMatte(Image corner, bool right, bool bottom)
    {
        // 角饰只保留金色部分，其余由对应地图或面板底色填充，避免暗点或异色方块。
        for (var y = 0; y < 24; y++)
        for (var x = 0; x < 24; x++)
        {
            var colour = corner.GetPixel(x, y);
            var edgeX = right ? 23 - x : x; var edgeY = bottom ? 23 - y : y;
            if (colour.R + colour.G + colour.B < .9f || edgeX >= 4 && edgeY >= 4 && edgeX + edgeY > 30)
                corner.SetPixel(x, y, Colors.Transparent);
        }
    }
}
