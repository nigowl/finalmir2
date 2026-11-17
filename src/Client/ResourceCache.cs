using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using Mir2.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.MirObjects;
using Godot;
using MirFrame = Client.MirObjects.Frame;

namespace Crystal.Native;

public sealed record SpriteImage(Texture2D Texture, Vector2 Offset, Vector2 Size)
{
    public Vector2 BodyCentre { get; init; } = Size / 2;
}
public sealed record LibraryFrames(int Count, FrameSet Frames);

public sealed partial class ResourceCache : IDisposable
{
    private readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim workers = new(8);
    private readonly ConcurrentDictionary<string, Task<byte[]>> requests = new();
    private readonly Dictionary<(string Library, int Index), SpriteImage?> textures = new();
    private readonly Dictionary<string, Task<LibraryFrames>> libraries = new();
    private readonly ConcurrentDictionary<string, long> retryAt = new();
    private readonly Dictionary<int, string> sounds = new();
    private readonly Dictionary<int, Task<AudioStream?>> audio = new();
    private readonly Dictionary<string, string> thumbnailKeys = new();
    private readonly string cacheRoot;
    private string root = "", directory = "";
    public string Version { get; private set; } = "";
    public string Host { get; private set; } = "127.0.0.1";
    public int GamePort { get; private set; }
    public bool SupportsCharacterLocation { get; private set; }
    public int FunctionKeys { get; private set; } = 8;
    public int AttackModes { get; private set; } = 6;
    public Mir2.StorageUnlockHint[] StorageUnlocks { get; private set; } = [];
    private int downloads, cachedFiles, pendingDownloads;
    private long downloadBytes;
    public int Downloads => Volatile.Read(ref downloads);
    public int CachedFiles => Volatile.Read(ref cachedFiles);
    public int PendingDownloads => Volatile.Read(ref pendingDownloads);
    public long DownloadBytes => Interlocked.Read(ref downloadBytes);
    public readonly ConcurrentQueue<string> Errors = new();
    public bool Ready => Version.Length > 0;
    internal int TextureCount => textures.Count;
    internal int TextureEvictions { get; private set; }

    public ResourceCache(string cacheRoot) => this.cacheRoot = cacheRoot;

