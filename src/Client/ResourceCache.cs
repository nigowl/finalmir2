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
    public Vector2 HeadAnchor { get; init; } = new(Size.X / 2, 0);
}
public sealed record LibraryFrames(int Count, FrameSet Frames);

public sealed partial class ResourceCache : IDisposable
{
    private readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim workers = new(8);
    private readonly SemaphoreSlim soundWorkers = new(2);
    private readonly ConcurrentDictionary<string, Task<byte[]>> requests = new();
    private readonly Dictionary<(string Library, int Index), SpriteImage?> textures = new();
    private readonly Dictionary<string, Task<LibraryFrames>> libraries = new();
    private readonly ConcurrentDictionary<string, long> retryAt = new();
    private readonly Dictionary<int, string> sounds = new();
    private readonly Dictionary<int, Task<AudioStream?>> audio = new();
    private readonly HashSet<int> missingSounds = new();
    private int resourceGeneration;
    private readonly Dictionary<string, string> thumbnailKeys = new();
    private readonly string cacheRoot;
    private const string StartupAssetName = "ClientStartupAssets.zip";
    private readonly ZipArchive? startupAssets;
    private string root = "", directory = "";
    public string Version { get; private set; } = "";
    public string Host { get; private set; } = "127.0.0.1";
    public int GamePort { get; private set; }
    public bool SupportsPublicShop { get; private set; }
    public bool SupportsPublicShopLayout { get; private set; }
    public bool SupportsQuestNavigation { get; private set; }
    public bool SupportsCharacterLocation { get; private set; }
    public bool SupportsHealthDisplay { get; private set; }
    public bool SupportsEquipmentGlow { get; private set; }
    public bool SupportsItemPrices { get; private set; }
    public bool SupportsHeroRecovery { get; private set; }
    public bool SupportsHeroDeletion { get; private set; }
    public bool SupportsItemRecycling { get; private set; }
    public ItemCatalogEntry[] ItemCatalog { get; private set; } = [];
    public PlayerActionTiming PlayerTiming { get; private set; } = PlayerActionTiming.Default;
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

