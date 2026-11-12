using Crystal.Native;
using Godot;

public partial class OriginalArt
{
    public static Texture2D DialogSkin(SpriteImage image)
    {
        using var source = image.Texture.GetImage();
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

    public static Texture2D FrameSkin(SpriteImage image, bool compact = false)
    {
        using var source = image.Texture.GetImage();
        using var skin = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        skin.Fill(compact ? Colors.Transparent : source.GetPixel(source.GetWidth() / 2, 70));
        // 四角各保留完整 24 像素花纹，直边独立延展，不能只裁最外面几条线。
        foreach (var x in new[] { 0, 40 })
        foreach (var y in new[] { 0, 40 })
        {
            if (x == 40 && y == 0)
            {
                using var corner = source.GetRegion(new Rect2I(0, 0, 24, 24)); corner.FlipX();
                skin.BlitRect(corner, new Rect2I(0, 0, 24, 24), new Vector2I(x, y));
            }
            else skin.BlitRect(source, new Rect2I(x == 0 ? 0 : source.GetWidth() - 24, y == 0 ? 0 : source.GetHeight() - 24, 24, 24), new Vector2I(x, y));
        }
        for (var offset = 24; offset < 40; offset++)
        {
            skin.BlitRect(source, new Rect2I(source.GetWidth() / 2, 0, 1, 4), new Vector2I(offset, 0));
            skin.BlitRect(source, new Rect2I(source.GetWidth() / 2, source.GetHeight() - 4, 1, 4), new Vector2I(offset, 60));
            skin.BlitRect(source, new Rect2I(0, 70, 4, 1), new Vector2I(0, offset));
            skin.BlitRect(source, new Rect2I(source.GetWidth() - 4, 70, 4, 1), new Vector2I(60, offset));
        }
        if (compact) skin.Resize(32, 32, Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(skin);
    }
}
