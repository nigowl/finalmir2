using Godot;

namespace Crystal.Native;

public sealed partial class ResourceCache
{
    internal SpriteImage? CachedSprite(string library, int index)
    {
        if (!textures.TryGetValue((library, index), out var image) || image == null) return null;
        TouchTexture((library, index)); return image;
    }

    internal static Vector2 MeasureBodyCentre(byte[] pixels, int width, int height)
    {
        long xTotal = 0, yTotal = 0, count = 0;
        // 原素材带有纯黑/半透明阴影；按实体像素重心定位，避免长兵器和大块透明留白拉偏名称。
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var offset = pixel * 4;
            if (pixels[offset + 3] < 200 || Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2])) <= 16) continue;
            xTotal += pixel % width; yTotal += pixel / width; count++;
        }
        return count == 0 ? new Vector2(width, height) / 2 : new Vector2(xTotal / (float)count, yTotal / (float)count);
    }

    private readonly LinkedList<(string Library, int Index)> textureOrder = new();
    internal static Vector2 MeasureHeadAnchor(byte[] pixels, int width, int height, Vector2 centre)
    {
        var radius = Math.Clamp(width / 8, 3, 10);
        var from = Math.Max(0, (int)centre.X - radius); var to = Math.Min(width - 1, (int)centre.X + radius);
        // 只取躯干中线附近的连续实体像素，避免护卫长矛、翅膀和阴影把血条抬到武器顶端。
        for (var y = 0; y < height; y++)
        {
            var count = 0;
            for (var x = from; x <= to; x++)
            {
                var offset = (y * width + x) * 4;
                if (pixels[offset + 3] >= 200 && Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2])) > 16) count++;
            }
            if (count >= Math.Min(4, to - from + 1)) return new Vector2(centre.X, y);
        }
        return new Vector2(centre.X, 0);
    }

    private readonly Dictionary<(string Library, int Index), LinkedListNode<(string Library, int Index)>> textureNodes = new();
    private long textureBytes, decodeTicks;
    internal long TextureBytes => textureBytes;
    internal void BeginFrame() => decodeTicks = 0;

    private void TouchTexture((string Library, int Index) key)
    {
        var node = textureNodes[key]; textureOrder.Remove(node); textureOrder.AddLast(node);
    }

    private void CacheTexture((string Library, int Index) key, SpriteImage? image)
    {
        textures.Add(key, image); textureNodes.Add(key, textureOrder.AddLast(key));
        textureBytes += image == null ? 0 : (long)image.Size.X * (long)image.Size.Y * 4;
        // 保留近期使用的当前画面，按张数和显存双重限额淘汰；FIFO 会反复踢掉常用地砖。
        while (textures.Count > 8192 || textureBytes > 256L * 1024 * 1024)
        {
            var oldest = textureOrder.First!; textureOrder.RemoveFirst(); textureNodes.Remove(oldest.Value);
            var removed = textures[oldest.Value]; textures.Remove(oldest.Value);
            textureBytes -= removed == null ? 0 : (long)removed.Size.X * (long)removed.Size.Y * 4;
            TextureEvictions++;
            // 控件可能仍引用纹理，由 Godot 引用计数回收，不能直接 Dispose。
        }
    }

    private void ResetTextureOrder()
    {
        textureOrder.Clear(); textureNodes.Clear(); textureBytes = decodeTicks = 0; TextureEvictions = 0;
    }
}
