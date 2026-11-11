using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.MirObjects;
using Godot;
using MirFrame = Client.MirObjects.Frame;

namespace Crystal.Native;

public sealed record SpriteImage(Texture2D Texture, Vector2 Offset, Vector2 Size);
public sealed record LibraryFrames(int Count, FrameSet Frames);

public sealed class ResourceCache : IDisposable
{
    private readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim workers = new(8);
    private readonly ConcurrentDictionary<string, Task<byte[]>> requests = new();
    private readonly Dictionary<string, SpriteImage?> textures = new();
    private readonly Dictionary<string, Task<LibraryFrames>> libraries = new();
    private readonly ConcurrentDictionary<string, long> retryAt = new();
    private readonly Dictionary<int, string> sounds = new();
    private readonly string cacheRoot;
    private string root = "", directory = "";
    public string Version { get; private set; } = "";
    public string Host { get; private set; } = "127.0.0.1";
    public int GamePort { get; private set; }
    public int Downloads { get; private set; }
    public long DownloadBytes { get; private set; }
    public readonly ConcurrentQueue<string> Errors = new();
    public bool Ready => Version.Length > 0;

    public ResourceCache(string cacheRoot) => this.cacheRoot = cacheRoot;

    public async Task Initialize(string address)
    {
        Version = "";
        foreach (var image in textures.Values) image?.Texture.Dispose();
        textures.Clear(); libraries.Clear(); sounds.Clear(); requests.Clear(); retryAt.Clear();
        var uri = new Uri(address.TrimEnd('/') + "/");
        if (uri.Scheme is not ("http" or "https")) throw new InvalidDataException("资源地址必须是 http 或 https。");
        Host = uri.Host;
        using var manifest = JsonDocument.Parse(await http.GetByteArrayAsync(new Uri(uri, "assets/manifest")));
        Version = manifest.RootElement.GetProperty("version").GetString()!;
        if (Version.Length != 20 || Version.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid asset version.");
        if (manifest.RootElement.GetProperty("frameFormat").GetInt32() != 1) throw new InvalidDataException("Unsupported resource format.");
        GamePort = manifest.RootElement.GetProperty("gamePort").GetInt32();
        root = new Uri(uri, $"assets/{Version}/").ToString();
        directory = Path.Combine(cacheRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.ToString())))[..12], Version);
        Directory.CreateDirectory(directory);
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
        return requests.GetOrAdd(key, Download);
    }

    private async Task<byte[]> Download(string key)
    {
        var path = CachePath(key);
        var url = root + key;
        await workers.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            const int limit = 64 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Resource exceeds 64 MiB.");
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[32768];
            int read;
            while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > limit) throw new InvalidDataException("Resource exceeds 64 MiB.");
                memory.Write(buffer, 0, read);
            }
            var bytes = memory.ToArray();
            var temporary = path + "." + Guid.NewGuid() + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Downloads++; DownloadBytes += bytes.Length;
            return bytes;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or TaskCanceledException)
        {
            retryAt[key] = error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } ? long.MaxValue : Environment.TickCount64 + 10000;
            Errors.Enqueue($"资源读取失败 {key}: {error.Message}");
            throw;
        }
        finally { workers.Release(); }
    }

    internal async Task<MapReader> LoadMap(string name)
    {
        var key = "map/" + Escape(name);
        await Fetch(key);
        requests.TryRemove(key, out _);
        return new MapReader(CachePath(key));
    }

    public LibraryFrames? Frames(string library)
    {
        if (!Ready) return null;
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
        var metadata = Frames(library);
        if (metadata == null || index >= metadata.Count) return null;
        var key = $"frame/{Escape(library)}?index={index}";
        if (textures.TryGetValue(key, out var image)) return image;
        var task = Fetch(key);
        if (!task.IsCompletedSuccessfully) return null;
        try
        {
            var bytes = task.Result;
            if (bytes.Length < 8) throw new InvalidDataException("Invalid sprite header.");
            var width = BinaryPrimitives.ReadInt16LittleEndian(bytes);
            var height = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2));
            if (width == 0 && height == 0) { textures[key] = null; requests.TryRemove(key, out _); return null; }
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) throw new InvalidDataException("Invalid sprite size.");
            using var gzip = new GZipStream(new MemoryStream(bytes, 8, bytes.Length - 8), CompressionMode.Decompress);
            var pixels = new byte[checked(width * height * 4)];
            gzip.ReadExactly(pixels);
            if (gzip.ReadByte() != -1) throw new InvalidDataException("Invalid sprite pixel length.");
            for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            using var decoded = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
            image = new SpriteImage(ImageTexture.CreateFromImage(decoded),
                new Vector2(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4)), BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(6))), new Vector2(width, height));
            textures[key] = image;
            if (textures.Count > 3000)
            {
                // UI controls can still reference an evicted texture; let reference counting release it.
                var oldest = textures.First(); textures.Remove(oldest.Key);
                requests.TryRemove(oldest.Key, out _);
            }
            requests.TryRemove(key, out _);
            return image;
        }
        catch (Exception error) when (error is IOException or OverflowException)
        {
            textures[key] = null; File.Delete(CachePath(key)); Errors.Enqueue(error.Message); return null;
        }
    }

    public async Task<AudioStream?> Sound(int id)
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
