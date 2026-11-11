using System.Text;
using Mir2.Hosting;
using Mir2.Management;

namespace Server.ConsoleHost;

public sealed class DevelopmentFiles(ProjectPaths paths)
{
    private static bool Allowed(string name) => (name.StartsWith("Configs/", StringComparison.Ordinal) || name.StartsWith("Envir/", StringComparison.Ordinal)) &&
        Path.GetExtension(name).ToLowerInvariant() is ".ini" or ".txt" or ".json";
    private string Resolve(string name)
    {
        if (!Allowed(name)) throw new InvalidDataException("Only configuration and game script files may be edited.");
        return ProjectPaths.SafePath(paths.World, name);
    }
    public string[] List(string? search) => new[] { "Configs", "Envir" }.SelectMany(folder =>
        Directory.EnumerateFiles(Path.Combine(paths.World, folder), "*", SearchOption.AllDirectories))
        .Select(file => Path.GetRelativePath(paths.World, file).Replace('\\', '/')).Where(Allowed)
        .Where(name => string.IsNullOrWhiteSpace(search) || name.Contains(search, StringComparison.OrdinalIgnoreCase)).Order().ToArray();
    public TextDocument Read(string name)
    {
        var path = Resolve(name);
        if (!File.Exists(path)) return new(name, "", "");
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Text editor supports files up to 1 MiB.");
        return new(name, File.ReadAllText(path), DataBootstrap.Hash(path));
    }
    public TextDocument Save(TextEdit edit)
    {
        var path = Resolve(edit.Path);
        if (Encoding.UTF8.GetByteCount(edit.Content) > 1024 * 1024 || edit.Content.Contains('\0')) throw new InvalidDataException("Invalid or oversized text file.");
        var current = File.Exists(path) ? DataBootstrap.Hash(path) : "";
        if (current != edit.Version) throw new InvalidOperationException("The file changed since it was opened. Reload before saving.");
        if (File.Exists(path))
        {
            var backup = ProjectPaths.SafePath(Path.Combine(paths.Runtime, "backups", "text-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")), edit.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(path, backup);
        }
        AssetCatalog.AtomicText(path, edit.Content); return Read(edit.Path);
    }
}
