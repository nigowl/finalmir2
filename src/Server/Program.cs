// Native host: game, resource and management modules share one process and data store. GPL-3.0-only.
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;

if (args.Contains("--version")) { Console.WriteLine(ProgramVersion.Current); return; }

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory, Args = [] });
var paths = new ProjectPaths(versionDirectory: builder.Configuration["Mir2:VersionDirectory"]);
Directory.CreateDirectory(paths.Data);
using var instanceLock = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
if (args.Contains("--translate-chinese") || args.Contains("--organize-drop-scripts") || args.Contains("--compose-drop-scripts") || args.Contains("--balance-supplies"))
{
    if (!File.Exists(paths.Database)) throw new FileNotFoundException("请先导入默认世界，再执行中文迁移。", paths.Database);
    Directory.SetCurrentDirectory(paths.World);
    using var database = new SqliteDatabaseStorage(paths.Database);
    DatabaseStorage.Current = database;
    try { Console.WriteLine((args.Contains("--organize-drop-scripts") || args.Contains("--compose-drop-scripts") || args.Contains("--balance-supplies"))
        ? DropScriptOrganization.Apply(paths, database, args.Contains("--check"), args.Contains("--compose-drop-scripts") || args.Contains("--balance-supplies"), args.Contains("--balance-supplies"))
        : ChineseWorldTranslation.Apply(paths, database, args.Contains("--check"))); }
    finally { DatabaseStorage.Current = null; }
    return;
}
DataBootstrap.Prepare(paths);
if (args.Contains("--prepare-data")) { _ = new AssetCatalog(paths); Console.WriteLine("Canonical data ready."); return; }
Directory.SetCurrentDirectory(paths.World);
using var storage = new SqliteDatabaseStorage(paths.Database);
var seed = Path.Combine(paths.Import, "Crystal Database");
if (Directory.Exists(seed)) storage.Import(seed);
storage.BackupBeforePlayerUpgrade(Path.Combine(paths.Runtime, "backups"));
DatabaseStorage.Current = storage; Packet.IsServer = true;
var catalog = new AssetCatalog(paths);
if (args.Contains("--prepare-public-shop"))
{
    try
    {
        if (!Server.MirEnvir.Envir.Main.LoadDB()) throw new InvalidDataException("世界定义读取失败，未新增补给规格。");
        Console.WriteLine(new WorldDefinitions(paths, catalog).PreparePublicShop());
    }
    finally { DatabaseStorage.Current = null; }
    return;
}
await using var app = ResourceService.Build(paths, catalog, builder);
Console.WriteLine($"程序版本：{ProgramVersion.Current}\nGame version: {paths.VersionName}\nVersion directory: {paths.Data}\nSQLite: {paths.Database}\nCanonical assets: {paths.Assets}\nManagement key: {paths.AdminKeyFile}");
try { await app.RunAsync(); }
finally { DatabaseStorage.Current = null; }
