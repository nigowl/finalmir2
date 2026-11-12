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
        if (!Allowed(name)) throw new InvalidDataException("只能编辑配置文件和游戏脚本。");
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
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("文本编辑器仅支持不超过1兆字节的文件。");
        return new(name, File.ReadAllText(path), DataBootstrap.Hash(path));
    }
    public TextDocument Save(TextEdit edit)
    {
        var path = Resolve(edit.Path);
        if (Encoding.UTF8.GetByteCount(edit.Content) > 1024 * 1024 || edit.Content.Contains('\0')) throw new InvalidDataException("文本文件无效或超过大小上限。");
        var current = File.Exists(path) ? DataBootstrap.Hash(path) : "";
        if (current != edit.Version) throw new InvalidOperationException("文件已在读取后发生变化，请重新读取再保存。");
        if (File.Exists(path))
        {
            var backup = ProjectPaths.SafePath(Path.Combine(paths.Runtime, "backups", "text-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")), edit.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(path, backup);
        }
        AssetCatalog.AtomicText(path, edit.Content); return Read(edit.Path);
    }
}