    public async Task Initialize(string address)
    {
        Version = "";
        foreach (var image in textures.Values) image?.Texture.Dispose();
        textures.Clear(); libraries.Clear(); sounds.Clear(); audio.Clear(); requests.Clear(); retryAt.Clear(); thumbnailKeys.Clear();
        ResetTextureOrder();
        var uri = new Uri(address.TrimEnd('/') + "/");
        if (uri.Scheme is not ("http" or "https")) throw new InvalidDataException("资源地址必须是 http 或 https。");
        Host = uri.Host;
        using var manifest = JsonDocument.Parse(await http.GetByteArrayAsync(new Uri(uri, "assets/manifest")));
        Version = manifest.RootElement.GetProperty("version").GetString()!;
        if (Version.Length != 20 || Version.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("资源版本无效。");
        if (manifest.RootElement.GetProperty("frameFormat").GetInt32() != 1) throw new InvalidDataException("不支持此资源格式。");
        if (manifest.RootElement.GetProperty("checksumAlgorithm").GetString() != "sha256") throw new InvalidDataException("不支持此资源校验方式。");
        GamePort = manifest.RootElement.GetProperty("gamePort").GetInt32();
        SupportsCharacterLocation = manifest.RootElement.TryGetProperty("characterLocation", out var capability) && capability.ValueKind == JsonValueKind.True;
        FunctionKeys = manifest.RootElement.TryGetProperty("functionKeys", out var keys) && keys.TryGetInt32(out var count) && count == 12 ? 12 : 8;
        AttackModes = manifest.RootElement.TryGetProperty("attackModes", out var modes) && modes.TryGetInt32(out var modeCount) && modeCount == 8 ? 8 : 6;
        root = new Uri(uri, $"assets/{Version}/").ToString();
        directory = Path.Combine(cacheRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.ToString())))[..12], Version);
        Directory.CreateDirectory(directory);
        StorageUnlocks = await http.GetFromJsonAsync<Mir2.StorageUnlockHint[]>(new Uri(uri, "game/storage-unlocks")) ?? [];
        var list = Encoding.UTF8.GetString(await Fetch("sound-list"));
        foreach (var line in list.Split('\n'))
        {
            var parts = line.Trim().Split(':', '\t');
            if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out var index)) sounds[index] = parts[^1].Trim().Replace('\\', '/');
        }
    }

    private string CachePath(string key) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
    private static string Escape(string name) => string.Join('/', name.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

    public Task<byte[]> Fetch(string key)
    {
        if (retryAt.TryGetValue(key, out var until))
        {
            if (Environment.TickCount64 >= until) { retryAt.TryRemove(key, out _); requests.TryRemove(key, out _); }
            else if (requests.TryGetValue(key, out var failed)) return failed;
        }
        return requests.GetOrAdd(key, key => Task.Run(() => Download(key)));
    }

    private async Task<byte[]> Download(string key)
    {
        Interlocked.Increment(ref pendingDownloads);
        var path = CachePath(key);
        var url = root + key;
        await workers.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(path) && File.Exists(path + ".sha256"))
            {
                var cached = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                var expected = (await File.ReadAllTextAsync(path + ".sha256").ConfigureAwait(false)).Trim();
                if (Convert.ToHexString(SHA256.HashData(cached)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                { Interlocked.Increment(ref cachedFiles); return cached; }
                File.Delete(path); File.Delete(path + ".sha256");
            }
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            const int limit = 64 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("资源超过64兆字节。");
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[32768];
            int read;
            while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > limit) throw new InvalidDataException("资源超过64兆字节。");
                memory.Write(buffer, 0, read);
            }
            var bytes = memory.ToArray();
            var checksum = response.Headers.TryGetValues("X-Content-SHA256", out var headers) ? headers.SingleOrDefault() : null;
            if (checksum == null || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("资源校验失败，请重新连接服务端。");
            var temporary = path + "." + Guid.NewGuid() + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary + ".sha256", checksum).ConfigureAwait(false);
                File.Move(temporary + ".sha256", path + ".sha256", overwrite: true);
                await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(temporary + ".sha256")) File.Delete(temporary + ".sha256"); }
            Interlocked.Increment(ref downloads); Interlocked.Add(ref downloadBytes, bytes.Length);
            return bytes;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or TaskCanceledException)
        {
            retryAt[key] = error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } ? long.MaxValue : Environment.TickCount64 + 10000;
            Errors.Enqueue($"资源读取失败 {key}: {error.Message}");
            throw;
        }
        finally { workers.Release(); Interlocked.Decrement(ref pendingDownloads); }
    }

    internal async Task<MapReader> LoadMap(string name)
    {
        var key = "map/" + Escape(name);
        await Fetch(key);
        requests.TryRemove(key, out _);
        return new MapReader(CachePath(key));
    }

    public async Task<MapThumbnailInfo> ThumbnailInfo(string name)
    {
        var info = await http.GetFromJsonAsync<MapThumbnailInfo>(root + "map-thumbnail-meta/" + Escape(name)) ?? throw new InvalidDataException("缺少缩略图信息。");
        if (info.Width is <= 0 or > 4096 || info.Height is <= 0 or > 4096 || info.Revision.Length != 64 || info.Revision.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("缩略图信息无效。");
        return info;
    }
    public async Task<byte[]> Thumbnail(string name, string revision)
    {
        var key = "map-thumbnail/" + Escape(name) + "?revision=" + Uri.EscapeDataString(revision);
        var bytes = await Fetch(key);
        requests.TryRemove(key, out _);
        if (thumbnailKeys.TryGetValue(name, out var old) && old != key) { File.Delete(CachePath(old)); File.Delete(CachePath(old) + ".sha256"); }
        thumbnailKeys[name] = key;
        return bytes;
    }

    public LibraryFrames? Frames(string library)
    {
        if (!Ready) return null;
        if (libraries.TryGetValue(library, out var loaded) && loaded.IsCompletedSuccessfully) return loaded.Result;
        var key = "frames/" + Escape(library);
        if (libraries.TryGetValue(library, out var failed) && failed.IsFaulted && retryAt.TryGetValue(key, out var until) && Environment.TickCount64 >= until)
            libraries.Remove(library);
        if (!libraries.TryGetValue(library, out var task)) libraries[library] = task = LoadFrames(library);
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    private async Task<LibraryFrames> LoadFrames(string library)
    {
        using var json = JsonDocument.Parse(await Fetch("frames/" + Escape(library)));
        var set = new FrameSet();
        foreach (var property in json.RootElement.GetProperty("frames").EnumerateObject())
        {
            if (!Enum.TryParse<MirAction>(property.Name, out var action)) continue;
            var f = property.Value;
            var count = f.GetProperty("count").GetInt32();
            var effects = f.GetProperty("effectCount").GetInt32();
            set[action] = new MirFrame(f.GetProperty("start").GetInt32(), count,
                f.GetProperty("stride").GetInt32() - count, f.GetProperty("interval").GetInt32(),
                f.GetProperty("effectStart").GetInt32(), effects,
                f.GetProperty("effectStride").GetInt32() - effects, f.GetProperty("effectInterval").GetInt32())
            { Reverse = f.GetProperty("reverse").GetBoolean(), Blend = f.GetProperty("blend").GetBoolean() };
        }
        return new LibraryFrames(json.RootElement.GetProperty("count").GetInt32(), set);
    }

    public SpriteImage? Sprite(string library, int index)
    {
        if (!Ready || index < 0 || string.IsNullOrEmpty(library)) return null;
        var key = (Library: library, Index: index);
        if (textures.TryGetValue(key, out var image)) { TouchTexture(key); return image; }
        var metadata = Frames(library);
        if (metadata == null || index >= metadata.Count) return null;
        var resourceKey = $"frame/{Escape(library)}?index={index}";
        var task = Fetch(resourceKey);
        if (!task.IsCompletedSuccessfully) return null;
        if (decodeTicks >= System.Diagnostics.Stopwatch.Frequency / 250) return null;
        var decodeStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var bytes = task.Result;
            if (bytes.Length < 8) throw new InvalidDataException("素材帧文件头无效。");
            var width = BinaryPrimitives.ReadInt16LittleEndian(bytes);
            var height = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2));
            if (width == 0 && height == 0) { CacheTexture(key, null); requests.TryRemove(resourceKey, out _); return null; }
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) throw new InvalidDataException("素材帧尺寸无效。");
            using var gzip = new GZipStream(new MemoryStream(bytes, 8, bytes.Length - 8), CompressionMode.Decompress);
            var pixels = new byte[checked(width * height * 4)];
            gzip.ReadExactly(pixels);
            if (gzip.ReadByte() != -1) throw new InvalidDataException("素材帧像素长度无效。");
            for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            OriginalShadowPixels.RestoreAlpha(library, pixels, width, height);
            using var decoded = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
            image = new SpriteImage(ImageTexture.CreateFromImage(decoded),
                new Vector2(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4)), BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(6))), new Vector2(width, height));
            if (library.StartsWith("NPC/") || library.StartsWith("Monster/") || library.StartsWith("CArmour/") || library.StartsWith("AArmour/") || library.StartsWith("ARArmour/") ||
                library.StartsWith("Pet/") || library.StartsWith("Flag/") || library.StartsWith("Gate/") || library.StartsWith("Siege/"))
                image = image with { BodyCentre = MeasureBodyCentre(pixels, width, height) };
            CacheTexture(key, image);
            requests.TryRemove(resourceKey, out _);
            return image;
        }
        catch (Exception error) when (error is IOException or OverflowException)
        {
            File.Delete(CachePath(resourceKey)); File.Delete(CachePath(resourceKey) + ".sha256");
            requests[resourceKey] = Task.FromException<byte[]>(error); retryAt[resourceKey] = Environment.TickCount64 + 10000;
            Errors.Enqueue($"素材帧解码失败 {library}/{index}，10 秒后重试：{error.Message}"); return null;
        }
        finally { decodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - decodeStarted; }
    }

    public bool PrepareAnimation(string library, MirFrame frame, MirDirection direction, int offset = 0)
    {
        var ready = true;
        for (var i = 0; i < frame.Count; i++)
        {
            var index = frame.Start + (int)direction * frame.OffSet + offset + i;
            ready &= PrepareSprite(library, index);
        }
        return ready;
    }

    public bool PrepareSprite(string library, int index)
    {
        if (index < 0 || library.Length == 0) return true;
        if (textures.ContainsKey((library, index))) return true;
        var metadata = Frames(library);
        if (metadata == null) return false;
        if (index >= metadata.Count) return true;
        Sprite(library, index);
        return textures.ContainsKey((library, index));
    }

    public Task<AudioStream?> Sound(int id)
    {
        if (audio.TryGetValue(id, out var task) && !task.IsFaulted) return task;
        task = LoadSound(id); audio[id] = task;
        // 播放器仍可持有淘汰资源；由 Godot 引用计数回收，避免切声时截断音效。
        if (audio.Count > 64) audio.Remove(audio.Keys.First());
        return task;
    }

    private async Task<AudioStream?> LoadSound(int id)
    {
        if (!sounds.TryGetValue(id, out var name)) return null;
        var key = "sound/" + Escape(name);
        var bytes = await Fetch(key); requests.TryRemove(key, out _);
        return Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            ? AudioStreamMP3.LoadFromBuffer(bytes) : AudioStreamWav.LoadFromBuffer(bytes);
    }

    public void Dispose()
    {
        http.Dispose();
        foreach (var image in textures.Values) image?.Texture.Dispose();
        textures.Clear();
    }
}
