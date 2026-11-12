using Client.MirObjects;
using Mir2.Hosting;
using System.Security.Cryptography;
using System.Text;

namespace Server.ConsoleHost;

public sealed record MapThumbnail(int Width, int Height, string Walkable, byte[] Png, string Source, string Revision = "");

// Cache static terrain and collision data; population is read live on the game thread.
public sealed class MapThumbnails(AssetCatalog assets, ProjectPaths paths)
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, MapThumbnail> cache = new();
    private readonly Dictionary<string, MapReader> maps = new();
    private MapReader ReadMap(string path)
    {
        var key = assets.Version + ":" + path;
        if (maps.TryGetValue(key, out var map)) return map;
        if (maps.Count >= 2) maps.Clear();
        return maps[key] = new MapReader(path);
    }
    public Task<MapThumbnail> Get(string file, int miniMap, string version, CancellationToken token) => Resource(file.Replace('\\', '/') + ".map", false, token, version);
    public async Task<MapThumbnail> Resource(string name, bool regenerate, CancellationToken token, string? version = null)
    {
        await gate.WaitAsync(token);
        try
        {
            using var lease = assets.TryRead() ?? throw new InvalidOperationException("资源正在发布，请在发布完成后重试。");
            if (version != null && version != assets.Version) throw new InvalidOperationException("资源已变化，请刷新地图。");
            var path = assets.ReadPath("maps", name);
            var key = assets.Version + ":" + path;
            if (!regenerate && cache.TryGetValue(key, out var found)) return found;
            var root = Path.Combine(paths.Runtime, "cache", "map-thumbnails");
            var folder = Path.Combine(root, assets.Version);
            Directory.CreateDirectory(folder);
            // 仅保留当前素材版本的可重建缓存，不随发布次数无限累积。
            foreach (var old in Directory.EnumerateDirectories(root).Where(p => p != folder)) Directory.Delete(old, true);
            var file = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetRelativePath(Path.Combine(paths.Assets, "maps"), path)))) + ".png");
            var map = ReadMap(path);
            MapThumbnail? result = null;
            if (!regenerate && File.Exists(file) && new FileInfo(file).Length is > 32 and <= 5 * 1024 * 1024 && File.Exists(file + ".sha256"))
            {
                var bytes = await File.ReadAllBytesAsync(file, token);
                var checksum = Hash(bytes);
                if (checksum == (await File.ReadAllTextAsync(file + ".sha256", token)).Trim())
                    result = new(map.Width, map.Height, Walkable(map), bytes, "统一地图缩略图缓存", checksum);
            }
            if (result == null)
            {
                result = await Task.Run(() => Render(map, token), token);
                result = result with { Revision = Hash(result.Png) };
                var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, result.Png, token);
                    await File.WriteAllTextAsync(temporary + ".sha256", result.Revision, token);
                    File.Move(temporary, file, true); File.Move(temporary + ".sha256", file + ".sha256", true);
                }
                finally { File.Delete(temporary); File.Delete(temporary + ".sha256"); }
            }
            if (cache.Count >= 4) cache.Clear(); cache[key] = result; return result;
        }
        finally { gate.Release(); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Walkable(MapReader map)
    {
        var mask = new byte[(checked(map.Width * map.Height) + 7) / 8];
        for (var y = 0; y < map.Height; y++) for (var x = 0; x < map.Width; x++)
        {
            var cell = map.MapCells[x, y]; var index = y * map.Width + x;
            if ((cell.BackImage & 0x20000000) == 0 && (cell.FrontImage & 0x8000) == 0) mask[index / 8] |= (byte)(1 << (index % 8));
        }
        return Convert.ToBase64String(mask);
    }
    public async Task<byte[]> Native(string name, string version, int tileX, int tileY, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            using var lease = assets.TryRead() ?? throw new InvalidOperationException("资源正在发布，请在发布完成后重试。");
            if (version != assets.Version) throw new InvalidOperationException("资源已变化，请刷新地图。");
            var map = ReadMap(assets.ReadPath("maps", name));
            if (tileX < 0 || tileY < 0 || (long)tileX * 1024 >= (long)map.Width * 48 || (long)tileY * 1024 >= (long)map.Height * 32)
                throw new InvalidDataException("原尺寸地图块超出地图范围。");
            var key = $"native:{assets.Version}:{name}:{tileX}:{tileY}";
            if (cache.TryGetValue(key, out var found)) return found.Png;
            var result = await Task.Run(() => Render(map, token, nativeX: tileX * 1024, nativeY: tileY * 1024), token);
            if (cache.Count >= 4) cache.Clear(); cache[key] = result; return result.Png;
        }
        finally { gate.Release(); }
    }
    private MapThumbnail Render(MapReader map, CancellationToken token, int nativeX = -1, int nativeY = -1)
    {
        var native = nativeX >= 0;
        // 48×32 是游戏格子的像素尺寸，缩略图需在同一像素比例下等比缩小。
        var scale = 1024d / Math.Max(map.Width * 48d, map.Height * 32d);
        var scaleX = native ? 48d : 48d * scale; var scaleY = native ? 32d : 32d * scale;
        var width = native ? 1024 : Math.Max(1, (int)Math.Round(map.Width * scaleX)); var height = native ? 1024 : Math.Max(1, (int)Math.Round(map.Height * scaleY));
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 30; pixels[i + 1] = 38; pixels[i + 2] = 34; pixels[i + 3] = 255; }
        var sprites = new Dictionary<(int Library, int Frame), (int W, int H, byte[] Pixels, bool Floor)>(); var libraries = new HashSet<string>(); var missing = false;
        (int W, int H, byte[] Pixels, bool Floor) Sprite(int library, int frame)
        {
            if (frame < 0) return default;
            if (sprites.TryGetValue((library, frame), out var cached)) return cached;
            (int W, int H, byte[] Pixels, bool Floor) result = default;
            if (sprites.Count >= 8192) { missing = true; return default; }
            try
            {
                var name = MapTileLibraries.Resolve(library);
                if (name == "") throw new FileNotFoundException();
                if (!libraries.Contains(name)) { assets.ReadPath("libraries", name + ".Lib"); libraries.Add(name); }
                // Bound generation work for pathological maps; collision data remains exact.
                var original = SpritePreview.Decode(assets.Decoder.Frame(name, frame));
                var w = Math.Max(1, (int)Math.Ceiling(original.Width / 48d * scaleX)); var h = Math.Max(1, (int)Math.Ceiling(original.Height / 32d * scaleY));
                var small = new byte[w * h * 4];
                for (var yy = 0; yy < h; yy++) for (var xx = 0; xx < w; xx++)
                {
                    var source = (Math.Min(original.Height - 1, (int)((yy + .5) * original.Height / h)) * original.Width + Math.Min(original.Width - 1, (int)((xx + .5) * original.Width / w))) * 4;
                    Array.Copy(original.Pixels, source, small, (yy * w + xx) * 4, 4);
                }
                result = (w, h, small, (original.Width == 48 && original.Height == 32) || (original.Width == 96 && original.Height == 64));
            }
            catch (Exception error) when (error is FileNotFoundException or InvalidDataException or EndOfStreamException) { missing = true; }
            if (sprites.Count < 8192) sprites[(library, frame)] = result;
            return result;
        }
        void Draw((int W, int H, byte[] Pixels, bool Floor) sprite, int x, int y, bool raised)
        {
            if (sprite.Pixels == null) return;
            var left = (int)(x * scaleX) - (native ? nativeX : 0); var top = (int)((y + (raised ? 1 : 0)) * scaleY) - (raised ? sprite.H : 0) - (native ? nativeY : 0);
            for (var yy = Math.Max(0, -top); yy < Math.Min(sprite.H, height - top); yy++)
            for (var xx = Math.Max(0, -left); xx < Math.Min(sprite.W, width - left); xx++)
            {
                var from = (yy * sprite.W + xx) * 4; var to = ((top + yy) * width + left + xx) * 4; var alpha = sprite.Pixels[from + 3];
                for (var channel = 0; channel < 3; channel++) pixels[to + channel] = (byte)((sprite.Pixels[from + channel] * alpha + pixels[to + channel] * (255 - alpha)) / 255);
            }
        }
        for (var layer = 0; layer < 2; layer++)
        // 原素材尺寸上限 4096，包含跨块伸出的高物件，避免地图块接缝缺图。
        for (var y = native ? Math.Max(0, nativeY / 32 - 1) : 0; y < (native ? Math.Min(map.Height, (nativeY + height) / 32 + 4096 / 32 + 1) : map.Height); y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = native ? Math.Max(0, nativeX / 48 - 4096 / 48 - 1) : 0; x < (native ? Math.Min(map.Width, (nativeX + width) / 48 + 1) : map.Width); x++)
            {
                var c = map.MapCells[x, y];
                if (layer == 0 && x % 2 == 0 && y % 2 == 0) Draw(Sprite(c.BackIndex, (c.BackImage & 0x1FFFFFFF) - 1), x, y, false);
                var middle = Sprite(c.MiddleIndex, c.MiddleImage - 1); var front = Sprite(c.FrontIndex, (c.FrontImage & 0x7FFF) - 1);
                if (middle.Pixels != null && (layer == 0) == middle.Floor) Draw(middle, x, y, !middle.Floor);
                if (front.Pixels != null && (layer == 0) == (front.Floor && c.FrontAnimationFrame == 0)) Draw(front, x, y, !(front.Floor && c.FrontAnimationFrame == 0));
            }
        }
        return new(map.Width, map.Height, native ? "" : Walkable(map), SpritePreview.Png(width, height, pixels), "原地图素材生成" + (missing ? "（部分素材缺失）" : ""));
    }
}
