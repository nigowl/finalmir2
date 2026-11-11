using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Client.MirObjects;
using Mir2.Hosting;
using Mir2.Management;

namespace Server.ConsoleHost;

public sealed class AssetCatalog
{
    private readonly ProjectPaths paths;
    private readonly object gate = new();
    private int readers;
    private bool publishing;
    private TaskCompletionSource? drained;
    private AssetManifest manifest = new("", []);
    public string Version => manifest.Version;
    public AssetEntry[] Files => manifest.Files;
    public string Staging => Path.Combine(paths.Runtime, "staging", "resources");
    public GameAssets Decoder { get; }
    public AssetCatalog(ProjectPaths paths)
    {
        this.paths = paths; Decoder = new GameAssets(paths.Assets); Refresh();
    }
    public void Refresh(ISet<string>? changed = null)
    {
        var old = manifest.Files.ToDictionary(f => f.Kind + "/" + f.Path, StringComparer.OrdinalIgnoreCase);
        var manifestFile = Path.Combine(paths.Assets, "manifest.json");
        var files = new List<AssetEntry>();
        foreach (var kind in new[] { "libraries", "maps", "sounds" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(paths.Assets, kind), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(Path.Combine(paths.Assets, kind), file).Replace('\\', '/');
            if (name.Split('/').Any(part => part.StartsWith('.')) || !Supported(kind, name)) continue;
            ProjectPaths.SafePath(Path.Combine(paths.Assets, kind), name);
            var info = new FileInfo(file); var key = kind + "/" + name;
            var hash = changed?.Contains(key) != true && old.TryGetValue(key, out var prior) && prior.Size == info.Length && prior.LastWriteUtc == info.LastWriteTimeUtc ? prior.Sha256 : DataBootstrap.Hash(file);
            files.Add(new(kind, name, info.Length, hash, info.LastWriteTimeUtc));
        }
        if (files.GroupBy(f => f.Kind + "/" + f.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Case-insensitive asset names collide.");
        var fingerprint = string.Join('\n', files.OrderBy(f => f.Kind + "/" + f.Path, StringComparer.Ordinal).Select(f => $"{f.Kind}/{f.Path}:{f.Sha256}"));
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))).ToLowerInvariant()[..20];
        var updated = new AssetManifest(version, files.ToArray());
        AtomicText(manifestFile, JsonSerializer.Serialize(updated)); manifest = updated;
    }
    public IDisposable? TryRead()
    {
        lock (gate) { if (publishing) return null; readers++; return new ReadLease(this); }
    }
    private sealed class ReadLease(AssetCatalog owner) : IDisposable
    {
        public void Dispose() { lock (owner.gate) { owner.readers--; if (owner.readers == 0) owner.drained?.TrySetResult(); } }
    }
    public async Task BeginPublish(CancellationToken cancellation)
    {
        Task wait;
        lock (gate)
        {
            if (publishing) throw new InvalidOperationException("A resource publication is already in progress.");
            publishing = true; drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (readers == 0) drained.TrySetResult(); wait = drained.Task;
        }
        try { await wait.WaitAsync(TimeSpan.FromSeconds(30), cancellation); }
        catch { EndPublish(); throw; }
    }
    public void EndPublish() { lock (gate) { publishing = false; drained = null; } }
    public string ReadPath(string kind, string name)
    {
        var entry = Files.FirstOrDefault(f => f.Kind == kind && f.Path.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new FileNotFoundException("Resource not found.");
        var path = ProjectPaths.SafePath(Path.Combine(paths.Assets, kind), entry.Path); var info = new FileInfo(path);
        if (!info.Exists || info.Length != entry.Size || info.LastWriteTimeUtc != entry.LastWriteUtc) throw new InvalidOperationException("Unpublished resource changes detected. Publish resources before serving them.");
        return path;
    }
    public ResourceListing List(string? search = null)
    {
        var staged = Directory.Exists(Staging) ? Directory.GetFiles(Staging, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".tmp")).Select(p => Path.GetRelativePath(Staging, p).Replace('\\', '/')).ToArray() : [];
        var report = Path.Combine(paths.Assets, "import-report.json");
        return new(Version, Files.Where(f => string.IsNullOrEmpty(search) || f.Path.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray(), staged,
            File.Exists(report) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(report)) ?? [] : []);
    }
    public static bool Supported(string kind, string name) => kind switch
    {
        "libraries" => Path.GetExtension(name).Equals(".lib", StringComparison.OrdinalIgnoreCase),
        "maps" => Path.GetExtension(name).Equals(".map", StringComparison.OrdinalIgnoreCase),
        "sounds" => Path.GetExtension(name).ToLowerInvariant() is ".wav" or ".mp3" || name.Equals("SoundList.lst", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
    public async Task Stage(string kind, string name, Stream input, CancellationToken cancellation)
    {
        if (!Supported(kind, name)) throw new InvalidDataException("Unsupported resource type.");
        var target = ProjectPaths.SafePath(Path.Combine(Staging, kind), name); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid() + ".tmp";
        try
        {
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[65536]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    total += read; if (total > 512L * 1024 * 1024) throw new InvalidDataException("Upload exceeds 512 MiB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                }
            }
            Validate(kind, temp); File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Validate(string kind, string file)
    {
        if (kind == "maps") { _ = new MapReader(file); return; }
        if (kind == "libraries")
        {
            using var reader = new BinaryReader(File.OpenRead(file));
            var version = reader.ReadInt32(); var count = reader.ReadInt32(); var start = version == 3 ? 12L : 8L;
            if (version is < 2 or > 3 || count is < 0 or > 1000000 || start + count * 4L > reader.BaseStream.Length) throw new InvalidDataException("Invalid sprite library.");
        }
        else if (new FileInfo(file).Length == 0) throw new InvalidDataException("Empty sound resource.");
    }
    public async Task<PublishResult> Publish(GameRuntime runtime, CancellationToken cancellation)
    {
        await BeginPublish(cancellation);
        var changed = new List<(string target, string? backup)>(); var wasRunning = runtime.Ready; var stopped = false;
        var backupRoot = Path.Combine(paths.Runtime, "backups", "assets-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        var staged = Directory.Exists(Staging) ? Directory.GetFiles(Staging, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".tmp")).Order(StringComparer.Ordinal).ToArray() : [];
        var changedNames = staged.Select(p => Path.GetRelativePath(Staging, p).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (staged.Length == 0) throw new InvalidOperationException("No staged resources to publish.");
            await runtime.Stop(cancellation); stopped = true;
            foreach (var source in staged)
            {
                var relative = Path.GetRelativePath(Staging, source).Replace('\\', '/'); var split = relative.IndexOf('/');
                if (split < 0 || !Supported(relative[..split], relative[(split + 1)..])) throw new InvalidDataException("Invalid staged resource.");
                Validate(relative[..split], source);
                var target = ProjectPaths.SafePath(Path.Combine(paths.Assets, relative[..split]), relative[(split + 1)..]);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); string? backup = null;
                if (File.Exists(target)) { backup = ProjectPaths.SafePath(backupRoot, relative); Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(target, backup); }
                changed.Add((target, backup));
                File.Copy(source, target + ".publishing", true); File.Move(target + ".publishing", target, true);
            }
            Refresh(changedNames);
            if (wasRunning) await runtime.Start(cancellation);
            foreach (var file in staged) File.Delete(file);
            return new(Version, changed.Count, wasRunning);
        }
        catch
        {
            if (!stopped) throw;
            await runtime.Stop(CancellationToken.None);
            foreach (var (target, backup) in changed.AsEnumerable().Reverse())
                if (backup == null) File.Delete(target); else File.Copy(backup, target, true);
            Refresh(changedNames);
            if (wasRunning) await runtime.Start(CancellationToken.None);
            throw;
        }
        finally { foreach (var (target, _) in changed) if (File.Exists(target + ".publishing")) File.Delete(target + ".publishing"); EndPublish(); }
    }
    public static void AtomicText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp = path + "." + Guid.NewGuid() + ".tmp";
        try { File.WriteAllText(temp, content, new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
