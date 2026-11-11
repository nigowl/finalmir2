// Modified for the macOS headless host, 2026-11-11. GPL-3.0-only.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Server;
using Server.ConsoleHost;
using Server.MirEnvir;

var dataRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("MIR2_DATA_ROOT") ?? "Data/ref");
var runtimeRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("MIR2_RUNTIME_ROOT") ?? "Data/runtime");
var source = Path.Combine(dataRoot, "Crystal Database");
if (!File.Exists(Path.Combine(source, "Server.MirDB")) || !Directory.Exists(Path.Combine(source, "Maps")))
{
    Console.Error.WriteLine($"Missing server data: {source}. Set MIR2_DATA_ROOT to the directory containing Crystal Database and Crystal Client.");
    return 1;
}

var working = Path.Combine(runtimeRoot, "server");
Directory.CreateDirectory(working);
using var instanceLock = new FileStream(Path.Combine(working, "server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
foreach (var folder in new[] { "Configs", "Envir" })
foreach (var file in Directory.EnumerateFiles(Path.Combine(source, folder), "*", SearchOption.AllDirectories))
{
    if (Path.GetFileName(file).StartsWith('.')) continue;
    var target = Path.Combine(working, Path.GetRelativePath(source, file));
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    if (!File.Exists(target)) File.Copy(file, target);
}
var maps = Path.Combine(working, "Maps");
if (!Directory.Exists(maps)) Directory.CreateSymbolicLink(maps, Path.Combine(source, "Maps"));
var localization = Path.Combine(working, "Localization");
Directory.CreateDirectory(localization);
foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Localization"), "*.json"))
    File.Copy(file, Path.Combine(localization, Path.GetFileName(file)), overwrite: true);

Directory.SetCurrentDirectory(working);
var sqlitePath = Path.Combine(runtimeRoot, "mir2.sqlite3");
using var storage = new SqliteDatabaseStorage(sqlitePath);
storage.Import(source);
DatabaseStorage.Current = storage;
Packet.IsServer = true;
Settings.Load();
Settings.IPAddress = Environment.GetEnvironmentVariable("MIR2_BIND_ADDRESS") ?? "127.0.0.1";
Settings.Port = ushort.Parse(Environment.GetEnvironmentVariable("MIR2_GAME_PORT") ?? "7000");
Settings.CheckVersion = false;
Settings.StartHTTPService = false;
Settings.AllowStartGame = true;
// The original monster workers spin while waiting; a local host uses one game loop.
Settings.Multithreaded = false;
Console.WriteLine($"SQLite: {sqlitePath}");
Console.WriteLine($"Server data: {source}");

using var shutdown = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Set(); };
using var term = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    shutdown.Set();
});
var environment = Envir.Main;
environment.StatusPortEnabled = false;
await using var resources = ResourceService.Build(dataRoot, Settings.Port,
    int.Parse(Environment.GetEnvironmentVariable("MIR2_RESOURCE_PORT") ?? "5080"), System.Net.IPAddress.Parse(Settings.IPAddress));
try
{
    await resources.StartAsync();
    environment.Start();
    var startup = Stopwatch.StartNew();
    while (!environment.NetworkStarted && environment.Running && startup.Elapsed < TimeSpan.FromMinutes(2) && !shutdown.IsSet)
    {
        DrainLogs();
        shutdown.Wait(100);
    }
    if (!environment.NetworkStarted)
        throw new InvalidOperationException("Game server did not start. Review the messages above and the data configuration.");

    Console.WriteLine($"Game server ready on {Settings.IPAddress}:{Settings.Port}. Press Ctrl+C to save and stop.");
    Console.WriteLine($"Native client resources: http://{Settings.IPAddress}:{Environment.GetEnvironmentVariable("MIR2_RESOURCE_PORT") ?? "5080"}");
    while (!shutdown.Wait(250))
    {
        DrainLogs();
        if (!environment.Running) throw new InvalidOperationException("The game loop stopped unexpectedly.");
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
finally
{
    environment.Stop();
    await resources.StopAsync();
    DrainLogs();
    DatabaseStorage.Current = null;
}

static void DrainLogs()
{
    var queue = MessageQueue.Instance;
    foreach (var log in new[] { queue.MessageLog, queue.DebugLog, queue.ChatLog })
        while (log.TryDequeue(out var message)) Console.Write(message);
}