    public ResourceCache(string cacheRoot)
    {
        this.cacheRoot = cacheRoot;
        if (typeof(ResourceCache).Assembly.GetManifestResourceStream(StartupAssetName) is { } stream)
            startupAssets = new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private ZipArchiveEntry? StartupSprite(string library, int index) =>
        library is "ChrSel" or "Prguse" or "Prguse2" or "Title" ? startupAssets?.GetEntry($"{library}/{index}") : null;
    internal bool HasStartupSprite(string library, int index) => StartupSprite(library, index) != null;

    private static byte[] ReadStartupEntry(ZipArchiveEntry entry)
    {
        var bytes = new byte[checked((int)entry.Length)];
        using var stream = entry.Open();
        stream.ReadExactly(bytes);
        return bytes;
    }

    public async Task Initialize(string address, CancellationToken cancellationToken = default)
    {
        Version = "";
        resourceGeneration++;
        // 重连只清服务器素材；本地纹理和界面主题无需重建。
        var local = textures.Where(pair => HasStartupSprite(pair.Key.Library, pair.Key.Index)).ToArray();
        textures.Clear(); libraries.Clear(); sounds.Clear(); audio.Clear(); missingSounds.Clear(); requests.Clear(); retryAt.Clear(); thumbnailKeys.Clear();
        Errors.Clear();
        ResetTextureOrder();
        foreach (var (key, image) in local) CacheTexture(key, image);
        var uri = new Uri(address.TrimEnd('/') + "/");
        if (uri.Scheme is not ("http" or "https")) throw new InvalidDataException("资源地址必须是 http 或 https。");
        Host = uri.Host;
        using var manifest = JsonDocument.Parse(await http.GetByteArrayAsync(new Uri(uri, "assets/manifest"), cancellationToken));
        var version = manifest.RootElement.GetProperty("version").GetString()!;
        if (version.Length != 20 || version.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("资源版本无效。");
        if (manifest.RootElement.GetProperty("frameFormat").GetInt32() != 1) throw new InvalidDataException("不支持此资源格式。");
        if (manifest.RootElement.GetProperty("checksumAlgorithm").GetString() != "sha256") throw new InvalidDataException("不支持此资源校验方式。");
        GamePort = manifest.RootElement.GetProperty("gamePort").GetInt32();
        SupportsPublicShop = manifest.RootElement.TryGetProperty("publicShop", out var shop) && shop.ValueKind == JsonValueKind.True;
        SupportsPublicShopLayout = manifest.RootElement.TryGetProperty("publicShopLayout", out var layout) && layout.ValueKind == JsonValueKind.True;
        SupportsQuestNavigation = manifest.RootElement.TryGetProperty("questNavigation", out var navigation) && navigation.ValueKind == JsonValueKind.True;
        SupportsCharacterLocation = manifest.RootElement.TryGetProperty("characterLocation", out var capability) && capability.ValueKind == JsonValueKind.True;
        SupportsHeroRecovery = manifest.RootElement.TryGetProperty("heroRecovery", out var recovery) && recovery.ValueKind == JsonValueKind.True;
        SupportsHeroDeletion = manifest.RootElement.TryGetProperty("heroDeletion", out var deletion) && deletion.ValueKind == JsonValueKind.True;
        SupportsHealthDisplay = manifest.RootElement.TryGetProperty("healthDisplay", out var health) && health.ValueKind == JsonValueKind.True;
        SupportsItemPrices = manifest.RootElement.TryGetProperty("itemPrices", out var itemPrices) && itemPrices.ValueKind == JsonValueKind.True;
        SupportsEquipmentGlow = manifest.RootElement.TryGetProperty("equipmentGlow", out var equipmentGlow) && equipmentGlow.ValueKind == JsonValueKind.True;
        SupportsItemRecycling = manifest.RootElement.TryGetProperty("itemRecycling", out var recycling) && recycling.ValueKind == JsonValueKind.True;
        if (!manifest.RootElement.TryGetProperty("playerMoveMilliseconds", out var move) || !move.TryGetInt32(out var milliseconds))
            throw new InvalidDataException("服务端未提供有效的走跑间隔，请更新服务端。");
        var percent = manifest.RootElement.TryGetProperty("playerActionSpeedPercent", out var speed) ? speed.GetInt32() : PlayerActionTiming.DefaultActionSpeedPercent;
        if (!PlayerActionTiming.Valid(milliseconds, percent)) throw new InvalidDataException("服务端提供的角色速度超出支持范围。");
        PlayerTiming = new(milliseconds, percent);
        ItemCatalog = SupportsItemRecycling ? await http.GetFromJsonAsync<ItemCatalogEntry[]>(new Uri(uri, "game/item-catalog"), cancellationToken) ?? [] : [];
        FunctionKeys = manifest.RootElement.TryGetProperty("functionKeys", out var keys) && keys.TryGetInt32(out var count) && count == 12 ? 12 : 8;
        AttackModes = manifest.RootElement.TryGetProperty("attackModes", out var modes) && modes.TryGetInt32(out var modeCount) && modeCount == 8 ? 8 : 6;
        root = new Uri(uri, $"assets/{version}/").ToString();
        directory = Path.Combine(cacheRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.ToString())))[..12], version);
        Directory.CreateDirectory(directory);
        StorageUnlocks = await http.GetFromJsonAsync<Mir2.StorageUnlockHint[]>(new Uri(uri, "game/storage-unlocks"), cancellationToken) ?? [];
        var list = Encoding.UTF8.GetString(await Fetch("sound-list").WaitAsync(cancellationToken));
        foreach (var line in list.Split('\n'))
        {
            var parts = line.Trim().Split(':', '\t');
            if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out var index)) sounds[index] = parts[^1].Trim().Replace('\\', '/');
        }
        Version = version;
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
        return requests.GetOrAdd(key, key =>
        {
            var generation = resourceGeneration;
            var path = CachePath(key);
            var url = root + key;
            return Task.Run(() => Download(key, path, url, generation));
        });
    }

    private async Task<byte[]> Download(string key, string path, string url, int generation)
    {
        Interlocked.Increment(ref pendingDownloads);
        var pool = key.StartsWith("sound/", StringComparison.Ordinal) ? soundWorkers : workers;
        await pool.WaitAsync().ConfigureAwait(false);
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
            if (generation == resourceGeneration)
            {
                retryAt[key] = error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } ? long.MaxValue : Environment.TickCount64 + 10000;
                if (!(key.StartsWith("sound/", StringComparison.Ordinal) && error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }))
                    Errors.Enqueue($"资源读取失败 {key}: {error}");
            }
            throw;
        }
        finally { pool.Release(); Interlocked.Decrement(ref pendingDownloads); }
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
        if (index < 0 || string.IsNullOrEmpty(library)) return null;
        var key = (Library: library, Index: index);
        if (textures.TryGetValue(key, out var image)) { TouchTexture(key); return image; }
        var local = StartupSprite(library, index);
        string? resourceKey = null;
        byte[]? downloaded = null;
        if (local == null)
        {
            var metadata = Frames(library);
            if (metadata == null || index >= metadata.Count) return null;
            resourceKey = $"frame/{Escape(library)}?index={index}";
            var task = Fetch(resourceKey);
            if (!task.IsCompletedSuccessfully || decodeTicks >= System.Diagnostics.Stopwatch.Frequency / 250) return null;
            downloaded = task.Result;
        }
        var decodeStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var bytes = local != null ? ReadStartupEntry(local) : downloaded!;
            if (bytes.Length < 8) throw new InvalidDataException("素材帧文件头无效。");
            var width = BinaryPrimitives.ReadInt16LittleEndian(bytes);
            var height = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2));
            if (width == 0 && height == 0)
            { CacheTexture(key, null); if (resourceKey != null) requests.TryRemove(resourceKey, out _); return null; }
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
            {
                var centre = MeasureBodyCentre(pixels, width, height);
                image = image with { BodyCentre = centre, HeadAnchor = MeasureHeadAnchor(pixels, width, height, centre) };
            }
            CacheTexture(key, image);
            if (resourceKey != null) requests.TryRemove(resourceKey, out _);
            return image;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or OverflowException)
        {
            if (resourceKey == null)
            {
                CacheTexture(key, null);
                Errors.Enqueue($"内置素材帧解码失败 {library}/{index}：{error.Message}");
                return null;
            }
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
        if (HasStartupSprite(library, index))
        {
            // 批量预载选角动画仍分帧，单张可见的背景与控件优先显示。
            if (decodeTicks >= System.Diagnostics.Stopwatch.Frequency / 250) return false;
        }
        else
        {
            var metadata = Frames(library);
            if (metadata == null) return false;
            if (index >= metadata.Count) return true;
        }
        Sprite(library, index);
        return textures.ContainsKey((library, index));
    }

    public Task<AudioStream?> Sound(int id)
    {
        if (id < 0) return Task.FromResult<AudioStream?>(null);
        var local = startupAssets?.GetEntry($"sound/{id}.wav") ?? startupAssets?.GetEntry($"sound/{id}.mp3");
        if (!Ready && local == null) return Task.FromResult<AudioStream?>(null);
        if (missingSounds.Contains(id)) return OriginalSoundRules.Fallback(id) is >= 0 and var fallback ? Sound(fallback) : Task.FromResult<AudioStream?>(null);
        if (audio.TryGetValue(id, out var task) && !task.IsFaulted) return task;
        task = local == null ? LoadSound(id) : LoadStartupSound(id, local.FullName); audio[id] = task;
        // 播放器仍可持有淘汰资源；由 Godot 引用计数回收，避免切声时截断音效。
        if (audio.Count > 128) audio.Remove(audio.Keys.First());
        return task;
    }

    private static async Task<AudioStream?> LoadStartupSound(int id, string key)
    {
        var bytes = await Task.Run(() =>
        {
            using var archive = new ZipArchive(typeof(ResourceCache).Assembly.GetManifestResourceStream(StartupAssetName)!, ZipArchiveMode.Read);
            return ReadStartupEntry(archive.GetEntry(key)!);
        });
        return DecodeSound(id, key, bytes);
    }

    private async Task<AudioStream?> LoadSound(int id)
    {
        var generation = resourceGeneration;
        var name = sounds.GetValueOrDefault(id) ?? OriginalSoundRules.SoundName(id);
        foreach (var file in Path.HasExtension(name)
            ? new[] { name, Path.ChangeExtension(name, Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".mp3") }
            : new[] { name + ".wav", name + ".mp3" })
        {
            var key = "sound/" + Escape(file);
            byte[] bytes;
            try { bytes = await Fetch(key); }
            catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
            { if (generation != resourceGeneration) return null; continue; }
            if (generation != resourceGeneration) return null;
            requests.TryRemove(key, out _);
            return DecodeSound(id, file, bytes);
        }
        // 缺失结果也缓存，避免每次攻击都重复请求不存在的资源。
        missingSounds.Add(id);
        if (OriginalSoundRules.Fallback(id) is >= 0 and var fallback)
        {
            GD.Print($"声音素材缺失：{id} ({name})，使用同类替代 {fallback}。");
            return await Sound(fallback);
        }
        GD.PrintErr($"缺少声音素材：{id} ({name})。");
        return null;
    }

    private static AudioStream DecodeSound(int id, string file, byte[] bytes)
    {
        AudioStream? stream = Path.GetExtension(file).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            ? AudioStreamMP3.LoadFromBuffer(bytes) : AudioStreamWav.LoadFromBuffer(OriginalSoundRules.WaveBytes(bytes));
        if (stream == null || stream.GetLength() <= 0) throw new InvalidDataException($"声音无法解码：{id} ({file})。");
        return stream;
    }

    internal async Task WarmSounds(IEnumerable<int> ids)
    {
        var generation = resourceGeneration;
        foreach (var id in ids.Where(id => id >= 0).Distinct().ToArray())
            try { if (generation != resourceGeneration) return; await Sound(id); }
            catch (Exception error) { GD.PrintErr($"声音预载 {id}：{error.Message}"); }
    }

    public void Dispose()
    {
        resourceGeneration++;
        http.Dispose();
        startupAssets?.Dispose();
        foreach (var image in textures.Values) image?.Texture.Dispose();
        textures.Clear();
    }
}
