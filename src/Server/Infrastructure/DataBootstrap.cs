using System.Security.Cryptography;
using System.Text.Json;
using Mir2.Hosting;

namespace Server.ConsoleHost;

public static class DataBootstrap
{
    public static void Prepare(ProjectPaths paths)
    {
        Directory.CreateDirectory(paths.Runtime);
        Directory.CreateDirectory(paths.World);
        var server = Path.Combine(paths.Import, "Crystal Database");
        var client = Path.Combine(paths.Import, "Crystal Client");
        foreach (var folder in new[] { "Configs", "Envir" })
        {
            var source = Path.Combine(server, folder);
            if (Directory.Exists(source)) CopyTree(source, Path.Combine(paths.World, folder));
        }
        if (!File.Exists(paths.Database) && !File.Exists(Path.Combine(server, "Server.MirDB")))
            throw new DirectoryNotFoundException($"没有 SQLite 数据库或首次导入资料，请将原始数据放入 {paths.Import}。");
        var completed = Path.Combine(paths.Assets, "import-report.json");
        if (!File.Exists(completed))
        {
            if (!Directory.Exists(client) || !Directory.Exists(server)) throw new DirectoryNotFoundException("缺少原始数据导入目录。");
            Console.WriteLine("Importing one authoritative resource store (server maps take precedence)...");
            CopyTree(Path.Combine(client, "Data"), Path.Combine(paths.Assets, "libraries"));
            CopyTree(Path.Combine(client, "Sound"), Path.Combine(paths.Assets, "sounds"));
            var maps = Path.Combine(paths.Assets, "maps");
            CopyTree(Path.Combine(server, "Maps"), maps);
            var conflicts = new List<string>();
            var known = Directory.EnumerateFiles(maps, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(maps, p), StringComparer.OrdinalIgnoreCase);
            foreach (var source in Directory.EnumerateFiles(Path.Combine(client, "Map"), "*", SearchOption.AllDirectories))
            {
                if (!Path.GetExtension(source).Equals(".map", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetRelativePath(Path.Combine(client, "Map"), source);
                if (known.TryGetValue(name, out var target))
                {
                    if (!Hash(source).Equals(Hash(target), StringComparison.Ordinal)) conflicts.Add($"{name}: server map retained; client source differs");
                }
                else
                {
                    target = Path.Combine(maps, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target); known[name] = target;
                }
            }
            File.WriteAllText(completed, JsonSerializer.Serialize(conflicts, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Unified resource import complete; {conflicts.Count} map conflicts recorded in {completed}.");
        }
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Localization"), "*.json"))
        {
            var target = Path.Combine(paths.World, "Localization", Path.GetFileName(file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(file, target);
        }
    }
    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).StartsWith('.') || Path.GetRelativePath(source, file).StartsWith("Previews" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(file, target);
        }
    }
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
