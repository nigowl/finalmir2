using System.IO.Compression;
using Client.MirObjects;
using Mir2.Hosting;
using Server.ConsoleHost;

var output = args[0];
var paths = new ProjectPaths();
var available = Directory.Exists(Path.Combine(paths.Assets, "libraries"));
if (!available && args.ElementAtOrDefault(1) == "required")
{
    Console.Error.WriteLine($"error MIR2001: 缺少客户端基础素材，请先导入游戏资源：{paths.Assets}");
    return 1;
}

using var payload = new MemoryStream();
using (var archive = new ZipArchive(payload, ZipArchiveMode.Create, leaveOpen: true))
{
    if (available)
    {
        var assets = new GameAssets(paths.Assets);
        var appearances = from job in Enum.GetValues<MirClass>()
                          from gender in Enum.GetValues<MirGender>()
                          from frame in Enumerable.Range(OriginalPlayerArt.SelectionStart(job, gender), 16)
                          select frame;
        AddFrames("ChrSel", new[] { 0 }.Concat(appearances));
        AddFrames("Title", new[] { 196, 276, 277, 278, 500, 504 }.Concat(Enumerable.Range(658, 5)));
        // HUD、共用窗口及控件的静态图；不随包携带地图、场景人物或物品图集。
        AddFrames("Prguse", new[] { 1, 4, 6, 8, 24, 65, 76, 180, 340, 341, 389, 390, 586, 917, 918, 950, 960, 961, 995,
            1084, 1851, 1932, 1933, 2443 }.Concat(Enumerable.Range(100, 5)).Concat(Enumerable.Range(114, 7))
            .Concat(Enumerable.Range(826, 3)).Concat(Enumerable.Range(1900, 15)).Concat(Enumerable.Range(1960, 3))
            .Concat(Enumerable.Range(1982, 3)).Concat(Enumerable.Range(1991, 6)).Concat(Enumerable.Range(2099, 3)).Append(2164)
            .Concat(Enumerable.Range(1400, 5)).Concat(Enumerable.Range(1410, 5)));
        AddFrames("Prguse2", [197, 198, 199, 207, 208, 209, 360, 361, 362, 431, 432, 433]);

        var soundList = File.ReadLines(GameAssets.Resolve(Path.Combine(paths.Assets, "sounds"), "SoundList.lst", ""));
        foreach (var id in new[] { 10103, 10146, 10147 })
        {
            var name = soundList.Select(line => line.Trim().Split(':', '\t'))
                .First(parts => parts.Length >= 2 && int.TryParse(parts[0], out var index) && index == id)[^1].Trim();
            string file;
            try { file = assets.SoundPath(name); }
            catch (FileNotFoundException)
            { file = assets.SoundPath(Path.ChangeExtension(name, Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".mp3")); }
            Add($"sound/{id}{Path.GetExtension(file).ToLowerInvariant()}", File.ReadAllBytes(file), CompressionLevel.Fastest);
        }

        void AddFrames(string library, IEnumerable<int> indices)
        {
            foreach (var index in indices.Distinct())
            {
                var frame = assets.Frame(library, index);
                if (frame.Length <= 8) throw new InvalidDataException($"客户端基础素材帧为空：{library}/{index}");
                Add($"{library}/{index}", frame, CompressionLevel.NoCompression);
            }
        }
    }

    void Add(string key, byte[] bytes, CompressionLevel compression)
    {
        var entry = archive.CreateEntry(key, compression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
var bytes = payload.ToArray();
if (!File.Exists(output) || !bytes.AsSpan().SequenceEqual(File.ReadAllBytes(output)))
{
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var temporary = output + "." + Guid.NewGuid() + ".tmp";
    try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, output, overwrite: true); }
    finally { File.Delete(temporary); }
}
return 0;
