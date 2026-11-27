using System.Buffers.Binary;
using System.IO.Compression;
using Mir2.Hosting;
using Server.ConsoleHost;

internal static class MapRenderingChecks
{
    public static async Task Run(Action<bool, string> check, AssetCatalog assets, ProjectPaths paths)
    {
        var library = Path.Combine(paths.Assets, "libraries", "Map", "WemadeMir2", "Tiles.Lib");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        using (var compressed = new MemoryStream())
        {
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true))
                for (var i = 0; i < 96 * 64; i++) gzip.Write([10, 20, 200, 255]);
            using var file = new BinaryWriter(File.Create(library));
            file.Write(2); file.Write(1); file.Write(12);
            file.Write((short)96); file.Write((short)64); file.Write((short)0); file.Write((short)0);
            file.Write(new byte[5]); file.Write((int)compressed.Length); file.Write(compressed.ToArray());
        }
        foreach (var (name, width, height) in new[] { ("wide", 4, 2), ("tall", 2, 4), ("square", 2, 2) })
        {
            using var file = new BinaryWriter(File.Create(Path.Combine(paths.Assets, "maps", name + ".map")));
            file.Write(new byte[] { 1, 0, 0x43, 0x23 }); file.Write((short)width); file.Write((short)height);
            for (var i = 0; i < width * height; i++)
            { file.Write((short)0); file.Write(1); file.Write(new byte[20]); }
        }
        assets.Refresh(); var renderer = new MapThumbnails(assets, paths); var token = CancellationToken.None;
        var wide = await renderer.Resource("wide.map", false, token);
        var tall = await renderer.Resource("tall.map", false, token);
        check(Size(wide.Png) == (1024, 341) && Size(tall.Png) == (768, 1024), "Resource thumbnails lost their aspect ratio or 1024px longest edge");
        check(Size((await renderer.Resource("square.map", false, token)).Png) == (1024, 683), "Square cell grid was squeezed into a square thumbnail instead of native 48:32 pixels");
        var small = await renderer.Get("wide", 0, assets.Version, token);
        check(small.Revision == wide.Revision && Size(small.Png) == (1024, 341) && small.Width == 4 && small.Height == 2, "Native map aspect or logical grid metadata changed in the map editor");
        check(ReferenceEquals(wide, await renderer.Resource("wide.map", false, token)), "Thumbnail read did not reuse its cached image");
        var regenerated = await renderer.Resource("wide.map", true, token);
        check(!ReferenceEquals(wide, regenerated) && regenerated.Png.SequenceEqual(wide.Png), "Regenerate did not rerender the original terrain deterministically");
        var cached = await new MapThumbnails(assets, paths).Get("wide", 99, assets.Version, token);
        check(cached.Png.SequenceEqual(regenerated.Png) && cached.Revision == regenerated.Revision, "Restart or original mmap index bypassed the canonical thumbnail");
        var cachedFile = Directory.GetFiles(Path.Combine(paths.Runtime, "cache", "map-thumbnails"), "*.png", SearchOption.AllDirectories).Single(p => File.ReadAllBytes(p).SequenceEqual(wide.Png));
        await File.WriteAllBytesAsync(cachedFile, new byte[40]);
        check((await new MapThumbnails(assets, paths).Resource("wide.map", false, token)).Png.SequenceEqual(wide.Png), "Corrupt persistent thumbnail was not rebuilt");
        var tile = await renderer.Native("wide.map", assets.Version, 0, 0, token);
        check(Size(tile) == (1024, 1024), "Native viewport tile is not 1024px");
        using var idat = new MemoryStream();
        for (var offset = 8; offset < tile.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(tile.AsSpan(offset));
            if (System.Text.Encoding.ASCII.GetString(tile, offset + 4, 4) == "IDAT") idat.Write(tile, offset + 8, length);
            offset += length + 12;
        }
        idat.Position = 0; using var zlib = new ZLibStream(idat, CompressionMode.Decompress); var row = new byte[1024 * 4 + 1]; zlib.ReadExactly(row);
        check(row.AsSpan(1, 4).SequenceEqual(new byte[] { 200, 20, 10, 255 }) && row.AsSpan(1 + 192 * 4, 4).SequenceEqual(new byte[] { 30, 38, 34, 255 }), "Native terrain pixels were scaled or drawn outside the native map bounds");
        foreach (var level in new[] { 1, 2, 6 })
        {
            var scaled = await renderer.Native("wide.map", assets.Version, 0, 0, token, level);
            check(Size(scaled) == (1024, 1024) && !scaled.SequenceEqual(tile), "显示比例未重新渲染原地图块或复用了其他比例缓存");
        }
        foreach (var level in new[] { -1, 7 })
        {
            try { await renderer.Native("wide.map", assets.Version, 0, 0, token, level); throw new Exception("接受非法地图比例"); }
            catch (InvalidDataException) { check(true, "地图比例边界"); }
        }
        foreach (var (x, version) in new[] { (-1, assets.Version), (1, assets.Version), (0, "stale") })
        {
            try { await renderer.Native("wide.map", version, x, 0, token); throw new Exception("Invalid native map request was accepted"); }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { check(true, "Native request boundary"); }
        }
    }
    private static (int Width, int Height) Size(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
}
