using Client.MirObjects;
using Mir2.Management;
using System.Buffers.Binary;

namespace Server.ConsoleHost;

public static class ResourcesModule
{
    public static void MapResources(this RouteGroupBuilder management)
    {
        var group = management.MapGroup("/resources");
        group.MapGet("/frame-info/{**name}", (AssetCatalog catalog, string name, int index) =>
        {
            catalog.ReadPath("libraries", name + ".Lib"); var frame = catalog.Decoder.Frame(name, index);
            return new SpriteFrameInfo(BinaryPrimitives.ReadInt16LittleEndian(frame), BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2)),
                BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(4)), BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(6)));
        });
        group.MapGet("/map-thumbnail/{**name}", async (MapThumbnails thumbnails, string name, CancellationToken token) =>
            Results.Bytes((await thumbnails.Resource(name, false, token)).Png, "image/png"));
        group.MapPost("/map-thumbnail/{**name}", async (MapThumbnails thumbnails, string name, CancellationToken token) =>
            new ActionResult((await thumbnails.Resource(name, true, token)).Source + "，缩略图最长边 1024 像素。"));
        group.MapGet("", (AssetCatalog catalog, string? search) => catalog.List(search));
        group.MapGet("/download/{kind}/{**name}", (AssetCatalog catalog, string kind, string name) => Results.File(catalog.ReadPath(kind, name), "application/octet-stream", Path.GetFileName(name)));
        group.MapPut("/stage/{kind}/{**name}", async (AssetCatalog catalog, string kind, string name, HttpRequest request, CancellationToken token) =>
        { await catalog.Stage(kind, name, request.Body, token); return new ActionResult("资源已暂存。发布会重启游戏引擎，并更新微端资源版本。"); });
        group.MapPost("/publish", (AssetCatalog catalog, GameRuntime runtime, CancellationToken token) => catalog.Publish(runtime, token));
        group.MapDelete("/staged/{kind}/{**name}", (AssetCatalog catalog, string kind, string name) =>
        { File.Delete(Mir2.Hosting.ProjectPaths.SafePath(Path.Combine(catalog.Staging, kind), name)); return new ActionResult("已丢弃暂存资源。"); });
        group.MapGet("/inspect-map/{**name}", async (AssetCatalog catalog, GameRuntime runtime, string name, CancellationToken token) =>
        {
            var path = catalog.ReadPath("maps", name); var map = new MapReader(path); long blocked = 0;
            foreach (var cell in map.MapCells) if ((cell.BackImage & 0x20000000) != 0 || (cell.FrontImage & 0x8000) != 0) blocked++;
            MapAssetUsage[] usages = runtime.Ready ? await runtime.Invoke(() =>
            {
                var world = Server.MirEnvir.Envir.Main;
                return world.MapInfoList.Where(m => (m.FileName.Replace('\\', '/') + ".map").Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Select(m => new MapAssetUsage(m.Index, m.FileName, m.Title, m.MiniMap, m.BigMap, m.Music, m.Light.ToLocalizedString(),
                        world.NPCInfoList.Count(n => n.MapIndex == m.Index), m.Respawns.Count)).ToArray();
            }, token) : [];
            return new MapInspection(name, map.Width, map.Height, blocked, DataBootstrap.Hash(path), usages);
        });
        group.MapGet("/inspect-library/{**name}", (AssetCatalog catalog, string name) =>
        { catalog.ReadPath("libraries", name + ".Lib"); return Results.Json(catalog.Decoder.Frames(name)); });
        group.MapGet("/preview/{**name}", (AssetCatalog catalog, string name, int index) =>
        { catalog.ReadPath("libraries", name + ".Lib"); return Results.Bytes(SpritePreview.Png(catalog.Decoder.Frame(name, index)), "image/png"); });
    }
}
